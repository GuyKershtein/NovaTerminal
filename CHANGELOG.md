# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — Milestone 3: the ANSI/VT parser

- `AnsiParser`: an escape-sequence state machine following Paul Williams' VT500 model, whose state
  persists between calls so a sequence split across reads parses identically to one that arrives
  whole.
- Resumable UTF-8 decoding, rejecting overlong encodings and surrogate halves; a character split
  across a read boundary is held rather than rejected.
- `TerminalInterpreter`: cursor movement, erasing, insert/delete, scrolling regions, tab stops,
  save/restore, reset, private modes, and replies to Device Status Report and Device Attributes.
- `SgrInterpreter`: colours and attributes, including 256-colour and 24-bit forms in both the
  semicolon and colon spellings.
- Bounded parameters, parameter counts and string payloads; unknown sequences are ignored and each
  distinct one is logged at most once.
- 69 further tests, including a case that splits a sequence at every possible byte boundary.

### Added — Milestone 2: the virtual terminal

- `TerminalCell`: character, colours, attributes and grid role packed into sixteen bytes; a zeroed
  cell is a valid blank cell, so buffers need no initialisation pass.
- `TerminalLine` and `TerminalBuffer`: the screen grid, with scrolling implemented as a rotation of
  line references and recycling of the vacated line, so scrolling allocates nothing.
- Per-row damage tracking, so the renderer can repaint only what changed.
- `TerminalCursor` with the deferred-wrap ("last column") rule, visibility, shape, and DECSC/DECRC
  save and restore including the pen.
- `TerminalState`: printing, `CR`/`LF`/`RI`/`BS`/`HT`/`CBT`, cursor movement bounded by the
  margins, `EL`/`ED`/`ECH`, `ICH`/`DCH`, `IL`/`DL`, `SU`/`SD`, `DECSTBM`, resize and `RIS`.
- `CharacterWidth`: zero-, single- and double-width classification, with wide characters resolved
  from a sorted range table and combining marks from their Unicode category.
- `TabStops`, honouring `HTS` and `TBC` rather than assuming a fixed interval.
- `CellStyle` in Core, doubling as the stored cell styling and the terminal's current pen.
- 137 further tests covering wrapping, scrolling regions, wide-character corruption, erase
  semantics and resize behaviour.

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
