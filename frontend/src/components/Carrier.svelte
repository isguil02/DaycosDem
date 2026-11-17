<script>
    // @ts-nocheck
    import { onMount } from "svelte";
    import { appState, fetchInvoices } from "../state.svelte.js";
    import InvoiceEditor from "./InvoiceEditor.svelte";

    let carrierList = $state([]);
    let selectedCarrier = $state("");
    let password = $state("");
    let loginError = $state("");

    onMount(async () => {
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

            appState.loggedInCarrier = await res.json();
            password = "";
        } catch (err) {
            loginError = "Could not connect to server.";
        }
    }

    function handleLogout() {
        appState.loggedInCarrier = null;
        password = "";
        loginError = "";
    }
</script>

{#if !appState.loggedInCarrier}
    <!-- Carrier Login Form (Right Column) -->
    <div class="card login-card">
        <h2>🚚 Carrier Sign In</h2>
        <p class="subtitle">Log in to view and add your invoices</p>

        {#if loginError}
            <div class="error-banner">{loginError}</div>
        {/if}

        <form onsubmit={handleLogin}>
            <div class="form-group">
                <label for="carrierSelect">Select Carrier</label>
                <select id="carrierSelect" bind:value={selectedCarrier}>
                    {#each carrierList as carrier}
                        <option value={carrier.name}>{carrier.name}</option>
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

            <button type="submit" class="btn-primary">Sign In</button>
        </form>

        <p class="hint">🔒 Default password: <code>postgres</code></p>
    </div>
{:else}
    <!-- Logged In Carrier: Header badge + Locked Invoice Editor -->
    <div class="carrier-box">
        <div class="carrier-auth-header">
            <div>
                <h3>🚚 {appState.loggedInCarrier.name}</h3>
                {#if appState.loggedInCarrier.contactPhone}
                    <span class="phone-badge"
                        >📞 {appState.loggedInCarrier.contactPhone}</span
                    >
                {/if}
            </div>
            <button class="btn-logout" onclick={handleLogout}>Log Out</button>
        </div>

        <!-- Locked Invoice Editor with Carrier name pre-filled & locked! -->
        <InvoiceEditor lockedCarrier={appState.loggedInCarrier.name} />
    </div>
{/if}

<style>
    .login-card {
        background: var(--card-bg);
        border: 1px solid var(--border);
        border-radius: 8px;
        padding: 1.5rem;
        text-align: center;
        height: 600px;
    }

    .subtitle {
        color: var(--text-muted);
        font-size: 0.85rem;
        margin-top: -0.25rem;
        margin-bottom: 1.25rem;
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
        padding: 0.5rem;
        border-radius: 6px;
        font-size: 0.85rem;
        margin-bottom: 1rem;
    }

    .carrier-box {
        display: flex;
        flex-direction: column;
        gap: 0.75rem;
    }

    .carrier-auth-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        background: var(--card-bg);
        border: 1px solid var(--border);
        padding: 0.75rem 1rem;
        border-radius: 8px;
    }

    .carrier-auth-header h3 {
        margin: 0;
        font-size: 1rem;
    }

    .phone-badge {
        font-size: 0.75rem;
        color: var(--text-muted);
    }

    .btn-logout {
        background: transparent;
        border: 1px solid var(--border);
        color: var(--text-main);
        padding: 0.3rem 0.6rem;
        border-radius: 6px;
        font-size: 0.8rem;
    }

    .btn-logout:hover {
        border-color: #ef4444;
        color: #ef4444;
    }

    .hint {
        font-size: 0.8rem;
        color: var(--text-muted);
        margin-top: 1rem;
    }

    .hint code {
        background: var(--bg);
        padding: 0.2rem 0.4rem;
        border-radius: 4px;
        color: var(--primary);
    }
</style>
