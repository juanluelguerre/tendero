using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// Parsed by hand: there are a handful of options and they do not justify a
/// package (CLAUDE.md's dependency policy).
/// </summary>
public sealed record EvaluationOptions(
    string Suite,
    string Elasticsearch,
    string GoldenDirectory,
    string ThresholdsPath,
    string SeedPath,
    string? ReportPath,
    bool FailUnderThresholds,
    string Strategy,
    string Qdrant,
    string? QdrantKey,
    string Embeddings,
    string? Reranker,
    string? PoolPath = null,
    string? Split = null,
    string? RerankerModel = null,
    string? ScopeDirectory = null,
    string? CategoriesPath = null,
    bool WriteCalibration = false,
    string? CalibrationPath = null,
    string? Extractor = null,
    string? Chat = null,
    string? Model = null,
    string? Scorer = null,
    string? Tei = null,
    string? AttributesPath = null,
    string? Onnx = null,
    string? ClaimsDirectory = null,
    string? Database = null,
    string? Script = null,
    int ContextTokens = 8192,
    string? CopilotDirectory = null,
    string? AssistantDirectory = null,
    string? Only = null,
    string? Dataset = null,
    string? Modality = null,
    int? Take = null,
    string? Format = null,
    string? Vocabulary = null,
    string? Siglip = null,
    string? Recordings = null,
    string? Whisper = null,
    string? Parakeet = null,
    string? Lines = null)
{
    public bool IsHybrid => Strategy == SearchStrategies.Hybrid;

    public static string Usage(IReadOnlyList<Suites.IEvaluationSuite> suites) =>
        $"""
        Usage: dotnet run --project tools/SearchEval -- [options]

          --suite <name>          Which gate to run (default search)
        {string.Join(Environment.NewLine, suites.Select(s => $"                            {s.Name}: {s.Description}"))}
          --strategy <name>       lexical (default), or hybrid to score lexical AND hybrid side by side
          --elasticsearch <url>   Elasticsearch endpoint (default http://localhost:9200)
          --qdrant <url>          Qdrant's gRPC endpoint, for hybrid (default http://localhost:6334)
          --qdrant-key <key>      Qdrant's API key, when it has one
          --embeddings <url>      Text Embeddings Inference serving bge-m3, for hybrid (default http://localhost:8080)
          --reranker <url>        Text Embeddings Inference serving bge-reranker-v2-m3; reranks hybrid when given
          --reranker-model <m@sha> A candidate reranker the --reranker endpoint serves instead, pinned by full commit SHA
          --siglip <dir>          With hybrid: our SigLIP 2 export (models/siglip2). Also scores words against the
                                  catalogue's pictures, ungated, and prints what a similarity floor would separate
          --golden <dir>          Golden set directory  (default tools/SearchEval/golden)
          --thresholds <file>     Committed thresholds  (default tools/SearchEval/eval.thresholds.json)
          --seed <file>           Seed catalogue        (default seed/products.sample.json)
          --report <file>         Write the Markdown report to a file as well
          --pool <file>           Write every strategy's unjudged first-ten results as JSON, to be judged
          --split <name>          tune or holdout: score only that half of the golden set (not with --ci)
          --ci                    Exit non-zero when a strategy falls under its thresholds in any culture
          --database <conn>       copilot: a disposable Postgres; migrated, and seeded when empty
          --chat <url> --model <id>  copilot: an OpenAI-compatible endpoint and the model it serves
          --script <file>         copilot: a Scripted model instead, for a dry run of the suite itself
          --context <tokens>      copilot: the profile's window (default 8192)
          --only <ids>            assistant: only the cases whose id contains one of these, comma separated
          --recordings <dir>      voice: the WAVs tools/voice-eval/prepare.mjs wrote (default artifacts/voice-eval)
          --whisper <url>         voice: a whisper.cpp server to measure (whisper-server, with its VAD)
          --parakeet <dir>        voice: a Parakeet export to measure in this process (models/parakeet)
          --lines <file>          voice: what was said (default docs/analysis/voice-recordings.md)

        The scope suite (--suite scope) needs only --embeddings, and reads:
          --scope <dir>           Scope query directory (default tools/SearchEval/scope)
          --categories <file>     Category seed         (default seed/categories.sample.json)
          --write-calibration     When the protocol ACCEPTS, write the chosen margin and its fingerprint
          --calibration <file>    Where to write it     (default tools/SearchEval/scope/query-scope.calibration.json)
          --ci                    With scope: exit 1 when the protocol refutes layer B

        The claims suite (--suite claims) scores an extractor against knowledge/claims.json:
          --knowledge <dir>       Claim and support sets (default tools/SearchEval/knowledge)
          --extractor <name>      rules (default), or chat: a generative model over an OpenAI-compatible endpoint
          --chat <url>            That endpoint (default http://localhost:38090)
          --onnx <dir>            Or a maker's ONNX model folder, run in process by ONNX Runtime GenAI
          --model <label>         What it serves, as it will appear in the report (e.g. qwen3.5-4b-q8)
          --attributes <file>     Attribute seed        (default seed/attributes.sample.json)
          --vocabulary <name>     whole (default: every attribute, as the floors were set) or category:
                                  what each product's category declares (ADR 0056), read from --categories
          --report <file>         Every prediction and its verdict, as JSON
          --ci                    With claims or support: exit 1 under the floors in knowledge/gates.json

        The ImplicitAVE suite (--suite implicit-ave) asks a model read attributes off product photographs:
          --dataset <dir>         ImplicitAVE's data/evaluation_data folder, downloaded outside this repository
          --chat <url> --model <id>  The OpenAI-compatible endpoint, and what it serves
          --modality <name>       both (default: picture and title), image, or text
          --format <name>         plain (default: the option as text, with its probability) or json (held by a schema)
          --take <n>              Only n examples, spread over the whole set
          --report <file>         Every answer beside its annotation, as JSON

        The support suite (--suite support) scores a scorer against knowledge/support.json:
          --scorer <name>         nli, reranker (both through TEI at --tei) or yesno (through --chat)
          --tei <url>             Text Embeddings Inference serving the scorer (default http://localhost:38091)
        """;

    public static EvaluationOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ci = false;
        var writeCalibration = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (argument == "--ci")
            {
                ci = true;
                continue;
            }

            if (argument == "--write-calibration")
            {
                writeCalibration = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{argument}'.");

            if (index + 1 >= args.Length)
                throw new ArgumentException($"Option '{argument}' needs a value.");

            values[argument[2..]] = args[++index];
        }

        var strategy = values.GetValueOrDefault("strategy", SearchStrategies.Lexical);
        if (strategy is not (SearchStrategies.Lexical or SearchStrategies.Hybrid))
            throw new ArgumentException($"Unknown strategy '{strategy}'. Available: lexical, hybrid.");

        // A split is for tuning runs. The gate measures every query, and a gate that
        // could be pointed at half of them would pass on whichever half it liked.
        var split = values.GetValueOrDefault("split");
        if (split is not (null or "tune" or "holdout"))
            throw new ArgumentException($"Unknown split '{split}'. Available: tune, holdout.");
        if (split is not null && ci)
            throw new ArgumentException("--split cannot be combined with --ci: the gate measures every query.");

        // A candidate is measured, never gated: it names what the endpoint serves,
        // so it means nothing without one.
        var rerankerModel = values.GetValueOrDefault("reranker-model");
        if (rerankerModel is not null && !values.ContainsKey("reranker"))
            throw new ArgumentException("--reranker-model names the model a --reranker endpoint serves, and no --reranker was given.");
        if (rerankerModel is not null && rerankerModel.Split('@') is not [{ Length: > 0 }, { Length: > 0 }])
            throw new ArgumentException($"--reranker-model is <model id>@<full commit SHA>, and '{rerankerModel}' is not.");

        // The pictures share the words' vector store, so they are measured where it is.
        var siglip = values.GetValueOrDefault("siglip");
        if (siglip is not null && strategy is not SearchStrategies.Hybrid)
            throw new ArgumentException("--siglip measures the pictures in the vector store, and needs --strategy hybrid.");

        return new EvaluationOptions(
            values.GetValueOrDefault("suite", "search"),
            values.GetValueOrDefault("elasticsearch", "http://localhost:9200"),
            values.GetValueOrDefault("golden", Path.Combine("tools", "SearchEval", "golden")),
            values.GetValueOrDefault("thresholds", Path.Combine("tools", "SearchEval", "eval.thresholds.json")),
            values.GetValueOrDefault("seed", Path.Combine("seed", "products.sample.json")),
            values.GetValueOrDefault("report"),
            ci,
            strategy,
            values.GetValueOrDefault("qdrant", "http://localhost:6334"),
            values.GetValueOrDefault("qdrant-key"),
            values.GetValueOrDefault("embeddings", "http://localhost:8080"),
            values.GetValueOrDefault("reranker"),
            values.GetValueOrDefault("pool"),
            split,
            rerankerModel,
            values.GetValueOrDefault("scope", Path.Combine("tools", "SearchEval", "scope")),
            values.GetValueOrDefault("categories", Path.Combine("seed", "categories.sample.json")),
            writeCalibration,
            values.GetValueOrDefault("calibration", Path.Combine("tools", "SearchEval", "scope", "query-scope.calibration.json")),
            values.GetValueOrDefault("extractor", "rules"),
            values.GetValueOrDefault("chat", "http://localhost:38090"),
            values.GetValueOrDefault("model"),
            values.GetValueOrDefault("scorer"),
            values.GetValueOrDefault("tei", "http://localhost:38091"),
            values.GetValueOrDefault("attributes", Path.Combine("seed", "attributes.sample.json")),
            values.GetValueOrDefault("onnx"),
            values.GetValueOrDefault("knowledge", Path.Combine("tools", "SearchEval", "knowledge")),
            values.GetValueOrDefault("database"),
            values.GetValueOrDefault("script"),
            int.TryParse(values.GetValueOrDefault("context"), out var context) ? context : 8192,
            values.GetValueOrDefault("copilot", Path.Combine("tools", "SearchEval", "Copilot")),
            values.GetValueOrDefault("assistant", Path.Combine("tools", "SearchEval", "Assistant")),
            values.GetValueOrDefault("only"),
            values.GetValueOrDefault("dataset"),
            values.GetValueOrDefault("modality"),
            int.TryParse(values.GetValueOrDefault("take"), out var take) ? take : null,
            values.GetValueOrDefault("format"),
            values.GetValueOrDefault("vocabulary"),
            siglip,
            values.GetValueOrDefault("recordings", Path.Combine("artifacts", "voice-eval")),
            values.GetValueOrDefault("whisper"),
            values.GetValueOrDefault("parakeet"),
            values.GetValueOrDefault("lines", Path.Combine("docs", "analysis", "voice-recordings.md")));
    }
}
