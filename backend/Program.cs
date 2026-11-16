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