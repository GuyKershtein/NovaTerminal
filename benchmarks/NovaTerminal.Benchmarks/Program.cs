using BenchmarkDotNet.Running;

namespace NovaTerminal.Benchmarks;

/// <summary>
/// Entry point for the benchmark suite.
/// </summary>
/// <remarks>
/// Run everything with <c>dotnet run -c Release --project benchmarks/NovaTerminal.Benchmarks</c>,
/// or a subset with <c>--filter *Parser*</c>. A short run (<c>--job short</c>) is enough to see the
/// shape of a change; the default job is what should be quoted anywhere.
/// </remarks>
internal static class Program
{
    private static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
