using System.Xml.Linq;

namespace NovaTerminal.Integration.Tests;

/// <summary>
/// Enforces the project dependency graph by inspecting the project files themselves.
/// </summary>
/// <remarks>
/// <para>
/// Checking the <c>.csproj</c> files rather than the compiled assemblies catches a violation the
/// moment someone declares a reference, even before any code uses it - the compiler drops unused
/// references from assembly metadata, so a metadata-only check would miss exactly the accidental
/// coupling this is meant to prevent.
/// </para>
/// <para>
/// An architecture that is only documented decays. One that is asserted does not.
/// </para>
/// </remarks>
public sealed class LayeringTests
{
    /// <summary>
    /// The dependency graph, written as data. A project may reference only what is listed for it.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedProjectReferences = new(StringComparer.Ordinal)
    {
        ["NovaTerminal.Core"] = [],
        ["NovaTerminal.Terminal"] = ["NovaTerminal.Core"],
        ["NovaTerminal.Process"] = ["NovaTerminal.Core"],
        ["NovaTerminal.Input"] = ["NovaTerminal.Core"],
        ["NovaTerminal.Platform"] = ["NovaTerminal.Core", "NovaTerminal.Process"],
        ["NovaTerminal.Rendering"] = ["NovaTerminal.Core", "NovaTerminal.Terminal"],
        ["NovaTerminal.App"] =
        [
            "NovaTerminal.Core",
            "NovaTerminal.Terminal",
            "NovaTerminal.Process",
            "NovaTerminal.Platform",
            "NovaTerminal.Input",
            "NovaTerminal.Rendering",
        ],
    };

    /// <summary>Only the application composes a GUI toolkit; every other layer stays toolkit-free.</summary>
    private static readonly string[] ProjectsAllowedToUseAvalonia = ["NovaTerminal.App"];

    [Fact]
    public void EveryProjectIsCovered()
    {
        var projects = EnumerateSourceProjects().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(
            AllowedProjectReferences.Keys.OrderBy(n => n, StringComparer.Ordinal),
            projects);
    }

    [Fact]
    public void NoProjectReferencesOutsideItsAllowedSet()
    {
        var violations = new List<string>();

        foreach (var (name, file) in EnumerateSourceProjects())
        {
            var allowed = AllowedProjectReferences[name];

            foreach (var reference in ReadProjectReferences(file))
            {
                if (!allowed.Contains(reference, StringComparer.Ordinal))
                {
                    violations.Add($"{name} -> {reference}");
                }
            }
        }

        Assert.True(violations.Count == 0, "Illegal project references: " + string.Join("; ", violations));
    }

    [Fact]
    public void DependencyGraphIsAcyclic()
    {
        // Layer order is the topological order; a reference to a later layer would be a cycle.
        var order = AllowedProjectReferences.Keys.ToList();

        foreach (var (name, file) in EnumerateSourceProjects())
        {
            var position = order.IndexOf(name);

            foreach (var reference in ReadProjectReferences(file))
            {
                Assert.True(
                    order.IndexOf(reference) < position,
                    $"{name} references {reference}, which is not below it in the layer order.");
            }
        }
    }

    [Fact]
    public void OnlyTheApplicationReferencesTheGuiToolkit()
    {
        var offenders = EnumerateSourceProjects()
            .Where(p => !ProjectsAllowedToUseAvalonia.Contains(p.Name, StringComparer.Ordinal))
            .Where(p => ReadPackageReferences(p.File)
                .Any(package => package.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "The terminal engine must not depend on a GUI toolkit. Offenders: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryPackageVersionIsManagedCentrally()
    {
        // Central Package Management only holds if no project pins its own version.
        var offenders = new List<string>();

        foreach (var (name, file) in EnumerateAllProjects())
        {
            var pinned = XDocument.Load(file)
                .Descendants("PackageReference")
                .Where(element => element.Attribute("Version") is not null)
                .Select(element => element.Attribute("Include")?.Value ?? "(unnamed)");

            offenders.AddRange(pinned.Select(package => $"{name}:{package}"));
        }

        Assert.True(
            offenders.Count == 0,
            "Package versions belong in Directory.Packages.props: " + string.Join(", ", offenders));
    }

    private static IEnumerable<(string Name, string File)> EnumerateSourceProjects()
        => EnumerateProjects(Path.Combine(RepositoryRoot, "src"));

    private static IEnumerable<(string Name, string File)> EnumerateAllProjects()
        => EnumerateProjects(Path.Combine(RepositoryRoot, "src"))
            .Concat(EnumerateProjects(Path.Combine(RepositoryRoot, "tests")));

    private static IEnumerable<(string Name, string File)> EnumerateProjects(string directory)
        => Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)
            .Select(file => (Path.GetFileNameWithoutExtension(file), file))
            .OrderBy(entry => entry.Item1, StringComparer.Ordinal);

    private static IEnumerable<string> ReadProjectReferences(string projectFile)
        => XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrEmpty(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!));

    private static IEnumerable<string> ReadPackageReferences(string projectFile)
        => XDocument.Load(projectFile)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty);

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NovaTerminal.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not locate NovaTerminal.sln above '{AppContext.BaseDirectory}'.");
    }
}
