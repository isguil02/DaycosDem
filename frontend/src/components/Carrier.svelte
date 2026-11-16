<script>
    // @ts-nocheck
    import { onMount } from "svelte";
    import { appState, fetchInvoices } from "../state.svelte.js";
    import InvoiceTable from "./InvoiceTable.svelte";
    import InvoiceEditor from "./InvoiceEditor.svelte";

    let carrierList = $state([]);
    let selectedCarrier = $state("");
    let password = $state("");
    let loggedInCarrier = $state(null);
    let loginError = $state("");

    // Automatically filter invoices to only show this carrier's data
    let filteredInvoices = $derived(
        loggedInCarrier
            ? appState.invoices.filter(
                  (i) =>
                      i.carrierName.toLowerCase() ===
                      loggedInCarrier.name.toLowerCase(),
              )
            : [],
    );

    onMount(async () => {
        fetchInvoices();
        // Fetch safe list of registered carriers for the dropdown
        try {
            const res = await fetch("http://localhost:5111/carriers");
            carrierList = await res.json();
            if (carrierList.length > 0) {
                selectedCarrier = carrierList[0].name;
            }
        } catch (err) {
            console.error("Failed to load carriers:", err);
        }
    });
    /** @param {any} e **/
    async function handleLogin(e) {
        e.preventDefault();
        loginError = "";

        try {
            const res = await fetch("http://localhost:5111/carriers/login", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    carrierName: selectedCarrier,
                    password,
                }),
            });

            if (!res.ok) {
                const errData = await res.json();
                loginError = errData.message || "Invalid credentials.";
                return;
            }

            loggedInCarrier = await res.json();
            password = "";
        } catch (err) {
            loginError = "Could not connect to server.";
        }
    }

    function handleLogout() {
        loggedInCarrier = null;
        password = "";
        loginError = "";
    }
</script>

<main class="dashboard">
    {#if !loggedInCarrier}
        <!-- Carrier Login Screen -->
        <div class="login-wrapper">
            <div class="card login-card">
                <h2>🚚 Carrier Portal Login</h2>
                <p class="subtitle">
                    Access your Daycos audited invoice records
                </p>

                {#if loginError}
                    <div class="error-banner">{loginError}</div>
                {/if}

                <form onsubmit={handleLogin}>
                    <div class="form-group">
                        <label for="carrierSelect">Select Carrier</label>
                        <select id="carrierSelect" bind:value={selectedCarrier}>
                            {#each carrierList as carrier}
                                <option value={carrier.name}
                                    >{carrier.name}</option
                                >
                            {/each}
                        </select>
                    </div>

                    <div class="form-group">
                        <label for="password">Password</label>
                        <input
                            id="password"
                            type="password"
                            bind:value={password}
                            placeholder="Default: postgres"
                            required
                        />
                    </div>

                    <button type="submit" class="btn-primary"
                        >Sign In to Portal</button
                    >
                </form>

                <p class="hint">
                    🔒 Demo default password: <code>postgres</code>
                </p>
            </div>
        </div>
    {:else}
        <!-- Authenticated Carrier Dashboard -->
        <div class="carrier-header">
            <div>
                <h1>🚚 {loggedInCarrier.name} Portal</h1>
                {#if loggedInCarrier.contactPhone}
                    <span class="phone-badge"
                        >📞 Registered Phone: {loggedInCarrier.contactPhone}</span
                    >
                {/if}
            </div>
            <button class="btn-logout" onclick={handleLogout}>Log Out</button>
        </div>

        <div class="grid">
            <InvoiceTable invoices={filteredInvoices} />
            <InvoiceEditor lockedCarrier={loggedInCarrier.name} />
        </div>
    {/if}
</main>

<style>
    .dashboard {
        max-width: 1100px;
        margin: 0 auto;
        padding: 0 1rem;
    }

    .login-wrapper {
        display: flex;
        justify-content: center;
        align-items: center;
        padding-top: 3rem;
    }

    .login-card {
        max-width: 420px;
        width: 100%;
        text-align: center;
    }

    .subtitle {
        color: var(--text-muted);
        font-size: 0.9rem;
        margin-top: -0.25rem;
        margin-bottom: 1.5rem;
    }

    .form-group {
        text-align: left;
        margin-bottom: 1rem;
    }

    .form-group label {
        display: block;
        font-size: 0.85rem;
        margin-bottom: 0.35rem;
        color: var(--text-muted);
    }

    .error-banner {
        background: rgba(239, 68, 68, 0.15);
        color: #ef4444;
        border: 1px solid rgba(239, 68, 68, 0.3);
        padding: 0.6rem 0.8rem;
        border-radius: 6px;
        font-size: 0.85rem;
        margin-bottom: 1rem;
    }

    .carrier-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 1.5rem;
    }

    .phone-badge {
        font-size: 0.85rem;
        color: var(--text-muted);
    }

    .btn-logout {
        background: transparent;
        border: 1px solid var(--border);
        color: var(--text);
        padding: 0.4rem 0.8rem;
        border-radius: 6px;
        cursor: pointer;
        font-size: 0.85rem;
    }

    .btn-logout:hover {
        background: var(--card-bg);
        border-color: #ef4444;
        color: #ef4444;
    }

    .hint {
        font-size: 0.8rem;
        color: var(--text-muted);
        margin-top: 1rem;
    }

    .hint code {
        background: var(--card-bg);
        padding: 0.2rem 0.4rem;
        border-radius: 4px;
        color: var(--primary);
    }

    .grid {
        display: grid;
        grid-template-columns: 2fr 1fr;
        gap: 1.5rem;
    }
</style>
