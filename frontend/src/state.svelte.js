export const appState = $state({
    timeZone: 'UTC',
    view: 'manager',
    invoices: [],
    loading: false,
    selectedInvoiceID: null,
    filter: '',
    loggedInCarrier: null
});


// Global fetch function: Any component can call this to refresh the data!
export async function fetchInvoices() {
    appState.loading = true;
    try {
        const response = await fetch("http://localhost:5111/invoices");
        appState.invoices = await response.json();

        // 🛡️ Auto-Snapshot: Create timestamped backup folder whenever active invoices exist
        if (appState.invoices.length > 0) {
            fetch("http://localhost:5111/system/backup", { method: "POST" }).catch(() => { });
        }
    } catch (err) {
        console.error("Error fetching invoices:", err);
    } finally {
        appState.loading = false;
    }
}