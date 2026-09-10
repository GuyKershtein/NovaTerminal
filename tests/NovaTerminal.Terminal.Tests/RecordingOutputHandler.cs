using System.Text;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.Terminal.Tests;

/// <summary>
/// Records what the parser recognised, so tests can assert on the grammar independently of whether
/// the engine implements a given function.
/// </summary>
/// <remarks>
/// A <see cref="CsiSequence"/> is a ref struct that borrows the parser's buffers, so it cannot be
/// stored. Copying the parts out is exactly what a real handler does too - and it is what makes the
/// borrowing safe.
/// </remarks>
internal sealed class RecordingOutputHandler : ITerminalOutputHandler
{
    private readonly StringBuilder _text = new();

    public List<string> Events { get; } = [];

    public List<CsiRecord> CsiSequences { get; } = [];

    /// <summary>Everything that was printed, as text.</summary>
    public string Text => _text.ToString();

    public void Print(Rune rune)
    {
        _text.Append(rune);
        Events.Add($"print:{rune}");
    }

    public void Execute(byte control) => Events.Add($"execute:0x{control:X2}");

    public void EscapeDispatch(char final, char intermediate)
    {
        var prefix = intermediate == CsiSequence.None ? string.Empty : intermediate.ToString();
        Events.Add($"esc:{prefix}{final}");
    }

    public void CsiDispatch(in CsiSequence sequence)
    {
        var parameters = new int[sequence.Count];
        var subParameters = new bool[sequence.Count];

        for (var index = 0; index < sequence.Count; index++)
        {
            parameters[index] = sequence[index];
            subParameters[index] = sequence.IsSubParameter(index);
        }

        var record = new CsiRecord(parameters, subParameters, sequence.PrivateMarker, sequence.Intermediate, sequence.Final);
        CsiSequences.Add(record);
        Events.Add($"csi:{record}");
    }

    public void OscDispatch(ReadOnlySpan<byte> data) => Events.Add($"osc:{Encoding.UTF8.GetString(data)}");

    internal sealed record CsiRecord(
        int[] Parameters,
        bool[] SubParameters,
        char PrivateMarker,
        char Intermediate,
        char Final)
    {
        public override string ToString()
        {
            var marker = PrivateMarker == CsiSequence.None ? string.Empty : PrivateMarker.ToString();
            var intermediate = Intermediate == CsiSequence.None ? string.Empty : Intermediate.ToString();
            var parameters = string.Join(";", Parameters);
            return $"{marker}{parameters}{intermediate}{Final}";
        }
    }
}
