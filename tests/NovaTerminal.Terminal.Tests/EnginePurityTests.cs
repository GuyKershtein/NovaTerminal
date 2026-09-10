using System.Reflection;
using NovaTerminal.Terminal;

namespace NovaTerminal.Terminal.Tests;

/// <summary>
/// Guards the single most important architectural rule in the project: the terminal engine is a
/// pure state machine that knows nothing about how - or whether - it is displayed.
/// </summary>
/// <remarks>
/// The rule is what makes the engine testable as "bytes in, screen state out", benchmarkable
/// without a window, and reusable by a different front end. Rules that are only written down get
/// broken, so this one is asserted on every build.
/// </remarks>
public sealed class EnginePurityTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Avalonia",
        "System.Windows",
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Drawing",
        "SkiaSharp",
        "Microsoft.Maui",
    ];

    [Fact]
    public void TerminalEngine_ReferencesNoGuiAssembly()
    {
        AssertNoGuiReferences(typeof(VtConstants).Assembly);
    }

    [Fact]
    public void Core_ReferencesNoGuiAssembly()
    {
        AssertNoGuiReferences(typeof(Core.TerminalSize).Assembly);
    }

    [Fact]
    public void TerminalEngine_DoesNotReferenceTheApplication()
    {
        // Dependencies point downward only; the engine must never know its consumers.
        var referenced = typeof(VtConstants).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .ToArray();

        Assert.DoesNotContain("NovaTerminal.App", referenced);
        Assert.DoesNotContain("NovaTerminal.Rendering", referenced);
    }

    private static void AssertNoGuiReferences(Assembly assembly)
    {
        var offenders = assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .Where(name => ForbiddenAssemblyPrefixes.Any(
                prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{assembly.GetName().Name} must stay free of GUI dependencies but references: " +
            string.Join(", ", offenders));
    }
}
