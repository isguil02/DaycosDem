using Npgsql;

var builder = WebApplication.CreateBuilder(args);


// Read the connection string from appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
// Allow our Svelte frontend to talk to this backend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors();

// GET /invoices: Pulls live from PostgreSQL and runs duplicate audit
app.MapGet("/invoices", async () =>
{
    var dbInvoices = new List<Invoice>();

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    var sql = """
        SELECT i.id, i.invoice_number, c.name, i.created_at, i.weight_lbs, i.amount
        FROM invoices i
        INNER JOIN carriers c ON i.carrier_id = c.id;
    """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    await using var reader = await cmd.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        dbInvoices.Add(new Invoice(
            reader.GetInt32(0),
            reader.GetString(1),                     // invoice_number
            reader.GetString(2),                     // carrier name (from carriers table!)
            reader.GetFieldValue<DateTimeOffset>(3), // created_at (UTC)
            reader.GetDecimal(4),                    // weight_lbs
            reader.GetDecimal(5),                    // amount
            false                                    // isDuplicate (audited below)
        ));
    }

    // Run our duplicate detection engine on the live database records
    var seen = new HashSet<string>();
    var auditedInvoices = new List<Invoice>();
    var sortedInvoices = dbInvoices.OrderBy(inv => inv.CreatedAt);

    foreach (var inv in sortedInvoices)
    {
        bool isDuplicate = !seen.Add($"{inv.CarrierName}:{inv.InvoiceNumber}"); //Added carrier name to ensure 2 companies with a matching invoice number don't get marked as duplicate
        auditedInvoices.Add(inv with { IsDuplicate = isDuplicate });
    }

    return Results.Ok(auditedInvoices);
});


// POST /invoices: Saves a new invoice into PostgreSQL
app.MapPost("/invoices", async (CreateInvoiceRequest request) =>
{
    // 1. Validation
    if (request.Amount <= 0)
    {
        return Results.BadRequest("Invoice amount must be greater than zero.");
    }
    // Daycos Rule: Money cannot have fractional cents (> 2 decimals)
    if (decimal.Round(request.Amount, 2) != request.Amount)
    {
        return Results.BadRequest("Invoice amount cannot have more than 2 decimal places (e.g. $10.50).");
    }
    if (request.WeightLbs <= 0)
    {
        return Results.BadRequest("Weight must be greater than zero.");
    }

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    // 2. Find carrier ID or create new carrier if it doesn't exist yet
    int carrierId;
    await using var findCmd = new NpgsqlCommand("SELECT id FROM carriers WHERE name = @name", conn);
    findCmd.Parameters.AddWithValue("name", request.CarrierName);
    var existingId = await findCmd.ExecuteScalarAsync();

    if (existingId != null)
    {
        carrierId = (int)existingId;
    }
    else
    {
        var code = Guid.NewGuid().ToString("N")[..4].ToUpper(); // 4-char unique code
        await using var insertCarrierCmd = new NpgsqlCommand(
            "INSERT INTO carriers (name, carrier_code) VALUES (@name, @code) RETURNING id", conn);
        insertCarrierCmd.Parameters.AddWithValue("name", request.CarrierName);
        insertCarrierCmd.Parameters.AddWithValue("code", code);
        carrierId = (int)(await insertCarrierCmd.ExecuteScalarAsync())!;
    }

    // 3. Insert invoice linked to that carrier_id (SQL-injection safe parameters)
    var insertSql = """
        INSERT INTO invoices (carrier_id, invoice_number, weight_lbs, amount)
        VALUES (@carrierId, @invoiceNumber, @weightLbs, @amount)
        RETURNING id, created_at;
    """;

    await using var cmd = new NpgsqlCommand(insertSql, conn);
    cmd.Parameters.AddWithValue("carrierId", carrierId);
    cmd.Parameters.AddWithValue("invoiceNumber", request.InvoiceNumber);
    cmd.Parameters.AddWithValue("weightLbs", request.WeightLbs);
    cmd.Parameters.AddWithValue("amount", request.Amount);

    await using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();

    var newId = reader.GetInt32(0);
    var createdAt = reader.GetFieldValue<DateTimeOffset>(1);

    // 4. Return 201 Created
    return Results.Created($"/invoices/{request.InvoiceNumber}", new
    {
        Id = newId,
        request.InvoiceNumber,
        request.CarrierName,
        request.WeightLbs,
        request.Amount,
        CreatedAt = createdAt
    });
});
// POST /invoices/delete: Compliant soft-delete with reason & 90-day retention
app.MapPost("/invoices/delete", async (DeleteInvoiceRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Reason))
    {
        return Results.BadRequest("A deletion reason is required for DoD audit compliance.");
    }

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        // 1. Fetch the invoice to archive
        var selectSql = "SELECT id, invoice_number, carrier_id, weight_lbs, amount, created_at FROM invoices WHERE id = @id;";
        await using var selectCmd = new NpgsqlCommand(selectSql, conn, tx);
        selectCmd.Parameters.AddWithValue("id", request.InvoiceId);

        await using var reader = await selectCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound($"Invoice with ID #{request.InvoiceId} not found.");
        }

        var id = reader.GetInt32(0);
        var invNum = reader.GetString(1);
        var carrierId = reader.GetInt32(2);
        var weight = reader.GetDecimal(3);
        var amount = reader.GetDecimal(4);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(5);
        await reader.CloseAsync();

        // 2. Insert into deleted_invoices archive table
        var archiveSql = """
            INSERT INTO deleted_invoices 
            (id, invoice_number, carrier_id, weight_lbs, amount, original_created_at, deletion_reason)
            VALUES (@id, @invNum, @carrierId, @weight, @amount, @createdAt, @reason);
        """;
        await using var archiveCmd = new NpgsqlCommand(archiveSql, conn, tx);
        archiveCmd.Parameters.AddWithValue("id", id);
        archiveCmd.Parameters.AddWithValue("invNum", invNum);
        archiveCmd.Parameters.AddWithValue("carrierId", carrierId);
        archiveCmd.Parameters.AddWithValue("weight", weight);
        archiveCmd.Parameters.AddWithValue("amount", amount);
        archiveCmd.Parameters.AddWithValue("createdAt", createdAt);
        archiveCmd.Parameters.AddWithValue("reason", request.Reason);
        await archiveCmd.ExecuteNonQueryAsync();

        // 3. Delete from active invoices
        await using var deleteCmd = new NpgsqlCommand("DELETE FROM invoices WHERE id = @id;", conn, tx);
        deleteCmd.Parameters.AddWithValue("id", request.InvoiceId);
        await deleteCmd.ExecuteNonQueryAsync();

        // 4. Auto-purge records older than 90 days (Compliance policy)
        await using var purgeCmd = new NpgsqlCommand(
            "DELETE FROM deleted_invoices WHERE deleted_at < NOW() - INTERVAL '90 days';", conn, tx);
        await purgeCmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        return Results.Ok(new { message = $"Invoice #{id} ({invNum}) safely archived." });
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        return Results.Problem($"Audit deletion failed: {ex.Message}");
    }
});

