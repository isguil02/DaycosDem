<script>
    let { currentView = $bindable("manager") } = $props();
</script>

<header>
    <h1>Invoice Tracker</h1>
    <div>
        <button
            class:active={currentView === "manager"}
            onclick={() => (currentView = "manager")}
        >
            🏢 Manager View
        </button>
        <button
            class:active={currentView === "carrier"}
            onclick={() => (currentView = "carrier")}
        >
            🚚 Carrier View
        </button>
    </div>
</header>

<style>
    header {
        border-top: 1px solid var(--border);
        border-bottom: 1px solid var(--border);
        position: sticky;
        top: 0;
        background-color: color-mix(in srgb, var(--bg) 95%, transparent);
        display: flex;
        justify-content: space-between;
        padding-left: 10%;
        padding-right: 10%;
        align-items: center;
        transition: background-color 0.5s;
        &:hover {
            background-color: color-mix(
                in srgb,
                var(--border-accent) 10%,
                var(--bg)
            );
        }
        margin-bottom: 3em;
    }
    button {
        background-color: var(--primary);
        border: none;
        border-radius: 5px;
        height: 3em;
        width: 10em;
        color: var(--background);

        &:hover {
            outline: 1px solid var(--border-accent);
            cursor: pointer;
        }
    }
    div {
        display: flex;
        gap: 2em;
    }

    button.active {
        outline: 2px solid var(--border-accent);
        font-weight: bold;
    }
</style>
