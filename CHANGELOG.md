# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — Milestones 9 to 13

- **Scrollback**: a bounded ring of retained lines that recycles the line it evicts, so scrolling at
  steady state allocates nothing. The bound is a hard limit, not a preference: terminal output can
  be infinite. Only the primary screen retains history.
- **Viewport**: the renderer draws rows 0..n-1 and never learns whether they came from history or
  from the live screen. Output or typing snaps the view back to the prompt.
- **Selection, copy and paste**: cell-based selection with linear and block modes, word and line
  selection, trailing blanks dropped, wrapped rows rejoined into one line, and bracketed paste.
- **Search** over history and screen, with wrap-around next/previous and bounded results.
- **Tabs**: independent terminals, each with its own engine, scrollback, shell and pump.
- **Startup watchdog**: a shell that starts but produces nothing gets diagnosed on the terminal
  itself rather than leaving a blank window.

### Fixed

- The ConPTY tests now run out of process, against the real backend, and pass. See below.

### Notes

- **A process that owns a console cannot bind a child to a pseudo console.** The child attaches to
  the inherited console instead and the pseudo console receives nothing; `FreeConsole` does not undo
  it. NovaTerminal is a GUI executable so it is unaffected, but xUnit v3 requires a console test
  host, which is why `tests/NovaTerminal.PtyHarness` exists. An earlier changelog entry attributed
  this to endpoint security software; that was wrong, and the difference is the subsystem of the
  host process. See [docs/troubleshooting.md](docs/troubleshooting.md).

### Added — Milestone 7: terminal features

- Alternate screen buffer (modes 1049, 1047 and 47), so a full-screen program can take over the
  display and hand back exactly what was there before.
- DEC Special Graphics character set with G0/G1 designation and shift in/out, which is how
  text-mode programs draw borders.
- Origin mode (DECOM): positioning relative to the scrolling region, with the cursor confined to it.
- Insert mode (IRM), cursor shape selection (DECSCUSR), bracketed paste mode, save/restore cursor
  through mode 1048, and the screen alignment pattern (DECALN).
- 25 further tests.

### Added — Milestones 5, 6 and 8: the shell

Milestones 5 and 8 were implemented together: ConPTY is the only correct way to launch an
interactive shell on Windows, so there was no intermediate step worth building.

- Windows ConPTY backend: `CreatePseudoConsole`, a process/thread attribute list, and
  `CreateProcessW`, all behind `IShellBackend` so a Unix backend can be added without touching any
  other layer.
- `ConPtyShellSession`: a dedicated reader thread feeding a bounded channel, so a flood of output
  applies backpressure instead of growing memory.
- `KeyEncoder`: the xterm key table as a pure function - arrows with and without application cursor
  mode, function keys, navigation keys, modifier parameters, control codes, alt as an escape
  prefix, and bracketed paste.
- `TerminalSession`: connects shell and engine. All engine mutation happens on the UI thread, so
  the hot path needs no locks and the renderer can never see a half-applied escape sequence.
- Keyboard input, window title, bell and shell exit are wired through to the window.

### Fixed

- `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` takes the pseudo console handle *by value*, unlike most
  attributes, which take a pointer to the value. Passing a pointer made `CreateProcess` succeed and
  the child then die during start-up with `STATUS_DLL_INIT_FAILED`.
- Tests now run through Microsoft.Testing.Platform rather than the VSTest bridge, which had been
  reporting a run as fully passing while five tests failed.

### Known issue

- On the development machine, child processes are not being bound to the pseudo console: the
  headless conhost is created correctly but the child never attaches, and the shell integration
  tests fail. An independent minimal reproduction of the documented Win32 sequence fails the same
  way, so the cause is outside NovaTerminal - see [docs/troubleshooting.md](docs/troubleshooting.md).

### Added — Milestone 4: rendering and the terminal view

- `TerminalTheme` and three built-in themes. Colours live in exactly one place; nothing in the
  rendering path contains a literal colour.
- Palette entries 16-255 are computed from the standard colour cube and greyscale ramp rather than
  stored, because they are the same in every terminal.
- `RowRunBuilder`: coalesces each row into maximal runs of identical style, so a row of plain text
  is one text-drawing call instead of eighty. Reuses its buffers, so a frame allocates nothing.
- `TerminalView`, an Avalonia control that draws the render model: colours, bold, faint, italic,
  underline, inverse, invisible and strikethrough, block/underline/bar cursors with blinking, and
  double-width glyphs.
- Viewport-driven resize: the window's pixel size is converted to rows and columns once, at the
  boundary, and the engine is resized to match.
- 23 further tests covering run coalescing and theme resolution.

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
