using System.Security.Authentication.ExtendedProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

var invoices = new List<Invoice>
{
    new("INV-1001", "Allied Van Lines", DateTimeOffset.UtcNow.AddHours(-2), 7500.00m, 2450.50m, false),
    new("INV-1002", "Atlas World Group", DateTimeOffset.UtcNow.AddHours(-1), 12400.00m, 4120.00m, false),
    new("INV-1001", "Allied Van Brah", DateTimeOffset.UtcNow.AddHours(-3), 7500.00m, 2450.50m, false)
};
app.MapGet("/invoices", () =>
{
    var seen = new HashSet<string>();
    var auditedInvoices = new List<Invoice>();
    var sortedInvoices = invoices.OrderBy(inv => inv.CreatedAt);

    foreach (var inv in sortedInvoices)
    {
        bool IsDuplicate = !seen.Add(inv.InvoiceNumber);
        auditedInvoices.Add(inv with { IsDuplicate = IsDuplicate });
        // 1. Try to add inv.InvoiceNumber into 'seen'
        // 2. If it's already there, create a copy with IsDuplicate = true
        // 3. Otherwise, keep IsDuplicate = false
    }

    return auditedInvoices;
});

app.MapPost("/invoices", (Invoice newInvoice) =>
{
    // Step 1: Validate input
    if (newInvoice.Amount <= 0)
    {
        return Results.BadRequest("Invoice amount must be greater than zero.");
    }

    // Step 2: Add to list
    invoices.Add(newInvoice);

    // Step 3: Return 201 Created
    return Results.Created($"/invoices/{newInvoice.InvoiceNumber}", newInvoice);
});


app.Run();


public record Invoice(
    string InvoiceNumber,
    string CarrierName,
    DateTimeOffset CreatedAt,
    decimal WeightLbs,
    decimal Amount,
    bool IsDuplicate
);