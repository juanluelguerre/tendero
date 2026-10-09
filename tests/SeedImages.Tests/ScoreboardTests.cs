using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// What each question catches, counted (the phase 13a spec, I-2b): a table of
/// kinds of defect against questions, the pictures no question caught, and the
/// right pictures a question doubted — and the rule that none of it may get worse.
/// </summary>
public sealed class ScoreboardTests
{
    private static Trial Good(string picture, params Question[] raised) => new(picture, null, raised.ToHashSet());

    private static Trial Bad(string picture, Defect defect, params Question[] raised) => new(picture, defect, raised.ToHashSet());

    [Fact]
    public void A_defect_is_counted_under_every_question_that_raised_it_and_apart_when_none_did()
    {
        var board = Scoreboard.Of(
        [
            Bad("a", Defect.Emblem, Question.Mark, Question.Text),
            Bad("b", Defect.Emblem, Question.Mark),
            Bad("c", Defect.Emblem),
            Bad("d", Defect.Count, Question.Count),
        ]);

        var emblem = board.Kinds[Defect.Emblem];
        Assert.Equal(3, emblem.Pictures);
        Assert.Equal(2, emblem.Caught[Question.Mark]);
        Assert.Equal(1, emblem.Caught[Question.Text]);
        Assert.Equal(1, emblem.ByNone);
        Assert.Equal(0, board.Kinds[Defect.Count].ByNone);
    }

    /// <summary>A right picture a question doubts is a false alarm, counted per question.</summary>
    [Fact]
    public void A_doubt_about_a_right_picture_is_a_false_alarm()
    {
        var board = Scoreboard.Of([Good("a"), Good("b", Question.Scene), Good("c", Question.Scene, Question.Colour)]);

        Assert.Equal(3, board.Good);
        Assert.Equal(2, board.FalseAlarms[Question.Scene]);
        Assert.Equal(1, board.FalseAlarms[Question.Colour]);
        Assert.Empty(board.Kinds);
    }

    [Fact]
    public void A_board_that_catches_as_much_as_the_recorded_one_does_not_fall_below_it()
    {
        var recorded = Scoreboard.Of([Bad("a", Defect.Emblem, Question.Mark), Bad("b", Defect.Emblem), Good("g")]);
        var today = Scoreboard.Of([Bad("a", Defect.Emblem, Question.Mark), Bad("b", Defect.Emblem, Question.Mark), Good("g")]);

        Assert.Empty(today.FallsBelow(recorded));
    }

    [Fact]
    public void A_question_that_catches_less_of_a_kind_than_recorded_is_named_with_both_numbers()
    {
        var recorded = Scoreboard.Of([Bad("a", Defect.Emblem, Question.Mark), Bad("b", Defect.Emblem, Question.Mark)]);
        var today = Scoreboard.Of([Bad("a", Defect.Emblem, Question.Mark), Bad("b", Defect.Emblem)]);

        var falls = today.FallsBelow(recorded);

        var fall = Assert.Single(falls, fall => fall.StartsWith("Mark", StringComparison.Ordinal));
        Assert.Contains("Emblem", fall);
        Assert.Contains("1 of 2", fall);
        Assert.Contains("2 of 2", fall);
        // And the picture it let through is now one no question catches.
        Assert.Single(falls, fall => fall.StartsWith("No question", StringComparison.Ordinal));
    }

    /// <summary>Each question can hold its share while a picture slips between them: that is counted on its own.</summary>
    [Fact]
    public void More_pictures_caught_by_no_question_is_a_fall_even_when_no_question_caught_less()
    {
        var recorded = Scoreboard.Of([Bad("a", Defect.Emblem, Question.Mark), Bad("b", Defect.Emblem, Question.Text)]);
        var today = Scoreboard.Of([Bad("a", Defect.Emblem, Question.Mark, Question.Text), Bad("b", Defect.Emblem)]);

        var fall = Assert.Single(today.FallsBelow(recorded));

        Assert.StartsWith("No question", fall);
    }

    [Fact]
    public void A_question_that_doubts_more_right_pictures_than_recorded_is_named()
    {
        var recorded = Scoreboard.Of([Good("a"), Good("b")]);
        var today = Scoreboard.Of([Good("a", Question.Scene), Good("b")]);

        var fall = Assert.Single(today.FallsBelow(recorded));

        Assert.Contains("Scene", fall);
        Assert.Contains("false alarm", fall);
    }

    /// <summary>The set grows as pictures are rejected: a share is compared, not a count, so more pictures is not a fall.</summary>
    [Fact]
    public void A_larger_set_is_compared_by_share()
    {
        var recorded = Scoreboard.Of([Bad("a", Defect.Emblem, Question.Mark), Bad("b", Defect.Emblem)]);
        var today = Scoreboard.Of(
        [
            Bad("a", Defect.Emblem, Question.Mark), Bad("b", Defect.Emblem),
            Bad("c", Defect.Emblem, Question.Mark), Bad("d", Defect.Emblem),
        ]);

        Assert.Empty(today.FallsBelow(recorded));
    }

    /// <summary>A kind the recorded board measured and today's does not is a fall: a measurement dropped is not a measurement passed.</summary>
    [Fact]
    public void A_kind_that_is_no_longer_measured_is_named()
    {
        var recorded = Scoreboard.Of([Bad("a", Defect.Count, Question.Count)]);
        var today = Scoreboard.Of([Bad("a", Defect.Emblem, Question.Mark)]);

        Assert.Contains(today.FallsBelow(recorded), fall => fall.Contains("Count", StringComparison.Ordinal));
    }

    [Fact]
    public void A_board_is_read_back_as_it_was_written()
    {
        var board = Scoreboard.Of([Bad("a", Defect.Emblem, Question.Mark), Bad("b", Defect.Count), Good("g", Question.Scene)]);

        var read = Scoreboard.Read(board.Write());

        Assert.Empty(read.FallsBelow(board));
        Assert.Empty(board.FallsBelow(read));
        Assert.Equal(board.Kinds[Defect.Emblem].Caught[Question.Mark], read.Kinds[Defect.Emblem].Caught[Question.Mark]);
        Assert.Equal(1, read.FalseAlarms[Question.Scene]);
    }
}
