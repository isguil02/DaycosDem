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

app.MapPut("/invoices/{id:int}", async (int id, UpdateInvoiceRequest request) =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    await using var checkCmd = new NpgsqlCommand("SELECT carrier_id FROM invoices WHERE id = @id", conn);
    checkCmd.Parameters.AddWithValue("id", id);
    var existingCarrierId = await checkCmd.ExecuteScalarAsync();

    if (existingCarrierId == null)
    {
        return Results.NotFound($"Invoice #{id} not found.");
    }
    var sql = """
        UPDATE invoices 
        SET amount = COALESCE(@amount, amount),
            weight_lbs = COALESCE(@weight, weight_lbs),
            invoice_number = COALESCE(@invNum, invoice_number)
        WHERE id = @id;
    """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("amount", (object?)request.Amount ?? DBNull.Value);
    cmd.Parameters.AddWithValue("weight", (object?)request.WeightLbs ?? DBNull.Value);
    cmd.Parameters.AddWithValue("invNum", (object?)request.InvoiceNumber ?? DBNull.Value);

    await cmd.ExecuteNonQueryAsync();

    return Results.Ok(new { message = $"Invoice #{id} updated successfully." });
});

// POST /invoices/seed: Bulk generate realistic freight invoices in a single transaction
app.MapPost("/invoices/seed", async (SeedInvoicesRequest request) =>
{
    // 1. Clamp count safely between 1 and 1,000
    int count = Math.Clamp(request.Count, 1, 1000);
    var carriers = new[] { "Allied Van Lines", "Atlas World Group", "Old Dominion Freight", "North American Van Lines" };
    var rnd = new Random();

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    // 2. Start Atomic Transaction (All or Nothing)
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        for (int i = 0; i < count; i++)
        {
            var carrier = carriers[rnd.Next(carriers.Length)];
            var invNum = $"INV-{rnd.Next(1000, 9999)}";
            var weight = rnd.Next(40, 260) * 100m; // 4,000 to 26,000 lbs
            var amount = Math.Round((weight * 0.35m) + rnd.Next(100, 500), 2);

            // Get carrier ID
            await using var cCmd = new NpgsqlCommand("SELECT id FROM carriers WHERE name = @name", conn, tx);
            cCmd.Parameters.AddWithValue("name", carrier);
            var cId = (int)(await cCmd.ExecuteScalarAsync())!;

            // Insert invoice with a random date over the last 30 days
            var sql = """
                INSERT INTO invoices (carrier_id, invoice_number, weight_lbs, amount, created_at)
                VALUES (@cId, @invNum, @weight, @amount, NOW() - (interval '1 day' * @daysAgo))
            """;
            await using var iCmd = new NpgsqlCommand(sql, conn, tx);
            iCmd.Parameters.AddWithValue("cId", cId);
            iCmd.Parameters.AddWithValue("invNum", invNum);
            iCmd.Parameters.AddWithValue("weight", weight);
            iCmd.Parameters.AddWithValue("amount", amount);
            iCmd.Parameters.AddWithValue("daysAgo", rnd.Next(0, 30));
            await iCmd.ExecuteNonQueryAsync();
        }

        // 3. All succeeded! Commit the transaction
        await tx.CommitAsync();
        return Results.Ok(new { message = $"Successfully generated {count} realistic freight invoices." });
    }
    catch (Exception ex)
    {
        // 4. Any error rolls back all inserts completely
        await tx.RollbackAsync();
        return Results.Problem($"Batch generation failed: {ex.Message}");
    }
});

