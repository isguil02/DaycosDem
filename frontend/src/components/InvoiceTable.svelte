<script>
    // @ts-nocheck
    import { appState, fetchInvoices } from "../state.svelte.js";
    let { invoices = null } = $props();

    // If a prop was passed (e.g. Carrier view), use it; otherwise use global appState.invoices!
    let baseInvoices = $derived(invoices ?? appState.invoices);

    let displayInvoices = $derived(
        appState.filter
            ? baseInvoices.filter((inv) => {
                  const f = appState.filter.toLowerCase().trim();

                  // 1. If filter is "duplicates", show only duplicates
                  if (f === "duplicates" || f === "duplicate") {
                      return inv.isDuplicate;
                  }

                  // 2. If filter is ">4000", show invoices with amount greater than 4000
                  if (f.startsWith(">")) {
                      const minPrice = parseFloat(f.slice(1));
                      return Number(inv.amount) > minPrice;
                  }

                  // 3. Otherwise, search carrier name or invoice number
                  return (
                      inv.carrierName.toLowerCase().includes(f) ||
                      inv.invoiceNumber.toLowerCase().includes(f)
                  );
              })
            : baseInvoices,
    );
    let duplicateCount = $derived(
        displayInvoices.filter((inv) => inv.isDuplicate).length,
    );

    let totalAmount = $derived(
        displayInvoices
            .reduce((sum, inv) => sum + Number(inv.amount), 0)
            .toLocaleString("en-US", { style: "currency", currency: "USD" }),
    );

    function formatDate(utcString = "") {
        if (!utcString) return "—";
        return new Date(utcString).toLocaleString("en-US", {
            timeZone: appState.timeZone,
            month: "short",
            day: "numeric",
            year: "numeric",
            hour: "numeric",
            minute: "2-digit",
            hour12: true,
        });
    }
</script>

<div class="card table-card">
    <div class="table-header">
        <h2>Audited Invoices (PostgreSQL)</h2>
        <button
            class="btn-refresh"
            onclick={fetchInvoices}
            disabled={appState.loading}
        >
            {appState.loading ? "Refreshing..." : "🔄 Refresh Data"}
        </button>
    </div>

    <!-- Live Stats Mini-Bar -->
    <div class="table-stats">
        <span class="TotalInvs"
            >Total: <strong>{displayInvoices.length}</strong></span
        >
        <span class="TotalSpend"
            >Total Spend: <strong>{totalAmount}</strong></span
        >
        <span class="stat-alert"
            >Duplicates: <strong>{duplicateCount}</strong></span
        >
    </div>

    <table>
        <thead>
            <tr>
                <th>ID</th>
                <th>Invoice #</th>
                <th>Carrier</th>
                <th>Created At (CST)</th>
                <th>Weight</th>
                <th>Amount</th>
                <th>Audit Status</th>
            </tr>
        </thead>
        <tbody>
            {#each displayInvoices as inv}
                <tr
                    class={inv.isDuplicate ? "dup-row" : ""}
                    class:selected={appState.selectedInvoiceID === inv.id}
                    onclick={() => (appState.selectedInvoiceID = inv.id)}
                    style="cursor: pointer;"
                >
                    <td><code>#{inv.id}</code></td>
                    <td>{inv.invoiceNumber}</td>
                    <td>{inv.carrierName}</td>
                    <td>{formatDate(inv.createdAt)}</td>
                    <td>{inv.weightLbs} lbs</td>
                    <td>${inv.amount}</td>
                    <td>
                        {#if inv.isDuplicate}
                            <span class="badge red">⚠️ DUPLICATE</span>
                        {:else}
                            <span class="badge green">✓ Verified</span>
                        {/if}
                    </td>
                </tr>
            {/each}
        </tbody>
    </table>
</div>

<style>
    .card {
        background: var(--card-bg);
        color: var(--text-main);
        padding: 1.25rem;
        border-radius: 8px;
        border: 1px solid var(--border);
        box-shadow: 0 1px 3px rgba(0, 0, 0, 0.2);
        min-height: 600px;
    }

    .table-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 1rem;
    }

    .table-header h2 {
        margin: 0;
    }

    .btn-refresh {
        background: var(--card-bg);
        color: var(--text-main);
        border: 1px solid var(--border);
        padding: 0.4rem 0.8rem;
        border-radius: 6px;
        font-size: 0.85rem;
        font-weight: 600;
        cursor: pointer;
        transition: background 0.15s ease;
    }

    .btn-refresh:hover {
        background: var(--border);
    }

    table {
        width: 100%;
        border-collapse: collapse;
        text-align: left;
    }

    th,
    td {
        padding: 0.75rem;
        border-bottom: 1px solid var(--border);
    }

    th {
        color: var(--text-muted);
        font-size: 0.85rem;
        text-transform: uppercase;
    }

    .dup-row {
        background-color: var(--dup-row);
    }

    .badge {
        padding: 0.25rem 0.5rem;
        border-radius: 4px;
        font-size: 0.75rem;
        font-weight: bold;
    }

    .badge.red {
        background: var(--dup-bg);
        color: var(--dup-text);
    }

    .badge.green {
        background: var(--verified-bg);
        color: var(--verified-text);
    }

    .table-stats {
        display: flex;
        gap: 1.5rem;
        padding: 0.5rem 0.75rem;
        background: color-mix(in srgb, var(--border) 25%, transparent);
        border-radius: 6px;
        margin-bottom: 1rem;
        font-size: 0.85rem;
        justify-content: space-between;
    }

    .stat-alert {
        color: var(--dup-text);
        font-weight: bold;
    }
    tr.selected {
        outline: 2px solid var(--border-accent);
    }
</style>
