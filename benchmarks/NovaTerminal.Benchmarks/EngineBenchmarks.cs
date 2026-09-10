using System.Text;
using BenchmarkDotNet.Attributes;
using NovaTerminal.Core;
using NovaTerminal.Rendering;
using NovaTerminal.Terminal;

namespace NovaTerminal.Benchmarks;

/// <summary>
/// Measures the engine and render-model operations that run most often.
/// </summary>
/// <remarks>
/// Scrolling and run coalescing happen once per line and once per frame respectively, so their cost
/// is multiplied by everything. Both were designed around specific claims - that scrolling allocates
/// nothing, and that coalescing turns a row into a handful of draw calls - and a benchmark is how
/// those claims stop being assertions.
/// </remarks>
[MemoryDiagnoser]
public class EngineBenchmarks
{
    private TerminalState _terminal = null!;
    private TerminalBuffer _buffer = null!;
    private TerminalBuffer _bufferWithHistory = null!;
    private RowRunBuilder _runBuilder = null!;
    private ScrollRegion _fullScreen;

    [GlobalSetup]
    public void Setup()
    {
        var size = new TerminalSize(120, 40);

        _terminal = new TerminalState(size);
        _runBuilder = new RowRunBuilder();
        _fullScreen = ScrollRegion.FullScreen(size);

        _buffer = new TerminalBuffer(size);
        _bufferWithHistory = new TerminalBuffer(size) { Scrollback = new Scrollback(10_000) };

        // A screen of realistic content: mostly plain, with styled spans scattered through it.
        for (var row = 0; row < size.Rows; row++)
        {
            _terminal.MoveCursorTo(0, row);
            _terminal.CurrentStyle = CellStyle.Default;
            _terminal.Print("plain text at the start of the line ");
            _terminal.CurrentStyle = CellStyle.Default.WithForeground(TerminalColor.FromAnsi(AnsiColor.Green));
            _terminal.Print("green ");
            _terminal.CurrentStyle = CellStyle.Default.WithAttributes(TextAttributes.Bold);
            _terminal.Print("bold ");
            _terminal.CurrentStyle = CellStyle.Default;
            _terminal.Print("and the rest");
        }

        FillBuffer(_buffer);
        FillBuffer(_bufferWithHistory);
    }

    /// <summary>Scrolling with no history: the pure line-rotation cost.</summary>
    [Benchmark]
    public void ScrollWithoutHistory() => _buffer.ScrollUp(_fullScreen, 1, CellStyle.Default);

    /// <summary>
    /// Scrolling into a full scrollback, where the evicted line is recycled into the screen.
    /// </summary>
    [Benchmark]
    public void ScrollIntoHistory() => _bufferWithHistory.ScrollUp(_fullScreen, 1, CellStyle.Default);

    /// <summary>Coalescing one row of mixed styling into runs, as every frame does per row.</summary>
    [Benchmark]
    public int CoalesceRow()
    {
        var runs = _runBuilder.Build(_terminal.Buffer.GetRow(0));
        return runs.Count;
    }

    /// <summary>Coalescing a whole screen: one frame of the render model.</summary>
    [Benchmark]
    public int CoalesceScreen()
    {
        var total = 0;

        for (var row = 0; row < _terminal.Size.Rows; row++)
        {
            total += _runBuilder.Build(_terminal.Buffer.GetRow(row)).Count;
        }

        return total;
    }

    /// <summary>
    /// Printing text straight into the engine, with no parser involved.
    /// </summary>
    /// <remarks>
    /// Comparing this with the parser benchmark on the same text separates the two costs. If they
    /// are close, the parser is not where the time goes and optimising it would be wasted effort.
    /// </remarks>
    [Benchmark]
    public void PrintTextDirectly()
    {
        for (var line = 0; line < 1000; line++)
        {
            _terminal.Print("[000000] the quick brown fox jumps over the lazy dog");
            _terminal.CarriageReturn();
            _terminal.LineFeed();
        }
    }

    /// <summary>Extracting the screen as text, which selection and search both rely on.</summary>
    [Benchmark]
    public int ExtractText() => _terminal.Buffer.GetText().Length;

    private static void FillBuffer(TerminalBuffer buffer)
    {
        var text = "the quick brown fox jumps over the lazy dog";

        for (var row = 0; row < buffer.Size.Rows; row++)
        {
            for (var column = 0; column < Math.Min(text.Length, buffer.Size.Columns); column++)
            {
                buffer.SetCell(column, row, new TerminalCell(new Rune(text[column]), CellStyle.Default));
            }
        }
    }
}
