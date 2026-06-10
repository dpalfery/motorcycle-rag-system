namespace MotorcycleRAG.Admin.Services;

internal static class MsalRedirectUriHelper
{
    internal static string GetRedirectUri()
    {
#if MACCATALYST || IOS
        return $"msauth.{AppInfo.PackageName}://auth";
#else
        return "http://localhost";
#endif
    }
}
