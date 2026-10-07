using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ElGuerre.Tendero.AI.Chat;
using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// The shop's model against somebody else's exam (phase 13a): ImplicitAVE's
/// 1,610 test examples — a product's picture, its title, an attribute and the
/// value a person annotated, which the title does not state. Our own pictures
/// are a hundred drawings we made; this is what says how the model reads an
/// attribute off a real product photograph, in five domains, and whether a
/// tuned model would have anything to add.
///
/// **Asked as the shop asks** (invariant 12): a closed question, the answer held
/// by a schema to the attribute's own options, compared by code. No sentence to
/// search for the value in — the benchmark's own scoring looks for the label
/// inside whatever the model wrote.
///
/// **The data is not in this repository and must not be.** Its repository
/// declares no licence, and it derives from MAVE and Amazon's product data: it
/// is downloaded to a directory outside the tree and pointed at with
/// `--dataset`. A measurement, never a gate: it has no floor and CI cannot run it.
/// </summary>
internal sealed class ImplicitAveSuite : IEvaluationSuite
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Name => "implicit-ave";

    /// <summary>Sixteen hundred pictures at a second or two each on the shop's card, and room for a slower one.</summary>
    public TimeSpan Budget => TimeSpan.FromHours(3);

    public string Description => "the model against ImplicitAVE's test set (--dataset <its evaluation_data folder>): an attribute read off a product's picture and title";

    public async Task<int> RunAsync(EvaluationOptions options, CancellationToken cancellationToken)
    {
        if (options.Dataset is not { Length: > 0 } dataset || !Directory.Exists(Path.Combine(dataset, "texts")))
        {
            Console.Error.WriteLine("--suite implicit-ave needs --dataset <dir>: ImplicitAVE's data/evaluation_data folder, with its texts and images.");
            return 2;
        }

        if (options.Model is not { Length: > 0 } modelName)
        {
            Console.Error.WriteLine("--suite implicit-ave needs --model: what the --chat endpoint serves, as the report will name it.");
            return 2;
        }

        var modality = options.Modality?.ToLowerInvariant() switch
        {
            null or "both" => ImplicitAveModality.Both,
            "image" => ImplicitAveModality.Image,
            "text" => ImplicitAveModality.Text,
            var other => throw new ArgumentException($"Unknown modality '{other}'. Available: both, image, text."),
        };

        var (examples, options_) = await ImplicitAveSet.ReadAsync(dataset, cancellationToken);
        if (options.Take is { } take)
            examples = ImplicitAveSet.Spread(examples, take);

        var model = OpenAICompatibleChat.Create(new Uri(options.Chat!), modelName);
        var answers = new List<ImplicitAveAnswer>(examples.Count);

        foreach (var example in examples)
        {
            var clock = Stopwatch.StartNew();
            var said = await AskAsync(model, dataset, example, options_[(example.Domain, example.Attribute)], modality, cancellationToken);
            answers.Add(new ImplicitAveAnswer(example, said, clock.Elapsed.TotalSeconds));

            if (answers.Count % 100 == 0)
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {answers.Count}/{examples.Count} · {ImplicitAveScore.Of(answers).Accuracy:P1} so far"));
        }

        var score = ImplicitAveScore.Of(answers);
        Console.WriteLine(ImplicitAveScore.Report(modelName, modality, score));

        if (options.ReportPath is { Length: > 0 } report)
        {
            await File.WriteAllTextAsync(
                report,
                JsonSerializer.Serialize(new { model = modelName, modality = modality.ToString(), score, answers }, Indented),
                cancellationToken);
        }

        return 0;
    }

    /// <summary>The option the model chose, or null when what it wrote is not one answer of the shape asked for.</summary>
    private static async Task<string?> AskAsync(
        IChatClient model, string dataset, ImplicitAveExample example, IReadOnlyList<string> choices, ImplicitAveModality modality,
        CancellationToken cancellationToken)
    {
        List<AIContent> asked = [];
        if (modality != ImplicitAveModality.Text)
            asked.Add(Pictures.ForModel(await File.ReadAllBytesAsync(Path.Combine(dataset, "images", example.Image), cancellationToken)));
        asked.Add(new TextContent(ImplicitAveQuestion.Ask(example, modality)));

        try
        {
            var response = await model.GetResponseAsync(
                [new ChatMessage(ChatRole.User, asked)],
                new ChatOptions
                {
                    ResponseFormat = ChatResponseFormat.ForJsonSchema(ImplicitAveQuestion.Schema(choices), "attribute_value"),
                    // The value is a few tokens; the model pads the object with white space, and a tight cap cuts it (I-4).
                    MaxOutputTokens = 100,
                    Temperature = 0,
                },
                cancellationToken);

            return JsonSerializer.Deserialize<Chosen>(response.Text, Web)?.Value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Chosen(string? Value);
}

/// <summary>What the model is given of the product besides the question.</summary>
internal enum ImplicitAveModality
{
    /// <summary>The picture and the title: the benchmark's main setting.</summary>
    Both,

    /// <summary>The picture alone: what the picture says.</summary>
    Image,

    /// <summary>The title alone: what a model with no eyes would have answered.</summary>
    Text,
}

/// <summary>One test example: a product, an attribute, and the value a person annotated.</summary>
internal sealed record ImplicitAveExample(string Domain, string Id, string Image, string Category, string Text, string Attribute, string Value);

/// <param name="Said">The option the model chose, or null when it gave no answer that could be read.</param>
internal sealed record ImplicitAveAnswer(ImplicitAveExample Example, string? Said, double Seconds)
{
    public bool Right => string.Equals(Said?.Trim(), Example.Value.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>The benchmark's files, as its authors published them.</summary>
internal static class ImplicitAveSet
{
    private const string Suffix = "_annotated_final.tsv";

    public static string DomainOf(string file) =>
        Path.GetFileName(file) is var name && name.EndsWith(Suffix, StringComparison.Ordinal) ? name[..^Suffix.Length] : Path.GetFileNameWithoutExtension(file);

    /// <summary>Three files of options for five domains: food, home, and everything worn.</summary>
    public static string OptionsFileOf(string domain) => domain switch
    {
        "Food" => "options_Food.json",
        "Home" => "options_Home.json",
        _ => "options_Clothing_Shoes_and_Jewelry.json",
    };

    public static async Task<(IReadOnlyList<ImplicitAveExample> Examples, IReadOnlyDictionary<(string Domain, string Attribute), IReadOnlyList<string>> Options)> ReadAsync(
        string dataset, CancellationToken cancellationToken)
    {
        List<ImplicitAveExample> examples = [];
        Dictionary<(string, string), IReadOnlyList<string>> options = [];

        foreach (var file in Directory.EnumerateFiles(Path.Combine(dataset, "texts"), "*" + Suffix).Order(StringComparer.Ordinal))
        {
            var domain = DomainOf(file);
            var choices = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                await File.ReadAllTextAsync(Path.Combine(dataset, OptionsFileOf(domain)), cancellationToken))!;
            var rows = Parse(domain, await File.ReadAllTextAsync(file, cancellationToken));

            foreach (var attribute in rows.Select(row => row.Attribute).Distinct(StringComparer.Ordinal))
                options[(domain, attribute)] = choices[attribute];
            examples.AddRange(rows);
        }

        return (examples, options);
    }

    /// <summary>A file's rows: tab-separated under a header, a field quoted when it holds a quote, as a spreadsheet writes it.</summary>
    public static IReadOnlyList<ImplicitAveExample> Parse(string domain, string tsv)
    {
        List<ImplicitAveExample> examples = [];
        var lines = tsv.Split('\n');

        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (line.Length == 0)
                continue;

            var fields = line.Split('\t');
            if (fields.Length != 6)
                throw new InvalidDataException($"{domain}, line {index + 1}: {fields.Length} fields where the benchmark has six.");

            examples.Add(new ImplicitAveExample(domain, fields[0], fields[1], Unquoted(fields[2]), Unquoted(fields[3]), Unquoted(fields[4]), Unquoted(fields[5])));
        }

        return examples;
    }

    /// <summary>Every n-th example, so a short run still crosses every domain and attribute in the files' order.</summary>
    public static IReadOnlyList<ImplicitAveExample> Spread(IReadOnlyList<ImplicitAveExample> examples, int take) =>
        take <= 0 || take >= examples.Count
            ? examples
            : [.. Enumerable.Range(0, take).Select(index => examples[(int)((long)index * examples.Count / take)])];

    private static string Unquoted(string field) =>
        field is ['"', .., '"'] && field.Length >= 2 ? field[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal) : field;
}

/// <summary>What an example becomes for the model: a closed question and the schema that holds its answer.</summary>
internal static class ImplicitAveQuestion
{
    public static string Ask(ImplicitAveExample example, ImplicitAveModality modality) => modality switch
    {
        ImplicitAveModality.Image =>
            $"Look at this product photograph. value: what is the {example.Attribute} of the product? Choose the one option that fits best.",
        ImplicitAveModality.Text =>
            $"A product in the category \"{example.Category}\": {example.Text} "
            + $"value: what is the {example.Attribute} of the product? Choose the one option that fits best.",
        _ =>
            $"Look at this product photograph. It is sold in the category \"{example.Category}\" as: {example.Text} "
            + $"value: what is the {example.Attribute} of the product? Choose the one option that fits best.",
    };

    public static JsonElement Schema(IReadOnlyList<string> choices) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { value = new { type = "string", @enum = choices } },
            required = new[] { "value" },
            additionalProperties = false,
        });
}