// GET /carriers: Safe list of carriers for the dropdown
app.MapGet("/carriers", async () =>
{
    var carriers = new List<object>();
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    var sql = "SELECT id, name, contact_phone FROM carriers ORDER BY name;";
    await using var cmd = new NpgsqlCommand(sql, conn);
    await using var reader = await cmd.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        carriers.Add(new
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            ContactPhone = reader.IsDBNull(2) ? null : reader.GetString(2)
        });
    }

    return Results.Ok(carriers);
});

// POST /carriers/login: Authenticate carrier with password
app.MapPost("/carriers/login", async (CarrierLoginRequest request) =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    var sql = "SELECT id, name, contact_phone, password_hash FROM carriers WHERE LOWER(name) = LOWER(@name);";
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("name", request.CarrierName);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return Results.NotFound("Carrier not registered with Daycos.");
    }

    var storedPassword = reader.IsDBNull(3) ? "daycos123" : reader.GetString(3);
    if (storedPassword != request.Password)
    {
        return Results.Json(new { message = "Invalid password. Use phone number for recovery." }, statusCode: 401);
    }

    return Results.Ok(new
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        ContactPhone = reader.IsDBNull(2) ? null : reader.GetString(2)
    });
});

app.Run();

public record Invoice(
    int id,
    string InvoiceNumber,
    string CarrierName,
    DateTimeOffset CreatedAt,
    decimal WeightLbs,
    decimal Amount,
    bool IsDuplicate
);


public record CreateInvoiceRequest(
    string InvoiceNumber,
    string CarrierName,
    decimal WeightLbs,
    decimal Amount
);

public record DeleteInvoiceRequest(int InvoiceId, string Reason);
public record CarrierLoginRequest(string CarrierName, string Password);