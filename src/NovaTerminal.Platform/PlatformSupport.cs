namespace NovaTerminal.Platform;

/// <summary>
/// Answers what pseudo-terminal facilities the current machine actually provides.
/// </summary>
/// <remarks>
/// Being on Windows is not sufficient to use ConPTY. The API
/// (<c>CreatePseudoConsole</c>) was introduced in Windows 10 version 1809, build 17763; on anything
/// older the entry point simply does not exist in <c>kernel32</c>. Detecting that up front produces
/// an actionable message instead of an <see cref="EntryPointNotFoundException"/> from deep inside
/// interop code.
/// </remarks>
public static class PlatformSupport
{
    /// <summary>
    /// The Windows 10 build (version 1809) that introduced the pseudo console API.
    /// </summary>
    public const int MinimumConPtyBuild = 17763;

    /// <summary>Whether the ConPTY backend can be used on this machine.</summary>
    public static bool IsConPtyAvailable =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumConPtyBuild);

    /// <summary>
    /// Whether the Unix pseudo-terminal backend applies to this machine. The backend itself arrives
    /// in a later milestone; the capability check is here so the composition root has a single
    /// place to ask.
    /// </summary>
    public static bool IsUnixPtyAvailable => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    /// <summary>Whether any supported pseudo-terminal mechanism is available.</summary>
    public static bool IsAnyPtyAvailable => IsConPtyAvailable || IsUnixPtyAvailable;

    /// <summary>
    /// Produces a diagnostic description of the current platform for logs and error messages.
    /// </summary>
    public static string Describe()
    {
        var mechanism = IsConPtyAvailable
            ? "ConPTY"
            : IsUnixPtyAvailable ? "Unix PTY" : "none";

        return $"{Environment.OSVersion.VersionString} ({RuntimeArchitecture}); pseudo-terminal: {mechanism}";
    }

    /// <summary>
    /// Throws when no pseudo-terminal mechanism is available, explaining why.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">No supported mechanism exists here.</exception>
    public static void ThrowIfNoPtyAvailable()
    {
        if (IsAnyPtyAvailable)
        {
            return;
        }

        var reason = OperatingSystem.IsWindows()
            ? $"Windows 10 build {MinimumConPtyBuild} (version 1809) or later is required for ConPTY; " +
              $"this machine reports {Environment.OSVersion.Version}."
            : $"No pseudo-terminal backend is implemented for {Environment.OSVersion.Platform}.";

        throw new PlatformNotSupportedException($"NovaTerminal cannot start a shell here. {reason}");
    }

    private static string RuntimeArchitecture =>
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
}
