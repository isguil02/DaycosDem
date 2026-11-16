<script>
    import { appState } from "../state.svelte.js";
    let {
        currentView = $bindable("manager"),
        selectedTimeZone = $bindable("UTC"),
    } = $props();
    function getTime(timeZone = "UTC") {
        return new Date().toLocaleTimeString("en-US", {
            timeZone,
            hour: "numeric",
            minute: "2-digit",
            hour12: true,
        });
    }
</script>

<header>
    <h1>Invoice Tracker</h1>
    <div class="tz-container">
        <label for="tz-select">🕒 Select Time Zone:</label>
        <select id="tz-select" bind:value={appState.timeZone}>
            <option value="UTC">UTC ({getTime("UTC")})</option>
            <option value="America/Chicago"
                >Central Time ({getTime("America/Chicago")})</option
            >
            <option value="America/New_York"
                >Eastern Time ({getTime("America/New_York")})</option
            >
            <option value="America/Denver"
                >Mountain Time ({getTime("America/Denver")})</option
            >
            <option value="America/Los_Angeles"
                >Pacific Time ({getTime("America/Los_Angeles")})</option
            >
        </select>
    </div>
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
    select {
        background-color: var(--primary);
        color: var(--background);
        border: none;
        border-radius: 5px;
        height: 3em;
        padding: 0 1em;
        font-weight: bold;
        cursor: pointer;
        &:hover {
            outline: 1px solid var(--border-accent);
        }
    }
    .tz-container {
        align-items: center;
        font-weight: bold;
    }
</style>
