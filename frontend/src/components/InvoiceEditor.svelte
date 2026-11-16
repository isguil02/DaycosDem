<script>
    import { fetchInvoices } from "../state.svelte.js";
    /** @type {any[]}*/
    let invoices = $state([]);
    let loading = $state(true);

    // Form input variables for adding a new invoice
    let invoiceNumber = $state("");
    let carrierName = $state("");
    let weightLbs = $state("");
    let amount = $state("");
    let formError = $state("");
    let selectMode = $state("add");

    // Submit invoice to C# POST /invoices
    /** @param {any} e **/
    async function handleSubmit(e) {
        e.preventDefault();
        formError = ""; // Clear previous error

        // Client-side check: Disallow 3+ decimal places
        const decimalParts = amount.toString().split(".");
        if (decimalParts[1] && decimalParts[1].length > 2) {
            formError =
                "Invalid currency: Amount cannot have more than 2 decimal places (e.g. 10.50).";
            return;
        }

        try {
            const res = await fetch("http://localhost:5111/invoices", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    invoiceNumber,
                    carrierName,
                    weightLbs: parseFloat(weightLbs),
                    amount: parseFloat(amount),
                }),
            });

            if (!res.ok) {
                const serverError = await res.text();
                formError =
                    serverError ||
                    `Error: Failed to save invoice (${res.status})`;
                return;
            }

            // SUCCESS: Clear form and reload table
            formError = "";
            carrierName = "";
            invoiceNumber = "";
            weightLbs = "";
            amount = "";
            await fetchInvoices();
        } catch (err) {
            formError =
                "Could not connect to C# backend. Is dotnet run running?";
        }
    }
</script>

<div class="card form-card">
    <h2>Invoice Editor</h2>
    <div class="selector">
        <button
            class:active={selectMode === "add"}
            onclick={() => (selectMode = "add")}
        >
            Add
        </button>

        <button
            class:active={selectMode === "remove"}
            onclick={() => (selectMode = "remove")}
        >
            Remove
        </button>
    </div>
    {#if selectMode === "add"}
        <h2>Add New Invoice</h2>
        <form onsubmit={handleSubmit}>
            <label for="carrier">Carrier Name</label>
            <input
                id="carrier"
                bind:value={carrierName}
                placeholder="Atlas World Group"
                required
            />
            <label for="invNum">Invoice Number</label>
            <input
                id="invNum"
                bind:value={invoiceNumber}
                placeholder="INV-5001"
                required
            />

            <label for="weight">Weight (lbs)</label>
            <input
                id="weight"
                type="number"
                step="0.01"
                bind:value={weightLbs}
                placeholder="9500"
                required
            />

            <label for="amount">Amount ($)</label>
            <input
                id="amount"
                type="number"
                step="0.01"
                bind:value={amount}
                placeholder="3200"
                required
            />

            <button type="submit" class="btn-submit">Save to PostgreSQL</button>
        </form>
    {:else}
        <div>
            <h2>Remove Invoice</h2>
            <p>Remove an invoice from the database.</p>
        </div>
    {/if}
</div>
{#if formError}
    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
    <div class="modal-backdrop" onclick={() => (formError = "")}>
        <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
        <div class="floating-alert" onclick={(e) => e.stopPropagation()}>
            <div class="alert-header">
                <span class="alert-icon">⚠️</span>
                <h3>Validation Error</h3>
            </div>
            <p class="alert-message">{formError}</p>
            <button class="btn-dismiss" onclick={() => (formError = "")}>
                Acknowledge & Fix
            </button>
        </div>
    </div>
{/if}

<style>
    .selector {
        display: flex;
    }
    .card {
        background: var(--card-bg);
        color: var(--text-main);
        padding: 1.25rem;
        border-radius: 8px;
        border: 1px solid var(--border);
        box-shadow: 0 1px 3px rgba(0, 0, 0, 0.2);
    }
    form {
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
    }

    label {
        font-size: 0.85rem;
        font-weight: 600;
        color: var(--text-muted);
    }

    input {
        padding: 0.6rem;
        background: var(--input-bg);
        color: var(--text-main);
        border: 1px solid var(--border);
        border-radius: 6px;
        font-size: 0.95rem;
    }

    input:focus {
        outline: 2px solid var(--primary);
    }
    .btn-submit {
        background: var(--primary);
        color: var(--background);
        padding: 0.75rem;
        border: none;
        border-radius: 6px;
        font-weight: bold;
        margin-top: 0.5rem;
    }

    .btn-submit:hover {
        background: var(--primary-hover);
    }

    /* Floating Modal Alert */
    .modal-backdrop {
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.6);
        backdrop-filter: blur(4px);
        display: flex;
        justify-content: center;
        align-items: flex-start;
        padding-top: 5rem;
        z-index: 9999;
    }

    .floating-alert {
        background: var(--card-bg);
        border: 2px solid var(--dup-text);
        border-radius: 12px;
        padding: 1.5rem;
        max-width: 480px;
        width: 90%;
        box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5);
        display: flex;
        flex-direction: column;
        gap: 0.75rem;
    }

    .alert-header {
        display: flex;
        align-items: center;
        gap: 0.5rem;
    }

    .alert-header h3 {
        margin: 0;
        color: var(--dup-text);
        font-size: 1.15rem;
    }

    .alert-icon {
        font-size: 1.5rem;
    }

    .alert-message {
        margin: 0;
        color: var(--text-main);
        font-size: 0.95rem;
        line-height: 1.4;
    }

    .btn-dismiss {
        align-self: flex-end;
        background: var(--dup-text);
        color: white;
        border: none;
        padding: 0.5rem 1.25rem;
        border-radius: 6px;
        font-weight: bold;
        cursor: pointer;
        margin-top: 0.5rem;
    }

    .btn-dismiss:hover {
        opacity: 0.9;
    }
</style>
