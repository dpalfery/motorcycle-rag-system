using Foundation;
using UIKit;

namespace MotorcycleRAG.Admin.Tests;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds)
        {
            RootViewController = new UIViewController(),
        };
        Window.MakeKeyAndVisible();

        // Run the XHarness test runner once the app finishes launching. RunAsync awaits
        // internally so it yields the UI thread; it terminates the app when finished.
        BeginInvokeOnMainThread(async () =>
        {
            var entryPoint = new TestApplicationEntryPoint();
            await entryPoint.RunAsync();
        });

        return true;
    }
}
