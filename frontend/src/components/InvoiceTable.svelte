<script>
    import { appState } from "../state.svelte.js";
    let {
        invoices = [],
        loading = false,
        onRefresh,
        selectedTimeZone = "UTC",
    } = $props();
    let duplicateCount = $derived(
        invoices.filter((inv) => inv.isDuplicate).length,
    );

    let totalAmount = $derived(
        invoices
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
        {#if onRefresh}
            <button class="btn-refresh" onclick={onRefresh} disabled={loading}>
                {loading ? "Refreshing..." : "🔄 Refresh Data"}
            </button>
        {/if}
    </div>

    <!-- Live Stats Mini-Bar -->
    <div class="table-stats">
        <span class="TotalInvs">Total: <strong>{invoices.length}</strong></span>
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
            {#each invoices as inv}
                <tr class={inv.isDuplicate ? "dup-row" : ""}>
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
</style>
