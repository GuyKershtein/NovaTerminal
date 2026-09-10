# Contributing to NovaTerminal

Thanks for your interest. This document describes how to build the project and what a change needs
to look like to be merged.

## Getting set up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) or newer.

```bash
dotnet restore
dotnet build
dotnet test
```

The GUI runs with:

```bash
dotnet run --project src/NovaTerminal.App
```

## Before you open a pull request

```bash
dotnet build -warnaserror      # the build must be warning-free
dotnet test                    # all tests must pass
dotnet format --verify-no-changes
```

CI runs exactly these three commands on Windows and Linux, so a green local run means a green PR.

## Architecture rules

These are not style preferences — they are enforced by tests, and a violation fails the build.

1. **The terminal engine never references a GUI toolkit.** `NovaTerminal.Core` and
   `NovaTerminal.Terminal` must be usable from a headless test or a benchmark.
2. **Dependencies point downward only.** The layer graph is acyclic; see
   `tests/NovaTerminal.Integration.Tests/LayeringTests.cs` for the exact permitted set.
3. **Native interop lives only in `NovaTerminal.Platform`.** Platform-specific behaviour belongs
   behind an abstraction, never sprinkled through the engine.
4. **Package versions live in `Directory.Packages.props`.** Projects reference packages without a
   version attribute.

If a change genuinely needs a new dependency edge, update the rule in `LayeringTests` in the same
pull request and explain why in the description. The test is the design document.

## Code style

- Nullable reference types are enabled everywhere and warnings are errors. Do not silence a
  nullability warning with `!` unless you can explain why the compiler is wrong.
- Public APIs carry XML documentation. Explain *why*, not *what* — the code already says what.
- Private instance fields are `_camelCase`; private `const` and `static readonly` are `PascalCase`.
- Prefer small, focused types. An interface should earn its place by having, or plausibly soon
  having, more than one implementation, or by marking a genuine test seam.
- Log through the source-generated `[LoggerMessage]` pattern rather than calling
  `logger.LogInformation(...)` directly, so disabled levels cost nothing.
- No magic numbers: a literal that means something gets a named constant.

## Tests

Every behavioural change needs a test. In particular:

- **Terminal engine** changes need tests written as "bytes in, screen state out".
- **Parser** changes need a test that feeds the sequence *split across multiple chunks*, because
  that is the failure mode real terminals hit and synthetic tests miss.
- **Bug fixes** start with a failing test that reproduces the bug.
- **Pseudo-terminal** changes are covered by `tests/NovaTerminal.PtyHarness`, which runs out of
  process. A process that owns a console cannot bind a child to a pseudo console, and the xUnit test
  host is a console executable, so that scenario cannot be tested in process. See
  [docs/troubleshooting.md](docs/troubleshooting.md).

Tests run on Microsoft.Testing.Platform rather than the VSTest bridge. That is deliberate: the
bridge was observed reporting a run as fully passing while five tests failed and were dropped from
the report.

Test methods are named `Member_Scenario_Expectation` so that a CI failure reads as a sentence.

## Commits and pull requests

- Keep commits scoped to one logical change.
- Write commit messages in the imperative mood: "Add scrolling region support", not "Added…".
- Fill in the pull request template, including how you verified the change.
- Update `CHANGELOG.md` under `[Unreleased]` for anything user-visible.

## Reporting bugs

Terminal bugs are much easier to fix with the exact byte sequence involved. Where possible include:

- The shell and command that produced the problem.
- The relevant escape sequences (`od -c` or a hex dump of the output is ideal).
- What you expected the screen to look like, and what it looked like instead.
- Relevant log output from the log directory named in the README.
