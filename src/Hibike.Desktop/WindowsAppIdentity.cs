using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Hibike.Desktop;

internal static class WindowsAppIdentity
{
    public const string Id = "Flugel.Hibike";

    public static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + Id);
        key.SetValue("DisplayName", "Hibike");
        if (Environment.ProcessPath is { } executable) key.SetValue("IconUri", executable + ",0");
        key.SetValue("ShowInSettings", 0, RegistryValueKind.DWord);
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(Id));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
