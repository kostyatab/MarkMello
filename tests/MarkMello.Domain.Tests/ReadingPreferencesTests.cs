using MarkMello.Domain;

namespace MarkMello.Domain.Tests;

public sealed class ReadingPreferencesTests
{
    [Fact]
    public void NormalizeReturnsDefaultsForNull()
    {
        var normalized = ReadingPreferences.Normalize(null);

        Assert.Equal(ReadingPreferences.Default, normalized);
        Assert.Equal(DocumentMinimapMode.Off, normalized.DocumentMinimapMode);
    }

    [Fact]
    public void DefaultsAreSans14WithLineHeight16AndNoMinimap()
    {
        Assert.Equal(
            new ReadingPreferences(FontFamilyMode.Sans, 14, 1.6, 820, DocumentMinimapMode.Off),
            ReadingPreferences.Default);
        Assert.Equal(12, ReadingPreferences.MinFontSize);
        Assert.Equal(24, ReadingPreferences.MaxFontSize);
    }

    [Fact]
    public void NormalizeClampsAndRoundsOutOfRangeValues()
    {
        var candidate = new ReadingPreferences(
            FontFamily: (FontFamilyMode)42,
            FontSize: 200,
            LineHeight: 0.2,
            ContentWidth: 517,
            DocumentMinimapMode: (DocumentMinimapMode)42);

        var normalized = ReadingPreferences.Normalize(candidate);

        Assert.Equal(FontFamilyMode.Sans, normalized.FontFamily);
        Assert.Equal(ReadingPreferences.MaxFontSize, normalized.FontSize);
        Assert.Equal(ReadingPreferences.MinLineHeight, normalized.LineHeight);
        Assert.Equal(ReadingPreferences.MinContentWidth, normalized.ContentWidth);
        Assert.Equal(DocumentMinimapMode.Off, normalized.DocumentMinimapMode);
    }

    [Theory]
    [InlineData(580, ReadingPreferences.NarrowContentWidth)]
    [InlineData(720, ReadingPreferences.MediumContentWidth)]
    [InlineData(860, ReadingPreferences.WideContentWidth)]
    public void NormalizeMigratesLegacyPresetContentWidths(int legacyWidth, int expectedWidth)
    {
        var candidate = ReadingPreferences.Default with { ContentWidth = legacyWidth };

        var normalized = ReadingPreferences.Normalize(candidate);

        Assert.Equal(expectedWidth, normalized.ContentWidth);
    }
    [Theory]
    [InlineData(DocumentMinimapMode.Auto)]
    [InlineData(DocumentMinimapMode.On)]
    [InlineData(DocumentMinimapMode.Off)]
    public void NormalizePreservesSupportedDocumentMinimapModes(DocumentMinimapMode mode)
    {
        var candidate = ReadingPreferences.Default with { DocumentMinimapMode = mode };

        var normalized = ReadingPreferences.Normalize(candidate);

        Assert.Equal(mode, normalized.DocumentMinimapMode);
    }

}
