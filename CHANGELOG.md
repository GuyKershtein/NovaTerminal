# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — Milestone 1: project foundation

- Seven-project solution enforcing a layered architecture: `Core`, `Terminal`, `Process`,
  `Platform`, `Input`, `Rendering`, `App`.
- Core value types: `TerminalSize`, `TerminalColor` (default / 256-colour palette / 24-bit RGB,
  packed into a single `uint`), `TextAttributes`, `CursorStyle`, `AnsiColor`.
- Strongly typed configuration model (`NovaTerminalOptions`) with validation, including a hard
  ceiling on scrollback retention.
- Shell abstractions `IShellSession` and `IShellBackend`, with a documented concurrency contract.
- `PlatformSupport` capability probe, detecting ConPTY availability (Windows 10 build 17763+).
- `CellMetrics`, the single conversion point between device-independent pixels and terminal cells.
- `VtConstants`: the C0 control set and escape-sequence grammar bytes, with bounds for untrusted
  sequence parameters.
- Avalonia application shell with dependency injection and source-generated structured logging
  through Serilog.
- 95 tests, including architecture tests that fail the build if a layer gains a forbidden
  dependency or if the terminal engine acquires a GUI reference.
- GitHub Actions CI: build and test on Windows and Linux, warnings as errors, formatting
  verification.

[Unreleased]: https://github.com/OWNER/NovaTerminal/commits/main
