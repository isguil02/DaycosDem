<script>
    // @ts-nocheck
    import { appState, fetchInvoices } from "../state.svelte.js";

    let inputMessage = $state("");
    let loading = $state(false);
    let messageCounter = 2;
    function getNextId() {
        return messageCounter++;
    }

    let activeModel = $derived(
        appState.view === "carrier" && appState.loggedInCarrier
            ? "daycos-carrier"
            : "daycos-auditor",
    );

    let messages = $state([
        {
            id: 1,
            sender: "bot",
            time: "10:55 AM",
            text: "👋 Welcome to the Daycos AI Audit Copilot. I'm connected to your local Daycos models. How can I assist with your freight audit today?",
        },
    ]);

    let chatContainer;

    function scrollToBottom() {
        if (chatContainer) {
            setTimeout(() => {
                chatContainer.scrollTop = chatContainer.scrollHeight;
            }, 50);
        }
    }

    function handleFilterClick(filterVal, label) {
        appState.filter = filterVal;
        const now = new Date().toLocaleTimeString([], {
            hour: "2-digit",
            minute: "2-digit",
        });
        messages.push({
            id: getNextId(),
            sender: "user",
            time: now,
            text: `Filter: ${label}`,
        });
        messages.push({
            id: getNextId(),
            sender: "bot",
            time: now,
            text: filterVal
                ? `✅ Applied filter for "${label}". Active in table.`
                : "🧹 Filters cleared. Showing all audited invoices.",
        });
        scrollToBottom();
    }

    // Executes an approved action (POST / PUT / DELETE)
    async function handleApproveAction(msg) {
        if (!msg.action || msg.action.status !== "pending") return;
        msg.action.status = "executing";

        const now = new Date().toLocaleTimeString([], {
            hour: "2-digit",
            minute: "2-digit",
        });

        try {
            const res = await fetch(msg.action.url, {
                method: msg.action.method,
                headers: { "Content-Type": "application/json" },
                body: msg.action.body ? JSON.stringify(msg.action.body) : null,
            });

            if (!res.ok) {
                const errText = await res.text();
                throw new Error(errText || "Action failed.");
            }

            msg.action.status = "approved";
            await fetchInvoices(); // Refresh the live table!

            messages.push({
                id: getNextId(),
                sender: "bot",
                time: now,
                text: `✅ Action executed successfully! ${msg.action.successMessage}`,
            });
        } catch (err) {
            msg.action.status = "failed";
            messages.push({
                id: getNextId(),
                sender: "bot",
                time: now,
                text: `⚠️ Execution failed: ${err.message}`,
            });
        } finally {
            scrollToBottom();
        }
    }

    function handleDenyAction(msg) {
        if (!msg.action) return;
        msg.action.status = "rejected";
        const now = new Date().toLocaleTimeString([], {
            hour: "2-digit",
            minute: "2-digit",
        });
        messages.push({
            id: getNextId(),
            sender: "bot",
            time: now,
            text: "❌ Action cancelled by auditor. No database changes were made.",
        });
        scrollToBottom();
    }

    async function handleSend(e) {
        e?.preventDefault();
        const prompt = inputMessage.trim();
        if (!prompt || loading) return;

        const now = new Date().toLocaleTimeString([], {
            hour: "2-digit",
            minute: "2-digit",
        });
        messages.push({
            id: getNextId(),
            sender: "user",
            time: now,
            text: prompt,
        });
        inputMessage = "";
        scrollToBottom();

        const lower = prompt.toLowerCase();
        const isCarrier =
            appState.view === "carrier" && !!appState.loggedInCarrier;
        const activeCarrierName = appState.loggedInCarrier?.name || "";

        // ─────────────────────────────────────────────────────────────
        // 1. SECURITY & PERMISSION GUARDRAILS
        // ─────────────────────────────────────────────────────────────

        // Rule A: Never reveal passwords / credentials
        if (
            lower.includes("password") ||
            lower.includes("secret") ||
            lower.includes("hash") ||
            lower.includes("credential")
        ) {
            messages.push({
                id: getNextId(),
                sender: "bot",
                time: now,
                text: "🔒 Security Policy: Carrier authentication credentials (password hashes) are strictly confidential and cannot be revealed or exported.",
            });
            scrollToBottom();
            return;
        }

        // Rule B: Carrier View Multi-Tenant Isolation
        if (isCarrier) {
            if (
                lower.includes("seed") ||
                lower.includes("generate") ||
                lower.includes("dummy")
            ) {
                messages.push({
                    id: getNextId(),
                    sender: "bot",
                    time: now,
                    text: `🛑 Access Denied: Bulk test data generation is restricted to internal Managers. Carrier accounts cannot create batch data.`,
                });
                scrollToBottom();
                return;
            }

            const competitorKeywords = [
                "allied",
                "atlas",
                "old dominion",
                "north american",
                "bob burgs",
            ].filter((c) => !activeCarrierName.toLowerCase().includes(c));
            if (competitorKeywords.some((c) => lower.includes(c))) {
                messages.push({
                    id: getNextId(),
                    sender: "bot",
                    time: now,
                    text: `🛑 Access Denied: You are authenticated as ${activeCarrierName}. You cannot query or view records for external carriers.`,
                });
                scrollToBottom();
                return;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 2. ROCK-SOLID NUMERIC & STATE FILTERS (0ms)
        // ─────────────────────────────────────────────────────────────

        // Reset / Clear
        if (
            lower.includes("clear") ||
            lower.includes("reset") ||
            lower.includes("show all")
        ) {
            appState.filter = "";
            messages.push({
                id: getNextId(),
                sender: "bot",
                time: now,
                text: "🧹 Filter cleared. Showing all invoices.",
            });
            scrollToBottom();
            return;
        }

        // Duplicates
        if (lower.includes("duplicate")) {
            appState.filter = "duplicates";
            messages.push({
                id: getNextId(),
                sender: "bot",
                time: now,
                text: "🔍 Filtered table to show flagged duplicate invoices.",
            });
            scrollToBottom();
            return;
        }

        // Context Memory: "i meant 10000", "actually 3000"
        const correctionMatch = lower.match(
            /(?:i meant|actually|make it|make that|change to)\s*\$?(\d+(?:\.\d+)?\s*k?)/,
        );
        if (correctionMatch && appState.filter) {
            let num = parseFloat(correctionMatch[1]);
            if (correctionMatch[1].includes("k")) num *= 1000;
            const op = appState.filter.includes("<") ? "<" : ">";

            if (appState.filter.startsWith("weight")) {
                appState.filter = `weight${op}${num}`;
                messages.push({
                    id: getNextId(),
                    sender: "bot",
                    time: now,
                    text: `⚖️ Updated filter: shipments ${op === "<" ? "under" : "over"} ${num.toLocaleString()} lbs.`,
                });
                scrollToBottom();
                return;
            }
            if (
                appState.filter.startsWith("amount") ||
                appState.filter.startsWith(">") ||
                appState.filter.startsWith("<")
            ) {
                appState.filter = `amount${op}${num}`;
                messages.push({
                    id: getNextId(),
                    sender: "bot",
                    time: now,
                    text: `💰 Updated filter: invoices ${op === "<" ? "under" : "over"} $${num.toLocaleString()}.`,
                });
                scrollToBottom();
                return;
            }
        }

        // Greater Than (>) or Less Than (<) for Weight & Amount
        const isUnder =
            lower.includes("under") ||
            lower.includes("less") ||
            lower.includes("below") ||
            lower.includes("<");
        const isOver =
            lower.includes("over") ||
            lower.includes("more") ||
            lower.includes("greater") ||
            lower.includes("above") ||
            lower.includes(">");
        const numMatch = lower.match(/\$?(\d+(?:\.\d+)?\s*k?)\b/);

        if (numMatch && (isOver || isUnder)) {
            let qty = parseFloat(numMatch[1]);
            if (numMatch[1].includes("k")) qty *= 1000;
            const op = isUnder ? "<" : ">";
            const word = isUnder ? "under" : "over";

            if (
                lower.includes("lb") ||
                lower.includes("weight") ||
                lower.includes("pound")
            ) {
                appState.filter = `weight${op}${qty}`;
                messages.push({
                    id: getNextId(),
                    sender: "bot",
                    time: now,
                    text: `⚖️ Filtered table: showing shipments ${word} ${qty.toLocaleString()} lbs.`,
                });
                scrollToBottom();
                return;
            }

            appState.filter = `amount${op}${qty}`;
            messages.push({
                id: getNextId(),
                sender: "bot",
                time: now,
                text: `💰 Filtered table: showing invoices ${word} $${qty.toLocaleString()}.`,
            });
            scrollToBottom();
            return;
        }

        // ─────────────────────────────────────────────────────────────
        // 3. MUTATING ACTIONS (Generate / Seed)
        // ─────────────────────────────────────────────────────────────
        if (
            lower.includes("generate") ||
            lower.includes("seed") ||
            lower.includes("dummy")
        ) {
            const countMatch = lower.match(/(\d+)\s*k?\b/);
            let count = 50;
            if (countMatch) {
                count = parseInt(countMatch[1]);
                if (lower.includes("1k") || lower.includes("1,000"))
                    count = 1000;
                else if (countMatch[0].includes("k")) count *= 1000;
            }
            count = Math.min(Math.max(count, 1), 1000);

            messages.push({
                id: getNextId(),
                sender: "bot",
                time: now,
                text: `⚠️ Action Confirmation Required: Generate **${count} synthetic invoices** across carriers for audit benchmarking?`,
                action: {
                    type: "seed",
                    title: `Seed ${count} Invoices`,
                    impact: `Inserts ${count} invoices into PostgreSQL in a single atomic transaction.`,
                    status: "pending",
                    method: "POST",
                    url: "http://localhost:5111/invoices/seed",
                    body: { count },
                    successMessage: `Generated ${count} invoices in PostgreSQL. Table refreshed.`,
                },
            });
            scrollToBottom();
            return;
        }

        // ─────────────────────────────────────────────────────────────
        // 4. GENERAL AUDIT COPILOT REASONING (Ollama LLM)
        // ─────────────────────────────────────────────────────────────
        loading = true;
        try {
            const roleText = isCarrier
                ? `Authenticated Carrier: ${activeCarrierName}`
                : "Internal Senior Auditor (Manager)";

            const res = await fetch("http://localhost:11434/api/generate", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    model: activeModel,
                    prompt: `Context: ${roleText}, ${appState.invoices.length} active shipping records.\nQuestion: ${prompt}`,
                    stream: false,
                }),
            });

            if (!res.ok) throw new Error("Ollama call failed");
            const data = await res.json();

            // 🎯 Detect proposed CLI command and build the [Execute Yes / No] card!
            const cmdMatch = data.response.match(
                /(?:`|\b)(\.?\\?cli\\(?:manager|carrier)\.ps1[^\n`]+)(?:`|\b)/i,
            );
            let action = null;

            if (cmdMatch) {
                const cmd = cmdMatch[1].trim();
                let url = "http://localhost:5111/system/backup";
                let impact = "Execute administrative command";
                let successMessage = "Command executed successfully.";
                let body = null;

                if (cmd.includes("nuke")) {
                    url = "http://localhost:5111/system/nuke";
                    impact =
                        "Simulates accidental wipe (DELETE FROM invoices).";
                    successMessage = "Ledger wiped to $0 spend.";
                } else if (cmd.includes("restore")) {
                    url = "http://localhost:5111/system/restore-backup";
                    impact =
                        "Restores ledger from newest backup folder in PostgreSQL.";
                    successMessage = "Invoices restored from snapshot.";
                } else if (cmd.includes("seed")) {
                    url = "http://localhost:5111/invoices/seed";
                    impact = "Seeds synthetic invoices into PostgreSQL.";
                    body = { count: 50 };
                    successMessage = "Batch invoices generated.";
                } else if (cmd.includes("backup")) {
                    url = "http://localhost:5111/system/backup";
                    impact = "Creates snapshot of all 4 tables in backups/.";
                    successMessage = "Backup snapshot created.";
                }

                action = {
                    type: "cli",
                    title: `Run Command: ${cmd}`,
                    impact: impact,
                    status: "pending",
                    method: "POST",
                    url: url,
                    body: body,
                    successMessage: successMessage,
                };
            }

            messages.push({
                id: getNextId(),
                sender: "bot",
                time: now,
                text: data.response,
                action: action,
            });
        } catch (err) {
            messages.push({
                id: getNextId(),
                sender: "bot",
                time: now,
                text: "⚠️ Local Ollama is offline. Ensure `ollama run llama3.2:1b` is running.",
            });
        } finally {
            loading = false;
            scrollToBottom();
        }
    }
</script>

<div class="card chat-card">
    <!-- Header with Model Status -->
    <div class="chat-header">
        <div class="header-left">
            <span class="ai-icon">🤖</span>
            <div>
                <h3>AI Audit Copilot</h3>
                <span class="subtext">Norfolk Audit Engine</span>
            </div>
        </div>
        <span class="model-badge">🟢 {activeModel}</span>
    </div>

    <!-- Quick Filter Pills -->
    <div class="pills-row">
        <button
            type="button"
            class="pill"
            onclick={() => handleFilterClick("duplicates", "Duplicates")}
        >
            🔍 Duplicates
        </button>
        <button
            type="button"
            class="pill"
            onclick={() => handleFilterClick("Allied", "Allied Van Lines")}
        >
            🚚 Allied
        </button>
        <button
            type="button"
            class="pill"
            onclick={() => handleFilterClick("weight>9000", "> 9,000 lbs")}
        >
            ⚖️ > 9,000 lbs
        </button>
        <button
            type="button"
            class="pill reset"
            onclick={() => handleFilterClick("", "Reset")}
        >
            🧹 Reset
        </button>
    </div>

    <!-- Messages Stream -->
    <div class="messages" bind:this={chatContainer}>
        {#each messages as msg, i (i)}
            <div class="msg-row {msg.sender}">
                <div class="msg-bubble {msg.sender}">
                    <p>{msg.text}</p>

                    <!-- Interactive Action Approval Card -->
                    {#if msg.action}
                        <div class="action-card">
                            <div class="action-title">{msg.action.title}</div>
                            <div class="action-impact">{msg.action.impact}</div>

                            {#if msg.action.status === "pending"}
                                <div class="action-buttons">
                                    <button
                                        class="btn-approve"
                                        onclick={() => handleApproveAction(msg)}
                                    >
                                        ✅ Approve & Execute
                                    </button>
                                    <button
                                        class="btn-deny"
                                        onclick={() => handleDenyAction(msg)}
                                    >
                                        ❌ Cancel
                                    </button>
                                </div>
                            {:else if msg.action.status === "executing"}
                                <div class="action-status executing">
                                    ⏳ Executing transaction in PostgreSQL...
                                </div>
                            {:else if msg.action.status === "approved"}
                                <div class="action-status approved">
                                    ✅ Approved & Executed
                                </div>
                            {:else if msg.action.status === "rejected"}
                                <div class="action-status rejected">
                                    ❌ Denied by Auditor
                                </div>
                            {/if}
                        </div>
                    {/if}

                    <span class="msg-time">{msg.time}</span>
                </div>
            </div>
        {/each}

        {#if loading}
            <div class="msg-row bot">
                <div class="msg-bubble bot thinking">
                    <span class="spinner"></span> Thinking with {activeModel}...
                </div>
            </div>
        {/if}
    </div>

    <!-- Multi-Line Textarea Input Bar -->
    <form class="chat-input-bar" onsubmit={handleSend}>
        <textarea
            bind:value={inputMessage}
            placeholder="Ask Copilot (e.g. 'generate 500 dummy invoices', 'more than 9000lbs')..."
            disabled={loading}
            rows="2"
            onkeydown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                    e.preventDefault();
                    handleSend(e);
                }
            }}></textarea>
        <button
            type="submit"
            class="btn-send"
            disabled={loading || !inputMessage.trim()}
        >
            ➤
        </button>
    </form>
</div>

<style>
    .chat-card {
        background: var(--card-bg);
        border: 1px solid var(--border);
        border-radius: 8px;
        padding: 1rem;
        display: flex;
        flex-direction: column;
        height: 600px;
        box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
    }

    .chat-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        border-bottom: 1px solid var(--border);
        padding-bottom: 0.65rem;
        margin-bottom: 0.65rem;
    }

    .header-left {
        display: flex;
        align-items: center;
        gap: 0.5rem;
    }

    .ai-icon {
        font-size: 1.4rem;
    }

    .chat-header h3 {
        margin: 0;
        font-size: 0.95rem;
        font-weight: 700;
        color: var(--text-main);
    }

    .subtext {
        font-size: 0.7rem;
        color: var(--text-muted);
    }

    .model-badge {
        font-size: 0.68rem;
        background: rgba(34, 197, 94, 0.15);
        color: #22c55e;
        padding: 0.2rem 0.5rem;
        border-radius: 999px;
        font-weight: 600;
        border: 1px solid rgba(34, 197, 94, 0.3);
    }

    .pills-row {
        display: flex;
        flex-wrap: wrap;
        gap: 0.35rem;
        margin-bottom: 0.65rem;
    }

    .pill {
        background: var(--bg);
        border: 1px solid var(--border);
        color: var(--text-main);
        font-size: 0.72rem;
        padding: 0.25rem 0.55rem;
        border-radius: 12px;
        cursor: pointer;
        transition: all 0.15s ease;
    }

    .pill:hover {
        border-color: var(--primary);
        color: var(--primary);
    }

    .pill.reset {
        color: var(--text-muted);
    }

    .messages {
        flex: 1;
        overflow-y: auto;
        display: flex;
        flex-direction: column;
        gap: 0.75rem;
        padding-right: 0.35rem;
        margin-bottom: 0.65rem;
    }

    .msg-row {
        display: flex;
        width: 100%;
    }

    .msg-row.user {
        justify-content: flex-end;
    }

    .msg-row.bot {
        justify-content: flex-start;
    }

    .msg-bubble {
        max-width: 90%;
        padding: 0.65rem 0.85rem;
        border-radius: 8px;
        font-size: 0.82rem;
        line-height: 1.45;
    }

    .msg-bubble p {
        margin: 0;
        white-space: pre-wrap;
    }

    .msg-bubble.user {
        background: var(--primary);
        color: #ffffff;
        border-bottom-right-radius: 2px;
    }

    .msg-bubble.bot {
        background: var(--bg);
        border: 1px solid var(--border);
        color: var(--text-main);
        border-bottom-left-radius: 2px;
    }

    .msg-time {
        display: block;
        font-size: 0.65rem;
        color: var(--text-muted);
        margin-top: 0.35rem;
        text-align: right;
    }

    .msg-bubble.user .msg-time {
        color: rgba(255, 255, 255, 0.7);
    }

    .msg-bubble.thinking {
        font-style: italic;
        color: var(--text-muted);
    }

    /* Interactive Action Approval Card */
    .action-card {
        background: var(--card-bg);
        border: 1px solid var(--border);
        border-radius: 6px;
        padding: 0.65rem;
        margin-top: 0.5rem;
        box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
    }

    .action-title {
        font-weight: 700;
        font-size: 0.85rem;
        color: var(--primary);
        margin-bottom: 0.25rem;
    }

    .action-impact {
        font-size: 0.75rem;
        color: var(--text-muted);
        margin-bottom: 0.5rem;
    }

    .action-buttons {
        display: flex;
        gap: 0.4rem;
    }

    .btn-approve {
        background: #16a34a;
        color: white;
        border: none;
        padding: 0.35rem 0.75rem;
        border-radius: 4px;
        font-size: 0.75rem;
        font-weight: 600;
        cursor: pointer;
    }

    .btn-approve:hover {
        background: #15803d;
    }

    .btn-deny {
        background: transparent;
        border: 1px solid var(--border);
        color: var(--text-muted);
        padding: 0.35rem 0.65rem;
        border-radius: 4px;
        font-size: 0.75rem;
        cursor: pointer;
    }

    .btn-deny:hover {
        color: #ef4444;
        border-color: #ef4444;
    }

    .action-status {
        font-size: 0.75rem;
        font-weight: 600;
        padding: 0.25rem 0.5rem;
        border-radius: 4px;
    }

    .action-status.approved {
        background: rgba(34, 197, 94, 0.15);
        color: #22c55e;
    }

    .action-status.rejected {
        background: rgba(239, 68, 68, 0.15);
        color: #ef4444;
    }

    .action-status.executing {
        color: var(--primary);
        font-style: italic;
    }

    /* Multi-Line Textarea */
    .chat-input-bar {
        display: flex;
        gap: 0.5rem;
        align-items: flex-end;
        padding-top: 0.4rem;
        border-top: 1px solid var(--border);
    }

    .chat-input-bar textarea {
        flex: 1;
        background: var(--bg);
        border: 1px solid var(--border);
        color: var(--text-main);
        padding: 0.5rem 0.75rem;
        border-radius: 6px;
        font-size: 0.82rem;
        resize: none;
        font-family: inherit;
        line-height: 1.3;
    }

    .chat-input-bar textarea:focus {
        outline: none;
        border-color: var(--primary);
    }

    .btn-send {
        background: var(--primary);
        color: white;
        border: none;
        padding: 0.6rem 0.85rem;
        border-radius: 6px;
        font-size: 0.85rem;
        cursor: pointer;
        transition: background 0.15s;
        height: 42px;
    }

    .btn-send:hover:not(:disabled) {
        background: var(--primary-hover);
    }

    .btn-send:disabled {
        opacity: 0.4;
        cursor: not-allowed;
    }
</style>
