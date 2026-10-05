using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Assist.Turns;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

/// <summary>
/// The walk over the profiles, on its own: what Steward's turns reach only through
/// a whole turn, and what Tino's will stand on.
/// </summary>
public sealed class ProfileWalkTests
{
    private sealed record Asked(string Name, ModelProfile Profile, int Needs) : IWalkCandidate;

    private static Asked Candidate(string name, int needs = 100, int window = 8192) =>
        new(name, new ModelProfile { Kind = ModelKind.Scripted, ContextTokens = window }, needs);

    private static Task<WalkResult<Asked, string>> Walk(
        IReadOnlyList<Asked> candidates, Func<Asked, CancellationToken, Task<string>> ask, Func<Asked, bool>? keeps = null,
        int seconds = 20) =>
        ProfileWalk.RunAsync("Test", candidates, seconds, TimeProvider.System, NullLogger.Instance,
            ask, keeps ?? (_ => false), CancellationToken.None);

    [Fact]
    public async Task The_first_profile_answers_and_it_is_not_a_fallback()
    {
        var walk = await Walk([Candidate("first"), Candidate("second")], (asked, _) => Task.FromResult(asked.Name));

        Assert.Equal((WalkEnd.Answered, "first", "first", false), (walk.End, walk.Answer, walk.Candidate!.Name, walk.FellBack));
    }

    [Fact]
    public async Task No_profile_is_its_own_end_and_nothing_is_asked()
    {
        var asked = 0;
        var walk = await Walk([], (_, _) => Task.FromResult((++asked).ToString(System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal((WalkEnd.NoProfile, 0, false), (walk.End, asked, walk.FellBack));
        Assert.Null(walk.Candidate);
    }

    [Fact]
    public async Task A_model_that_fails_hands_the_turn_to_the_next_profile()
    {
        var walk = await Walk([Candidate("first"), Candidate("second")], (asked, _) =>
            asked.Name == "first" ? throw new HttpRequestException("no route") : Task.FromResult(asked.Name));

        Assert.Equal((WalkEnd.Answered, "second", true), (walk.End, walk.Answer, walk.FellBack));
    }

    [Fact]
    public async Task One_profile_failing_alone_is_not_a_fallback_failing_too()
    {
        var walk = await Walk([Candidate("only")], (_, _) => throw new HttpRequestException("no route"));

        Assert.Equal((WalkEnd.ModelFailed, false), (walk.End, walk.FellBack));
        Assert.Null(walk.Candidate);
    }

    [Fact]
    public async Task Both_failing_says_a_fallback_was_reached()
    {
        var walk = await Walk([Candidate("first"), Candidate("second")], (_, _) => throw new HttpRequestException("no route"));

        Assert.Equal((WalkEnd.ModelFailed, true), (walk.End, walk.FellBack));
    }

    /// <summary>The shop's own failure would fail the same on the next profile, and cost a second turn.</summary>
    [Fact]
    public async Task A_failure_of_the_shops_own_stops_the_walk_at_that_profile()
    {
        var asked = new List<string>();
        var walk = await Walk([Candidate("first"), Candidate("second")], (candidate, _) =>
        {
            asked.Add(candidate.Name);
            throw new InvalidOperationException("the shop's own");
        });

        Assert.Equal((WalkEnd.Broken, "first", false), (walk.End, walk.Candidate!.Name, walk.FellBack));
        Assert.Equal(["first"], asked);
    }

    [Fact]
    public async Task A_profile_that_did_something_before_failing_is_not_followed_by_another()
    {
        var asked = new List<string>();
        var walk = await Walk([Candidate("first"), Candidate("second")],
            (candidate, _) =>
            {
                asked.Add(candidate.Name);
                throw new HttpRequestException("dropped half way");
            },
            keeps: candidate => candidate.Name == "first");

        Assert.Equal((WalkEnd.Kept, "first"), (walk.End, walk.Candidate!.Name));
        Assert.Equal(["first"], asked);
    }

    [Fact]
    public async Task A_window_too_small_is_skipped_and_the_next_profile_is_a_fallback()
    {
        var asked = new List<string>();
        var walk = await Walk([Candidate("small", needs: 9000), Candidate("large", needs: 9000, window: 32768)], (candidate, _) =>
        {
            asked.Add(candidate.Name);
            return Task.FromResult(candidate.Name);
        });

        Assert.Equal((WalkEnd.Answered, "large", true), (walk.End, walk.Answer, walk.FellBack));
        Assert.Equal(["large"], asked);
    }

    [Fact]
    public async Task No_window_holding_the_turn_is_too_long_whatever_else_failed()
    {
        var walk = await Walk([Candidate("small", needs: 9000), Candidate("down")], (_, _) => throw new HttpRequestException("no route"));

        Assert.Equal(WalkEnd.TooLong, walk.End);
    }

    /// <summary>What needs exactly the window fits it: the answer's room is already in what it needs.</summary>
    [Fact]
    public async Task What_needs_the_whole_window_is_still_asked()
    {
        var walk = await Walk([Candidate("exact", needs: 8192)], (asked, _) => Task.FromResult(asked.Name));

        Assert.Equal(WalkEnd.Answered, walk.End);
    }

    [Fact]
    public async Task A_turn_that_outlives_its_seconds_ends_there_and_no_other_profile_is_asked()
    {
        var asked = new List<string>();
        var walk = await Walk([Candidate("slow"), Candidate("second")], async (candidate, turn) =>
        {
            asked.Add(candidate.Name);
            await Task.Delay(Timeout.InfiniteTimeSpan, turn);
            return candidate.Name;
        }, seconds: 1);

        Assert.Equal((WalkEnd.OutOfTime, "slow"), (walk.End, walk.Candidate!.Name));
        Assert.Equal(["slow"], asked);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    public void A_token_is_four_characters_rounded_up(int characters, int tokens) =>
        Assert.Equal(tokens, ProfileWalk.Tokens(characters));
}
