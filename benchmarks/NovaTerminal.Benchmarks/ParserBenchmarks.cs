using System.Text;
using BenchmarkDotNet.Attributes;
using NovaTerminal.Core;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.Benchmarks;

/// <summary>
/// Measures the path a flood of output takes: bytes to parser to engine.
/// </summary>
/// <remarks>
/// <para>
/// The workloads are the shapes real output actually takes. Plain text is the common case - a build
/// log, a file being printed. Coloured text is what a modern tool emits, where escape sequences can
/// outnumber the characters they style. The full-screen workload is what an editor does: cursor
/// positioning and erasing rather than printing.
/// </para>
/// <para>
/// Throughput matters because the terminal must stay responsive while a command produces thousands
/// of lines. A parser that manages a few megabytes a second is fine; one that manages a few hundred
/// kilobytes is a visible stutter.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ParserBenchmarks
{
    private const string Esc = "\u001b";

    private byte[] _plainText = [];
    private byte[] _colouredText = [];
    private byte[] _fullScreenRedraw = [];
    private byte[] _wideText = [];

    private TerminalState _terminal = null!;
    private AnsiParser _parser = null!;

    /// <summary>How many lines each workload contains.</summary>
    [Params(1000)]
    public int Lines { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _terminal = new TerminalState(new TerminalSize(120, 40));
        _terminal.ConfigureScrollback(10_000);
        _parser = new AnsiParser(new TerminalInterpreter(_terminal));

        _plainText = Build(line => $"[{line:D6}] the quick brown fox jumps over the lazy dog\r\n");

        _colouredText = Build(line =>
            $"{Esc}[32m[{line:D6}]{Esc}[0m {Esc}[1;34minfo{Esc}[0m " +
            $"{Esc}[38;2;200;120;60mmodule{Esc}[0m ready\r\n");

        _fullScreenRedraw = Build(line =>
            $"{Esc}[{(line % 40) + 1};1H{Esc}[K{Esc}[7m row {line} {Esc}[0m");

        _wideText = Build(line => $"{line:D4} 中文字符测试 こんにちは 世界\r\n");
    }

    /// <summary>Plain text: the common case, and the one the fast path exists for.</summary>
    [Benchmark(Baseline = true)]
    public void PlainText() => _parser.Parse(_plainText);

    /// <summary>Coloured output, where escape sequences outnumber the styled characters.</summary>
    [Benchmark]
    public void ColouredText() => _parser.Parse(_colouredText);

    /// <summary>Cursor positioning and erasing, as a full-screen program does.</summary>
    [Benchmark]
    public void FullScreenRedraw() => _parser.Parse(_fullScreenRedraw);

    /// <summary>Double-width text, which costs a width lookup per character.</summary>
    [Benchmark]
    public void WideCharacters() => _parser.Parse(_wideText);

    private byte[] Build(Func<int, string> lineFactory)
    {
        var builder = new StringBuilder();

        for (var line = 0; line < Lines; line++)
        {
            builder.Append(lineFactory(line));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
