using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

return Runner.Run(args);

[ExcludeFromCodeCoverage]
internal static class Runner
{
    public static int Run(string[] args)
    {
        if (args is ["--il"])
        {
            Console.WriteLine(Convert.ToHexString(typeof(Decision).GetMethod(nameof(Decision.Route))!.GetMethodBody()!.GetILAsByteArray()!));
            return 0;
        }
        foreach (var value in args.Select(int.Parse))
            Console.WriteLine(Decision.Route(value));
        return 0;
    }
}

public static class Decision
{
    // Overlapping subsets {0, 1} and {1, 2} must yield {0, 1, 2}, never
    // a guessed sum of two aggregate "two covered" counters.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Route(int value)
    {
        return value switch { 0 => 11, 1 => 22, 2 => 33, 3 => 44, _ => 55 };
    }
}
