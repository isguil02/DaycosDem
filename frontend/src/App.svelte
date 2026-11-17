<script>
  // @ts-nocheck
  import { onMount } from "svelte";
  import { appState, fetchInvoices } from "./state.svelte.js";
  import Header from "./components/Header.svelte";
  import Chat from "./components/Chat.svelte";
  import InvoiceTable from "./components/InvoiceTable.svelte";
  import InvoiceEditor from "./components/InvoiceEditor.svelte";
  import Carrier from "./components/Carrier.svelte";

  onMount(() => {
    fetchInvoices();
  });

  let currentView = $state("manager");

  // In carrier view, show only that carrier's invoices (or empty if not logged in yet)
  let carrierFilteredInvoices = $derived(
    currentView === "carrier"
      ? appState.loggedInCarrier
        ? appState.invoices.filter(
            (i) =>
              i.carrierName.toLowerCase() ===
              appState.loggedInCarrier.name.toLowerCase(),
          )
        : []
      : null,
  );
</script>

<Header bind:currentView />

<main class="container">
  <div class="grid">
    <!-- 1. Left: AI Chat -->
    <Chat />

    <!-- 2. Middle: Invoice Table (shows all for Manager, or carrier's when logged in) -->
    <InvoiceTable invoices={carrierFilteredInvoices} />

    <!-- 3. Right: Manager Editor OR Carrier Login/Locked Editor -->
    {#if currentView === "manager"}
      <InvoiceEditor />
    {:else}
      <Carrier />
    {/if}
  </div>
</main>

<style>
  .container {
    width: 100%;
    padding: 1.5rem;
  }

  .grid {
    display: grid;
    grid-template-columns: minmax(280px, 1fr) 4fr minmax(320px, 1fr);
    gap: 1.5rem;
    align-items: start;
    width: 100%;
  }
</style>
