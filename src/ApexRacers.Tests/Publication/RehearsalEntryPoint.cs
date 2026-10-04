using Xunit.MicrosoftTestingPlatform;
using Xunit.Runner.InProc.SystemConsole;

namespace ApexRacers.Tests.Publication;

internal static class RehearsalEntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--driver-lifecycle-host"])
        {
            await Lifecycle.LifecycleHost.RunAsync();
            return 0;
        }

        if (args is ["--publication-rehearsal-host"])
        {
            await RehearsalHost.RunAsync();
            return 0;
        }

        // Match the pinned xUnit-generated entry point, including its automated console route.
        // Keep its source generated/compiled; select this relay with the C# StartupObject option.
        return args.Any(arg => arg is "-automated" or "@@")
            ? await ConsoleRunner.Run(args)
            : await TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions);
    }
}
