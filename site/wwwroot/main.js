import { dotnet } from './_framework/dotnet.js';

try {
    const runtime = await dotnet.withDiagnosticTracing(false).create();
    await runtime.runMain(runtime.getConfig().mainAssemblyName, []);
} catch (error) {
    const status = document.getElementById('status');
    if (status) status.textContent = 'Unable to load the interactive site. Use the links below for the HTML pages.';
    console.error('Hibike WebAssembly startup failed.', error);
}
