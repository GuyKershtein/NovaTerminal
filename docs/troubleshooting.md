# Troubleshooting

## The terminal opens but stays blank, or the shell exits immediately

**Symptom.** NovaTerminal starts and the window appears, but no prompt is ever drawn. The log shows
a session being created successfully:

```
Session 8f37ceac: started 'C:\WINDOWS\system32\WindowsPowerShell\v1.0\powershell.exe' as process 19044 at 115x29.
```

You may also see the shell fail on its own with `0xC0000142` (`STATUS_DLL_INIT_FAILED`), or its
output appear in some *other* console rather than in NovaTerminal.

**Cause.** The child process was created, but it was not bound to the pseudo console.

Attaching a child to a pseudo console is not done by handing it pipes. It is done by passing an
attribute list to `CreateProcess`:

```
InitializeProcThreadAttributeList(...)
UpdateProcThreadAttribute(list, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, hPC, sizeof(HPCON), ...)
CreateProcess(..., EXTENDED_STARTUPINFO_PRESENT, ..., &startupInfoEx, ...)
```

If something intercepts process creation and re-launches the child on your behalf, that attribute
list is not carried across. `CreateProcess` still returns success and the process still runs — it
simply has no console, so a shell falls back to whatever console it can find and its output never
reaches the emulator. Endpoint security software is the usual source of such interception.

**How to confirm it.** Two observations distinguish this from a bug in NovaTerminal:

1. **A headless conhost is still created.** While a session is running, look for it:

   ```powershell
   Get-CimInstance Win32_Process -Filter "Name='conhost.exe'" |
       Where-Object CommandLine -match headless |
       Select-Object ProcessId, CommandLine
   ```

   A line like `conhost.exe --headless --width 115 --height 29 --signal 0x2f0 --server 0x2ec`
   means the pseudo console itself was created correctly, with the size NovaTerminal asked for.
   The operating system side is working.

2. **The child produces no output on the pipe.** The integration tests in
   `tests/NovaTerminal.Integration.Tests/ShellSessionTests.cs` assert exactly this, and fail with a
   message describing the attachment problem rather than a generic timeout.

If both hold, the pseudo console is fine and the *binding* of the child to it is being lost outside
NovaTerminal.

**What to do about it.**

- Add an exclusion for the NovaTerminal build output (or the repository) in your security software,
  then run the integration tests again.
- Check whether other pseudo-console consumers work on the same machine — the Windows Terminal or
  Visual Studio Code integrated terminals are the easiest to try. If they are also affected, the
  cause is certainly not NovaTerminal.
- Verify on a machine, container or CI runner without the interfering software. The GitHub Actions
  Windows runner has none, which is why the integration tests are part of CI.

## Tests report as passing when they are not

Fixed, but worth knowing about if you see it elsewhere. Running xUnit v3 through the VSTest bridge
reported a run as `Passed! - Failed: 0, Passed: 94` while five tests were, in fact, failing; they
were dropped from the report entirely rather than counted.

NovaTerminal therefore runs tests through Microsoft.Testing.Platform, xUnit v3's native runner,
which is configured in `tests/Directory.Build.props`:

```xml
<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
<UseVSTest>false</UseVSTest>
```

A harness that under-reports failures is worse than no harness, because it converts a broken build
into a green one.

## The font looks wrong, or columns drift out of alignment

The terminal grid assumes every cell has the same advance width. If the configured font stack
resolves to a proportional font, characters will not line up.

Set a monospaced family in the configuration:

```json
{ "Appearance": { "FontFamily": "Cascadia Mono, Consolas, monospace" } }
```

Cell geometry is measured from the font itself at startup (`TerminalView.MeasureCell`), so the grid
follows whatever the font actually reports rather than a hard-coded guess.
