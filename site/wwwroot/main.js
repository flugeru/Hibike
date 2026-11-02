import { dotnet } from './_framework/dotnet.js';

try {
    // The .NET WebAssembly host must run the entry assembly before Avalonia
    // can attach its browser view to #out.
    await dotnet.withDiagnosticTracing(false).run();
} catch (error) {
    const status = document.getElementById('status');
    if (status) {
        status.textContent = 'Unable to start Hibike. Please reload the page.';
    }
    console.error('Hibike Avalonia WebAssembly startup failed.', error);
}
