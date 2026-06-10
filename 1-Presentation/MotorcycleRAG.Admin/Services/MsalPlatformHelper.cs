namespace MotorcycleRAG.Admin.Services;

internal static class MsalPlatformHelper
{
    internal static object GetPresenterWindow()
    {
#if MACCATALYST || IOS
        var keyWindow = UIKit.UIApplication.SharedApplication
            .ConnectedScenes
            .OfType<UIKit.UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(window => window.IsKeyWindow);

        return keyWindow?.RootViewController ?? keyWindow ?? new object();
#else
        return new object();
#endif
    }
}
