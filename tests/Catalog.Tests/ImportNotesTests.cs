using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// Each note the import answers says its sentence in the language asked, starts
/// with what it is about when it is about something, and carries the source's
/// detail (ADR 0059: the code for a front end, the sentence for whoever reads the
/// answer as it is). Found by phase 16's Stryker close: which language, and the
/// "about" prefix, survived every mutation.
/// </summary>
public sealed class ImportNotesTests
{
    /// <summary>Every code the import can say; a new one is here before it ships.</summary>
    public static TheoryData<ImportNoteCode> Codes() => [.. Enum.GetValues<ImportNoteCode>()];

    [Theory]
    [MemberData(nameof(Codes))]
    public void A_note_is_said_in_the_language_asked_with_the_sources_detail(ImportNoteCode code)
    {
        var note = new SourceNote(code, "8001", "Coffee Makers");

        var (spanish, english) = (ImportNotes.Said(note, english: false), ImportNotes.Said(note, english: true));

        Assert.Equal((code, "8001"), (spanish.Code, spanish.About));
        Assert.NotEqual(spanish.Text, english.Text);
        Assert.All([spanish.Text, english.Text], text => Assert.True(text.Length > 20, text));
        // A description's layout has no detail to carry: the product is the whole of it.
        if (code != ImportNoteCode.DescriptionHadLayout)
            Assert.All([spanish.Text, english.Text], text => Assert.Contains("Coffee Makers", text, StringComparison.Ordinal));
    }

    /// <summary>
    /// A note about one thing starts with it; a note about the source as a whole
    /// (a language it publishes) has nothing in front.
    /// </summary>
    [Theory]
    [MemberData(nameof(Codes))]
    public void A_note_about_something_starts_with_it(ImportNoteCode code)
    {
        var about = ImportNotes.Said(new SourceNote(code, "8001", "x"), english: true).Text;
        var whole = ImportNotes.Said(new SourceNote(code, null, "x"), english: true).Text;

        Assert.Equal(code != ImportNoteCode.LocaleIgnored, about.StartsWith("8001: ", StringComparison.Ordinal));
        Assert.False(whole.StartsWith(':'));
        Assert.True(char.IsUpper(whole[0]), $"A note about nothing in particular still starts a sentence: {whole}");
        if (code != ImportNoteCode.LocaleIgnored)
            Assert.Equal(Sentences.StartingUpper(about["8001: ".Length..]), whole);
        Assert.DoesNotContain("8001", whole, StringComparison.Ordinal);
    }
}
