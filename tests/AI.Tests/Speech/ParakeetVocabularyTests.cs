using ElGuerre.Tendero.AI.Speech.Parakeet;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests.Speech;

/// <summary>
/// From Parakeet's token ids to words: SentencePiece pieces, where "▁" starts a
/// word, and the blank and the special pieces never written.
/// </summary>
public sealed class ParakeetVocabularyTests
{
    private static readonly ParakeetVocabulary Vocabulary =
        new(["<unk>", "<|nospeech|>", "▁zapa", "tillas", "▁para", "▁correr", ".", "▁12", "<blank>"]);

    [Fact]
    public void Pieces_are_joined_into_words_by_their_word_marks() =>
        Assert.Equal("zapatillas para correr.", Vocabulary.Words([2, 3, 4, 5, 6]));

    [Fact]
    public void Special_pieces_and_the_blank_are_never_written() =>
        Assert.Equal("12", Vocabulary.Words([1, 7, 8, 0]));

    [Fact]
    public void No_tokens_are_no_words() =>
        Assert.Equal("", Vocabulary.Words([]));
}
