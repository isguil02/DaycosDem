<script>
  import Header from "./components/Header.svelte";
  import Manager from "./components/Manager.svelte";
  import Carrier from "./components/Carrier.svelte";

  let currentView = $state("manager");
</script>

<Header bind:currentView />

{#if currentView === "manager"}
  <Manager />
{:else}
  <Carrier />
{/if}
