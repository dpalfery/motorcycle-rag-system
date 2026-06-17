using Microsoft.DotNet.XHarness.TestRunners.Common;
using Microsoft.DotNet.XHarness.TestRunners.Xunit;
using ObjCRuntime;
using UIKit;

namespace MotorcycleRAG.Admin.Tests;

/// <summary>
/// XHarness entry point that runs this assembly's xUnit tests on the Mac Catalyst runtime.
/// Driven by `xharness apple test` (see the `test: admin-tests` VS Code task).
/// </summary>
internal sealed class TestApplicationEntryPoint : iOSApplicationEntryPoint
{
    // xUnit tests here are not thread-safe across classes (shared HTTP stubs etc.), so run serially.
    protected override int? MaxParallelThreads => 1;

    // Device metadata is only used to decorate logs; XHarness fills in the rest from the host.
    protected override IDevice? Device => null;

    protected override IEnumerable<TestAssemblyInfo> GetTestAssemblies()
    {
        var assembly = typeof(TestApplicationEntryPoint).Assembly;
        yield return new TestAssemblyInfo(assembly, assembly.Location);
    }

    protected override void TerminateWithSuccess()
    {
        // XHarness recognizes this private selector to shut the app down cleanly once
        // the run completes. Must be performed on the main (UI) thread.
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            var selector = new Selector("terminateWithSuccess");
            UIApplication.SharedApplication.PerformSelector(selector, UIApplication.SharedApplication, 0);
        });
    }
}