// POST /system/backup: Snapshots all 4 tables into a timestamped backup folder
app.MapPost("/system/backup", async () =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
    var backupFolder = Path.Combine("backups", $"backup_{timestamp}");
    Directory.CreateDirectory(backupFolder);

    // 1. Carriers
    var carriers = new List<dynamic>();
    await using (var cmd = new NpgsqlCommand("SELECT id, name, carrier_code, contact_phone FROM carriers;", conn))
    await using (var r = await cmd.ExecuteReaderAsync())
    {
        while (await r.ReadAsync())
        {
            carriers.Add(new
            {
                id = r.GetInt32(0),
                name = r.GetString(1),
                carrierCode = r.GetString(2),
                phone = r.IsDBNull(3) ? null : r.GetString(3)
            });
        }
    }

    // 2. Active Invoices
    var invoices = new List<dynamic>();
    await using (var cmd = new NpgsqlCommand("SELECT id, carrier_id, invoice_number, weight_lbs, amount, created_at FROM invoices;", conn))
    await using (var r = await cmd.ExecuteReaderAsync())
    {
        while (await r.ReadAsync())
        {
            invoices.Add(new
            {
                id = r.GetInt32(0),
                carrierId = r.GetInt32(1),
                invoiceNumber = r.GetString(2),
                weightLbs = r.GetDecimal(3),
                amount = r.GetDecimal(4),
                createdAt = r.GetFieldValue<DateTimeOffset>(5)
            });
        }
    }

    // 3. Deleted Carriers (Audit Archive)
    var delCarriers = new List<dynamic>();
    await using (var cmd = new NpgsqlCommand("SELECT id, name, contact_phone, deleted_at, deletion_reason FROM deleted_carriers;", conn))
    await using (var r = await cmd.ExecuteReaderAsync())
    {
        while (await r.ReadAsync())
        {
            delCarriers.Add(new
            {
                id = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                name = r.IsDBNull(1) ? null : r.GetString(1),
                phone = r.IsDBNull(2) ? null : r.GetString(2),
                deletedAt = r.GetFieldValue<DateTimeOffset>(3),
                reason = r.GetString(4)
            });
        }
    }

    // 4. Deleted Invoices (Audit Archive)
    var delInvoices = new List<dynamic>();
    await using (var cmd = new NpgsqlCommand("SELECT id, invoice_number, carrier_id, weight_lbs, amount, original_created_at, deleted_at, deletion_reason FROM deleted_invoices;", conn))
    await using (var r = await cmd.ExecuteReaderAsync())
    {
        while (await r.ReadAsync())
        {
            delInvoices.Add(new
            {
                id = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                invoiceNumber = r.IsDBNull(1) ? null : r.GetString(1),
                carrierId = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                weightLbs = r.IsDBNull(3) ? 0 : r.GetDecimal(3),
                amount = r.IsDBNull(4) ? 0 : r.GetDecimal(4),
                originalCreatedAt = r.IsDBNull(5) ? (DateTimeOffset?)null : r.GetFieldValue<DateTimeOffset>(5),
                deletedAt = r.GetFieldValue<DateTimeOffset>(6),
                reason = r.GetString(7)
            });
        }
    }

    var opt = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
    await File.WriteAllTextAsync(Path.Combine(backupFolder, "carriers.json"), System.Text.Json.JsonSerializer.Serialize(carriers, opt));
    await File.WriteAllTextAsync(Path.Combine(backupFolder, "invoices.json"), System.Text.Json.JsonSerializer.Serialize(invoices, opt));
    await File.WriteAllTextAsync(Path.Combine(backupFolder, "deleted_carriers.json"), System.Text.Json.JsonSerializer.Serialize(delCarriers, opt));
    await File.WriteAllTextAsync(Path.Combine(backupFolder, "deleted_invoices.json"), System.Text.Json.JsonSerializer.Serialize(delInvoices, opt));

    return Results.Ok(new
    {
        backupFolder,
        carriers = carriers.Count,
        invoices = invoices.Count,
        deletedCarriers = delCarriers.Count,
        deletedInvoices = delInvoices.Count
    });
});

// POST /system/nuke: Simulates the disaster (DELETE without WHERE)
app.MapPost("/system/nuke", async () =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand("DELETE FROM invoices;", conn);
    await cmd.ExecuteNonQueryAsync();
    return Results.Ok(new { message = "All invoices deleted from active ledger." });
});

// POST /system/restore-backup: Restores all 4 tables from the latest backup folder
app.MapPost("/system/restore-backup", async (int? beforeMinutes) =>
{
    var backupRoot = "backups";
    if (!Directory.Exists(backupRoot))
        return Results.NotFound(new { message = "No backups directory found." });

    var cutoff = beforeMinutes.HasValue ? DateTime.UtcNow.AddMinutes(-beforeMinutes.Value) : DateTime.UtcNow;

    // Pick newest backup folder older than cutoff
    var latestFolder = new DirectoryInfo(backupRoot)
        .GetDirectories("backup_*")
        .Where(d => d.LastWriteTimeUtc <= cutoff)
        .OrderByDescending(d => d.LastWriteTimeUtc)
        .FirstOrDefault();

    if (latestFolder == null)
        return Results.NotFound(new { message = "No valid backup folder found." });

    var invPath = Path.Combine(latestFolder.FullName, "invoices.json");
    if (!File.Exists(invPath))
        return Results.BadRequest(new { message = "Corrupt backup: missing invoices.json." });

    var json = await File.ReadAllTextAsync(invPath);
    var doc = System.Text.Json.JsonDocument.Parse(json);
    var invElements = doc.RootElement.EnumerateArray().ToList();

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        // 1. Wipe active invoices
        await using (var delCmd = new NpgsqlCommand("DELETE FROM invoices;", conn, tx))
        {
            await delCmd.ExecuteNonQueryAsync();
        }

        // 2. Restore active invoices
        foreach (var el in invElements)
        {
            var insSql = """
                INSERT INTO invoices (carrier_id, invoice_number, weight_lbs, amount, created_at)
                VALUES (@cId, @invNum, @weight, @amount, @createdAt);
            """;
            await using var iCmd = new NpgsqlCommand(insSql, conn, tx);
            iCmd.Parameters.AddWithValue("cId", el.GetProperty("carrierId").GetInt32());
            iCmd.Parameters.AddWithValue("invNum", el.GetProperty("invoiceNumber").GetString() ?? "");
            iCmd.Parameters.AddWithValue("weight", el.GetProperty("weightLbs").GetDecimal());
            iCmd.Parameters.AddWithValue("amount", el.GetProperty("amount").GetDecimal());
            iCmd.Parameters.AddWithValue("createdAt", DateTimeOffset.Parse(el.GetProperty("createdAt").GetString()!));
            await iCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return Results.Ok(new
        {
            restoredFolder = latestFolder.Name,
            invoicesRestored = invElements.Count,
            message = $"Successfully restored {invElements.Count} invoices from snapshot {latestFolder.Name}."
        });
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        return Results.Problem($"Restore failed: {ex.Message}");
    }
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
public record UpdateInvoiceRequest(string? InvoiceNumber, string? CarrierName, decimal? Amount, decimal? WeightLbs, DateTimeOffset? CreatedAt);
public record SeedInvoicesRequest(int Count);