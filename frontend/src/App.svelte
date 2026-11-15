<script>
  import { onMount } from "svelte";

  // ✍️ BLANK 1: Declare your reactive state variables using $state(...)
  // We need an array for invoices, and a boolean for loading!
  /** @type {any[]}*/
  let invoices = $state([]);
  let loading = $state(true);

  // Form input variables for adding a new invoice
  let invoiceNumber = $state("");
  let carrierName = $state("");
  let weightLbs = $state("");
  let amount = $state("");
  let formError = $state("");

  // ✍️ BLANK 2: Complete the API fetch function to pull from C#
  async function fetchInvoices() {
    loading = true;
    try {
      // Fetch from our C# API endpoint: 'http://localhost:5111/invoices'
      const response = await fetch("http://localhost:5111/invoices");

      // Parse the JSON data and store it into our 'invoices' state variable
      invoices = await response.json();
    } catch (err) {
      console.error("Error fetching invoices:", err);
    } finally {
      loading = false;
    }
  }

  // ✍️ BLANK 3: Use $derived(...) to count how many duplicates exist
  // Hint: filter the invoices where inv.isDuplicate is true, and get .length
  let duplicateCount = $derived(
    invoices.filter((inv) => inv.isDuplicate).length,
  );

  // Submit invoice to C# POST /invoices
  /** @param {any} e **/
  /** @param {any} e **/
  async function handleSubmit(e) {
    e.preventDefault();
    formError = ""; // Clear previous error

    // Client-side check: Disallow 3+ decimal places
    const decimalParts = amount.toString().split(".");
    if (decimalParts[1] && decimalParts[1].length > 2) {
      formError =
        "Invalid currency: Amount cannot have more than 2 decimal places (e.g. 10.50).";
      return; // STOP! Keep form inputs on screen!
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
        // Read the exact error message sent back from C#
        const serverError = await res.text();
        formError =
          serverError || `Error: Failed to save invoice (${res.status})`;
        return; // STOP! Do not clear inputs so user can fix them!
      }

      // SUCCESS: Clear form and reload table
      formError = "";
      invoiceNumber = "";
      carrierName = "";
      weightLbs = "";
      amount = "";
      await fetchInvoices();
    } catch (err) {
      formError = "Could not connect to C# backend. Is dotnet run running?";
    }
  }

  onMount(() => {
    fetchInvoices();
  });
</script>

<main class="dashboard">
  <header>
    <h1>Invoice Tracker</h1>
    <button class="refreshBtn" onclick={fetchInvoices}>🔄 Refresh</button>
  </header>

  <div class="metrics">
    <div class="card">Total Invoices: <strong>{invoices.length}</strong></div>
    <div class="card alert">
      Duplicates Flagged: <strong>{duplicateCount}</strong>
    </div>
  </div>

  <div class="grid">
    <!-- Invoices Table -->
    <div class="card table-card">
      <h2>ALL Invoices (PostgreSQL)</h2>
      <table>
        <thead>
          <tr>
            <th>Invoice #</th>
            <th>Carrier</th>
            <th>Weight</th>
            <th>Amount</th>
            <th>Audit Status</th>
          </tr>
        </thead>
        <tbody>
          {#each invoices as inv}
            <tr class={inv.isDuplicate ? "dup-row" : ""}>
              <td>{inv.invoiceNumber}</td>
              <td>{inv.carrierName}</td>
              <td>{inv.weightLbs} lbs</td>
              <td>${inv.amount}</td>
              <td>
                <!-- ✍️ BLANK 5: Show a warning if duplicate, else show Verified! -->
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

    <!-- Submit Form -->
    <div class="card form-card">
      <h2>Add New Invoice</h2>
      <form onsubmit={handleSubmit}>
        <label>Invoice Number</label>
        <input bind:value={invoiceNumber} placeholder="INV-5001" required />

        <label>Carrier Name</label>
        <input
          bind:value={carrierName}
          placeholder="Atlas World Group"
          required
        />

        <label>Weight (lbs)</label>
        <input
          type="number"
          step="0.01"
          bind:value={weightLbs}
          placeholder="9500"
          required
        />

        <label>Amount ($)</label>
        <input
          type="number"
          step="0.01"
          bind:value={amount}
          placeholder="3200"
          required
        />

        <button type="submit" class="btn-submit">Save to PostgreSQL</button>
      </form>
    </div>
  </div>
</main>

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
  .dashboard {
    max-width: 1100px;
    margin: 2rem auto;
    padding: 0 1rem;
  }

  header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 1.5rem;
  }

  h1 {
    margin: 0;
    color: var(--text-main);
  }

  .metrics {
    display: flex;
    gap: 1rem;
    margin-bottom: 1.5rem;
  }

  .card {
    background: var(--card-bg);
    color: var(--text-main);
    padding: 1.25rem;
    border-radius: 8px;
    border: 1px solid var(--border);
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.2);
  }

  .alert {
    border-left: 4px solid var(--dup-text);
  }

  .grid {
    display: grid;
    grid-template-columns: 2fr 1fr;
    gap: 1.5rem;
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

  button {
    cursor: pointer;
    &:hover {
      outline: 2px solid var(--border-accent);
    }
  }

  .btn-submit {
    background: var(--primary);
    color: white;
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
    animation: slideDown 0.2s ease-out;
  }

  @keyframes slideDown {
    from {
      transform: translateY(-20px);
      opacity: 0;
    }
    to {
      transform: translateY(0);
      opacity: 1;
    }
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
  .refreshBtn {
    background: var(--primary);
    color: var(--bg);
    padding: 0.75rem;
    border: none;
    border-radius: 6px;
    font-weight: bold;
    margin-top: 0.5rem;
    cursor: pointer;
  }
</style>
