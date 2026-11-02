const status = document.getElementById('status');
let started = false;
const reportFailure = (error) => {
    if (started) return;
    const message = error instanceof Error ? error.message : String(error);
    if (status) status.textContent = 'Could not start Hibike: ' + message;
    console.error('Hibike WebAssembly startup failed:', error);
};

// Static imports fail before a module executes, leaving the page stuck on
// "Loading…" forever. Import inside try so missing framework assets are visible.
window.addEventListener('error', event => reportFailure(event.error || event.message));
window.addEventListener('unhandledrejection', event => reportFailure(event.reason));
const watchdog = setTimeout(() => {
    if (!started && status) {
        status.textContent = 'Hibike is taking too long to start. Please reload the page or check your browser WebAssembly support.';
    }
}, 20000);

try {
    const { dotnet } = await import('./_framework/dotnet.js');
    await dotnet.withDiagnosticTracing(false).run();
    started = true;
    clearTimeout(watchdog);
} catch (error) {
    clearTimeout(watchdog);
    reportFailure(error);
}
