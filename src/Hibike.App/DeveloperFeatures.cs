namespace Hibike.App;

internal static class DeveloperFeatures
{
    public static bool Enabled =>
#if HIBIKE_DEVELOPER_TOOLS
        true;
#else
        false;
#endif
}
