using System.Text;
using NovaTerminal.Terminal;

namespace NovaTerminal.Terminal.Tests;

public sealed class CharacterWidthTests
{
    [Theory]
    [InlineData('a')]
    [InlineData('Z')]
    [InlineData('0')]
    [InlineData(' ')]
    [InlineData('~')]
    [InlineData('é')] // Precomposed e-acute occupies one column.
    [InlineData('Ж')] // Cyrillic Zhe.
    public void OrdinaryCharacters_OccupyOneColumn(char character)
    {
        Assert.Equal(CharacterWidth.SingleWidth, CharacterWidth.Measure(new Rune(character)));
    }

    [Theory]
    [InlineData(0x4E2D)]  // CJK ideograph
    [InlineData(0x3042)]  // Hiragana A
    [InlineData(0xAC00)]  // Hangul syllable
    [InlineData(0xFF21)]  // Fullwidth Latin capital A
    [InlineData(0x1F600)] // Grinning face emoji
    [InlineData(0x1F680)] // Rocket emoji
    [InlineData(0x20000)] // CJK extension B
    public void EastAsianAndEmojiCharacters_OccupyTwoColumns(int codePoint)
    {
        Assert.Equal(CharacterWidth.DoubleWidth, CharacterWidth.Measure(new Rune(codePoint)));
    }

    [Theory]
    [InlineData(0x0301)] // Combining acute accent
    [InlineData(0x0308)] // Combining diaeresis
    [InlineData(0x200D)] // Zero-width joiner
    [InlineData(0x200B)] // Zero-width space
    [InlineData(0x20E3)] // Combining enclosing keycap
    public void CombiningAndFormattingCharacters_OccupyNoColumns(int codePoint)
    {
        Assert.Equal(CharacterWidth.ZeroWidth, CharacterWidth.Measure(new Rune(codePoint)));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x07)]
    [InlineData(0x1B)]
    public void ControlCharacters_OccupyNoColumns(int codePoint)
    {
        // The parser executes these rather than printing them. Reporting zero means a stray one
        // cannot silently shift the rest of a line.
        Assert.Equal(CharacterWidth.ZeroWidth, CharacterWidth.Measure(new Rune(codePoint)));
    }

    [Fact]
    public void SoftHyphen_OccupiesAColumnDespiteBeingAFormatCharacter()
    {
        Assert.Equal(CharacterWidth.SingleWidth, CharacterWidth.Measure(new Rune(0x00AD)));
    }

    [Theory]
    [InlineData("hello", 5)]
    [InlineData("", 0)]
    [InlineData("中文", 4)]
    [InlineData("a中b", 4)]
    [InlineData("é", 1)] // "e" plus a combining accent still occupies one column.
    public void MeasuringAString_SumsItsCharacters(string text, int expected)
    {
        // The shell and the emulator must agree on where the cursor lands after printing a string;
        // a disagreement of one column desynchronises the entire line.
        Assert.Equal(expected, CharacterWidth.Measure(text));
    }

    [Fact]
    public void RangeTableBoundaries_AreInclusive()
    {
        // Off-by-one errors at a range edge would misclassify real characters, so both ends of a
        // representative range are checked along with the characters just outside it.
        Assert.False(CharacterWidth.IsWide(0x2E7F));  // Just below the CJK radicals block.
        Assert.True(CharacterWidth.IsWide(0x2E80));   // First character of it.
        Assert.True(CharacterWidth.IsWide(0x303E));   // Last character of it.
        Assert.False(CharacterWidth.IsWide(0x303F));  // Ideographic half-fill space: narrow.
    }
}
