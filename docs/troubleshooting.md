# Troubleshooting

## A process that owns a console cannot host a pseudo console

This is the most surprising thing discovered while building NovaTerminal, and it is worth knowing
before you try to reuse the platform layer somewhere else.

**The rule.** If the process calling `CreateProcess` already owns a console, the child attaches to
*that* console rather than to the pseudo console, even though the attribute list asking for the
pseudo console was accepted. The pseudo console is created, a headless `conhost` is started for it,
and it then receives nothing at all. Calling `FreeConsole` first does **not** fix it.

**How it was established.** The same Win32 sequence was run from two executables that differed only
in their subsystem:

| Host | `CreatePseudoConsole` | Child bound to it |
|------|----------------------|-------------------|
| Console executable (`<OutputType>Exe</OutputType>`) | succeeds | ✗ output goes to the inherited console |
| GUI executable (`<OutputType>WinExe</OutputType>`) | succeeds | ✓ output arrives on the pipe |

NovaTerminal itself is a GUI executable, so it is unaffected. The consequence is for *testing*:
xUnit v3 requires a console test host, so the ConPTY path cannot be exercised from inside the test
process. `tests/NovaTerminal.PtyHarness` exists for exactly this reason — a GUI-subsystem helper
that runs the real scenario against the real backend and reports through a file, which
`ShellSessionTests` then asserts on.

**If you are writing a console application** that needs a pseudo console, this is the constraint to
design around: the work has to happen in a process that never had a console.

## The terminal opens but stays blank

NovaTerminal notices this itself. If a shell starts and produces nothing within five seconds, the
terminal writes a diagnosis onto its own screen rather than leaving you with an empty window
(`StartupWatchdog`). The process really is running in that state, so nothing errors and there is
otherwise no clue as to what went wrong.

Causes worth checking, in order:

1. **The host process owns a console.** See above. This is by far the most likely cause if you are
   embedding `NovaTerminal.Platform` in your own program.
2. **Something is intercepting process creation.** Endpoint security software sometimes re-launches
   a child on your behalf, which loses the attribute list that performs the binding.

To confirm the pseudo console itself is healthy, look for its headless host while a session is
running:

```powershell
Get-CimInstance Win32_Process -Filter "Name='conhost.exe'" |
    Where-Object CommandLine -match headless |
    Select-Object ProcessId, CommandLine
```

A line such as `conhost.exe --headless --width 115 --height 29 --signal 0x2f0 --server 0x2ec` means
the pseudo console was created correctly, at the size NovaTerminal asked for. If that line is
present and the window is still blank, the problem is the binding of the child, not the console.

## Tests report as passing when they are not

Fixed, but worth knowing about if you see it elsewhere. Running xUnit v3 through the VSTest bridge
reported a run as `Passed! - Failed: 0, Passed: 94` while five tests were, in fact, failing; they
were dropped from the report entirely rather than counted.

NovaTerminal therefore runs tests through Microsoft.Testing.Platform, xUnit v3's native runner,
configured in `tests/Directory.Build.props`:

```xml
<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
<UseVSTest>false</UseVSTest>
```

A harness that under-reports failures is worse than no harness, because it turns a broken build into
a green one.

## The font looks wrong, or columns drift out of alignment

The terminal grid assumes every cell has the same advance width. If the configured font stack
resolves to a proportional font, characters will not line up.

Set a monospaced family in the configuration:

```json
{ "Appearance": { "FontFamily": "Cascadia Mono, Consolas, monospace" } }
```

Cell geometry is measured from the font itself at start-up (`TerminalView.MeasureCell`), so the grid
follows what the font actually reports rather than a hard-coded guess.
