<script>
    import { onMount } from "svelte";
    import { fetchInvoices } from "../state.svelte.js";
    import InvoiceTable from "./InvoiceTable.svelte";
    import InvoiceEditor from "./InvoiceEditor.svelte";

    onMount(() => {
        fetchInvoices();
    });
</script>

<main class="dashboard">
    <h1>Carrier Invoice Tracker</h1>

    <div class="grid">
        <InvoiceTable />
        <InvoiceEditor lockedCarrier="Allied Van Lines" />
    </div>
</main>

<style>
    .dashboard {
        max-width: 1100px;
        margin: 0 auto;
        padding: 0 1rem;
    }

    .grid {
        display: grid;
        grid-template-columns: 2fr 1fr;
        gap: 1.5rem;
    }
</style>