/// <summary>A share of examples answered as annotated.</summary>
internal sealed record ImplicitAvePart(string Name, int Examples, int Right)
{
    public double Accuracy => Examples == 0 ? 0 : Right / (double)Examples;
}

internal sealed record ImplicitAveScore(
    int Examples, int Right, int Unanswered, IReadOnlyList<ImplicitAvePart> ByDomain, IReadOnlyList<ImplicitAvePart> ByAttribute, double LatencyP50Seconds)
{
    public double Accuracy => Examples == 0 ? 0 : Right / (double)Examples;

    public static ImplicitAveScore Of(IReadOnlyList<ImplicitAveAnswer> answers)
    {
        IReadOnlyList<ImplicitAvePart> By(Func<ImplicitAveAnswer, string> name) =>
        [
            .. answers.GroupBy(name, StringComparer.Ordinal)
                .OrderBy(part => part.Key, StringComparer.Ordinal)
                .Select(part => new ImplicitAvePart(part.Key, part.Count(), part.Count(answer => answer.Right))),
        ];

        var latencies = answers.Select(answer => answer.Seconds).Order().ToArray();
        return new ImplicitAveScore(
            answers.Count,
            answers.Count(answer => answer.Right),
            answers.Count(answer => answer.Said is null),
            By(answer => answer.Example.Domain),
            By(answer => $"{answer.Example.Domain} · {answer.Example.Attribute}"),
            latencies.Length == 0 ? 0 : latencies[latencies.Length / 2]);
    }

    public static string Report(string model, ImplicitAveModality modality, ImplicitAveScore score)
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"{model} · {modality} · {score.Right}/{score.Examples} = {score.Accuracy:P1} · unanswered {score.Unanswered} · p50 {score.LatencyP50Seconds:F2}s"),
        };
        lines.AddRange(score.ByDomain.Select(part => string.Create(CultureInfo.InvariantCulture, $"  {part.Name,-32} {part.Right,4}/{part.Examples,-4} {part.Accuracy:P1}")));
        lines.Add("  by attribute:");
        lines.AddRange(score.ByAttribute.Select(part => string.Create(CultureInfo.InvariantCulture, $"    {part.Name,-52} {part.Right,4}/{part.Examples,-4} {part.Accuracy:P1}")));
        return string.Join(Environment.NewLine, lines);
    }
}
