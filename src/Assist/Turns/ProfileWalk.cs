using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Assist.Turns;

/// <summary>A profile a turn may ask, and what asking it takes of its window.</summary>
public interface IWalkCandidate
{
    string Name { get; }

    ModelProfile Profile { get; }

    /// <summary>Tokens the first call takes, the answer's room included.</summary>
    int Needs { get; }
}

/// <summary>How a walk over the profiles ended.</summary>
public enum WalkEnd
{
    /// <summary>A profile answered.</summary>
    Answered,

    /// <summary>No profile is configured and registered.</summary>
    NoProfile,

    /// <summary>Every profile asked failed as a model fails: the network, a timeout, a provider's status.</summary>
    ModelFailed,

    /// <summary>The turn outlived its seconds. No other profile was asked: it would spend the same and end the same.</summary>
    OutOfTime,

    /// <summary>The shop's own code failed, not the model's. No other profile was asked.</summary>
    Broken,

    /// <summary>A model failed after doing something that must not be done twice, so no other was asked.</summary>
    Kept,

    /// <summary>No profile's window holds the conversation and an answer.</summary>
    TooLong,
}

/// <param name="Candidate">The profile that answered, or the one the walk stopped at; none when it ran out of them.</param>
/// <param name="FellBack">Whether a profile was reached after another: one that failed alone was not a fallback failing too.</param>
public sealed record WalkResult<TCandidate, TAnswer>(WalkEnd End, TAnswer? Answer, TCandidate? Candidate, bool FellBack)
    where TCandidate : class, IWalkCandidate;

/// <summary>
/// What a turn of any of the shop's agents does the same way (Steward, phase 12;
/// Tino, phase 12b): ask the profile that answers and, when it fails as a model
/// fails, the one behind it — within the turn's seconds, and never a profile whose
/// window cannot hold the question. What a turn asks, and what it makes of the
/// end, is the agent's own.
/// </summary>
public static partial class ProfileWalk
{
    /// <summary>The profiles named, in order, that are configured and registered: a name with no client is skipped.</summary>
    public static IReadOnlyList<(string Name, IChatClient Client, ModelProfile Profile)> Find(
        IServiceProvider services, ModelProfiles profiles, params string?[] names) =>
    [
        .. names.OfType<string>().Where(name => name.Length > 0).Distinct()
            .Select(name => (Name: name, Client: services.GetKeyedService<IChatClient>(name), Profile: profiles.Find(name)))
            .Where(found => found.Client is not null && found.Profile is not null)
            .Select(found => (found.Name, found.Client!, found.Profile!)),
    ];

    /// <summary>Four characters a token, rounded up: an estimate, which the window's
    /// margin and the near-limit flag absorb.</summary>
    public static int Tokens(int characters) => (characters + 3) / 4;

    /// <param name="agent">Whose turn it is, for the log.</param>
    /// <param name="ask">The turn on one profile. It throws when the model fails.</param>
    /// <param name="keeps">Whether a profile that failed had already done something a
    /// second profile must not do again — Steward's stored proposals.</param>
    public static async Task<WalkResult<TCandidate, TAnswer>> RunAsync<TCandidate, TAnswer>(
        string agent, IReadOnlyList<TCandidate> candidates, int turnSeconds, TimeProvider clock, ILogger logger,
        Func<TCandidate, CancellationToken, Task<TAnswer>> ask, Func<TCandidate, bool> keeps,
        CancellationToken cancellationToken)
        where TCandidate : class, IWalkCandidate
    {
        if (candidates.Count == 0)
            return new(WalkEnd.NoProfile, default, null, false);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(turnSeconds), clock);
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var fellBack = false;
        var tooLong = false;
        var passedOne = false;
        foreach (var candidate in candidates)
        {
            // Falling back is reaching a profile after another: one that failed alone
            // was not a fallback failing too, and the reader must not be told it was.
            fellBack |= passedOne;
            passedOne = true;

            // A window that cannot hold the question and an answer is not asked: the
            // model would cut the history and answer something else.
            if (candidate.Needs > candidate.Profile.ContextTokens)
            {
                tooLong = true;
                continue;
            }

            try
            {
                return new(WalkEnd.Answered, await ask(candidate, turn.Token), candidate, fellBack);
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
            {
                // Only the model failing is worth another profile. The turn outliving
                // its deadline, or a failure of the shop's own, would spend the same
                // on the fallback and end the same.
                if (deadline.IsCancellationRequested)
                {
                    LogOutlived(logger, failure, agent, candidate.Name, turnSeconds);
                    return new(WalkEnd.OutOfTime, default, candidate, fellBack);
                }

                if (!ModelFailures.IsModelFailure(failure))
                {
                    LogBroken(logger, failure, agent, candidate.Name);
                    return new(WalkEnd.Broken, default, candidate, fellBack);
                }

                LogFailed(logger, failure, agent, candidate.Name);
                if (keeps(candidate))
                    return new(WalkEnd.Kept, default, candidate, fellBack);
            }
        }

        return new(tooLong ? WalkEnd.TooLong : WalkEnd.ModelFailed, default, null, fellBack);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Agent}'s profile {Profile} failed a turn")]
    private static partial void LogFailed(ILogger logger, Exception failure, string agent, string profile);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Agent}'s turn on {Profile} outlived its {Seconds} seconds")]
    private static partial void LogOutlived(ILogger logger, Exception failure, string agent, string profile, int seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Agent}'s turn on {Profile} broke on the shop's side, not the model's")]
    private static partial void LogBroken(ILogger logger, Exception failure, string agent, string profile);
}
