using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>One picture, examined: a right one (<paramref name="Defect"/> null) or one with a known defect, and the questions that doubted it.</summary>
/// <param name="Raised">For a known-bad picture, the questions that doubted it and did not doubt the picture it was made from.</param>
public sealed record Trial(string Picture, Defect? Defect, IReadOnlySet<Question> Raised);

/// <summary>How one kind of defect fared: how many pictures of it, how many each question caught, how many no question did.</summary>
public sealed record KindScore(int Pictures, IReadOnlyDictionary<Question, int> Caught, int ByNone);

/// <summary>
/// What each question catches (phase 13a, I-2b): kinds of defect against
/// questions, the pictures no question caught, and the right pictures a question
/// doubted. **A number that may not get worse**, as the search gate's NDCG and
/// Stryker's `of covered` are: the recorded board is committed, and changing the
/// model, its projector, a question or its wording means measuring again.
/// </summary>
public sealed record Scoreboard(
    int Good,
    IReadOnlyDictionary<Question, int> FalseAlarms,
    IReadOnlyDictionary<Defect, KindScore> Kinds)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Scoreboard Of(IReadOnlyCollection<Trial> trials)
    {
        var good = trials.Where(trial => trial.Defect is null).ToArray();

        return new Scoreboard(
            good.Length,
            Tally(good),
            trials
                .Where(trial => trial.Defect is not null)
                .GroupBy(trial => trial.Defect!.Value)
                .OrderBy(kind => kind.Key)
                .ToDictionary(
                    kind => kind.Key,
                    kind => new KindScore(kind.Count(), Tally([.. kind]), kind.Count(trial => trial.Raised.Count == 0))));
    }

    private static Dictionary<Question, int> Tally(IReadOnlyCollection<Trial> trials) =>
        Enum.GetValues<Question>()
            .Select(question => (question, raised: trials.Count(trial => trial.Raised.Contains(question))))
            .Where(tally => tally.raised > 0)
            .ToDictionary(tally => tally.question, tally => tally.raised);

    /// <summary>
    /// Where this board is worse than the recorded one, in words — empty when it
    /// is not. Shares are compared and not counts, because the set grows with
    /// every picture a person rejects.
    /// </summary>
    public IReadOnlyList<string> FallsBelow(Scoreboard recorded)
    {
        List<string> falls = [];

        foreach (var (defect, was) in recorded.Kinds)
        {
            if (!Kinds.TryGetValue(defect, out var now))
            {
                falls.Add($"{defect} was measured on {was.Pictures} picture(s) and is not measured now.");
                continue;
            }

            foreach (var (question, caught) in was.Caught)
            {
                var today = now.Caught.GetValueOrDefault(question);
                if ((long)today * was.Pictures < (long)caught * now.Pictures)
                    falls.Add($"{question} catches {today} of {now.Pictures} {defect} pictures; it caught {caught} of {was.Pictures}.");
            }

            if ((long)now.ByNone * was.Pictures > (long)was.ByNone * now.Pictures)
                falls.Add($"No question catches {now.ByNone} of {now.Pictures} {defect} pictures; it was {was.ByNone} of {was.Pictures}.");
        }

        foreach (var (question, alarms) in FalseAlarms)
        {
            var was = recorded.FalseAlarms.GetValueOrDefault(question);
            if (recorded.Good > 0 && (long)alarms * recorded.Good > (long)was * Good)
                falls.Add($"{question} doubts {alarms} of {Good} right pictures, a false alarm each; it was {was} of {recorded.Good}.");
        }

        return falls;
    }

    public string Write() => JsonSerializer.Serialize(this, JsonOptions);

    public static Scoreboard Read(string json) =>
        JsonSerializer.Deserialize<Scoreboard>(json, JsonOptions)
        ?? throw new InvalidDataException("The recorded scoreboard is empty.");
}
