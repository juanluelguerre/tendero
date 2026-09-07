using ElGuerre.Tendero.SeedImages;
using Microsoft.ML.OnnxRuntime;

// Two commands so far, and neither of them draws anything yet.
//
// `inspect` opens the ONNX graphs and prints what they really declare, because
// an SDXL export is five graphs whose tensor names differ between exporters and
// a pipeline written against the wrong ones fails as noise rather than as an
// error. `dry-run` prints the prompts, so all ninety-two can be audited in two
// seconds instead of after three hours of GPU.
var command = args.Length > 0 ? args[0] : String.Empty;

string? Value(string flag)
{
    var at = Array.IndexOf(args, flag);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

switch (command)
{
    case "inspect":
        return Inspect(Value("--models"), Value("--ep") ?? "dml");

    case "generate":
        return Generate(
            Value("--models"),
            Value("--only"),
            Int32.TryParse(Value("--take"), out var many) ? many : 1,
            Int32.TryParse(Value("--steps"), out var steps) ? steps : 30,
            Single.TryParse(Value("--guidance"), out var guidance) ? guidance : 7f,
            Value("--out"),
            Value("--ep") ?? "dml",
            args.Contains("--force"),
            UInt64.TryParse(Value("--rng-seed"), out var rng) ? rng : null);

    case "verify":
        return Verify(
            Value("--in"),
            Value("--ollama") ?? "http://localhost:11434",
            Value("--model") ?? "qwen3.5:9b",
            args.Contains("--ci"),
            args.Contains("--describe"));

    case "probe":
        return Probe(Value("--in"));

    case "tokens":
        return Tokens(Value("--models"));

    case "dry-run":
        return DryRun(
            Value("--only"),
            Int32.TryParse(Value("--take"), out var take) ? take : Int32.MaxValue,
            Value("--models"));

    default:
        Console.Error.WriteLine(
            """
            Usage: dotnet run --project tools/SeedImages -- <command> [options]

              inspect --models <dir> [--ep dml|cpu]   print what the ONNX graphs declare
              generate --models <dir> [--only <id>] [--take <n>] [--steps <n>]
                       [--guidance <f>] [--out <dir>] [--ep dml|cpu] [--force]
                       [--rng-seed <n>]
                                                      draw the missing illustrations
              verify --in <dir> [--ollama <url>] [--model <name>] [--ci]
                                                      ask a vision model what it sees
              probe --in <dir|file>                   where the object sits in images that exist
              tokens --models <dir>                   what each line of the prompt costs
              dry-run [--only <id>] [--take <n>] [--models <dir>]
                                                      print the prompts, and their
                                                      token cost when --models is given
            """);
        return 2;
}

// The graphs, one component at a time -- the UNet's weights are five gigabytes
// -- on the provider you name. That default is not cosmetic: an export optimised
// with Microsoft contrib operators cannot be opened on the plain CPU provider at
// all, so this command doubles as the proof that a provider binds.
static int Inspect(string? models, string provider)
{
    if (models is null || !Directory.Exists(models))
    {
        Console.Error.WriteLine($"No model directory at '{models}'. Pass --models <dir>.");
        return 2;
    }

    Console.WriteLine($"providers: {String.Join(", ", OrtEnv.Instance().GetAvailableProviders())}");
    Console.WriteLine();

    string[] components = ["text_encoder", "text_encoder_2", "unet", "vae_decoder"];

    foreach (var component in components)
    {
        var path = Path.Combine(models, component, "model.onnx");

        // A missing component is reported and stepped over rather than fatal.
        // This command is a diagnostic, and the moment it is most useful is on a
        // half-finished download -- where refusing to say anything about the
        // three files that ARE there would be the least helpful thing it could
        // do. `generate` is where a missing file is an error.
        if (!File.Exists(path))
        {
            Console.WriteLine($"--- {component}: not here yet ({path})");
            Console.WriteLine();
            continue;
        }

        Console.WriteLine($"--- {component} ---");

        using var options = new SessionOptions();

        if (provider == "dml")
            options.AppendExecutionProvider_DML(0);
        else
            options.AppendExecutionProvider_CPU();

        var opened = System.Diagnostics.Stopwatch.StartNew();
        using var session = new InferenceSession(path, options);

        Console.WriteLine($"  (opened on {provider} in {opened.Elapsed.TotalSeconds:F1}s)");

        foreach (var (name, meta) in session.InputMetadata)
            Console.WriteLine($"  in   {name,-24} {meta.ElementDataType,-10} [{String.Join(",", meta.Dimensions)}]");

        foreach (var (name, meta) in session.OutputMetadata)
            Console.WriteLine($"  out  {name,-24} {meta.ElementDataType,-10} [{String.Join(",", meta.Dimensions)}]");

        Console.WriteLine();
    }

    return 0;
}

// Everything the generator would do except the three hours of arithmetic. It
// loads no model, so it answers instantly and it is the cheapest possible way to
// read ninety-two prompts before spending a GPU on them.
static int DryRun(string? only, int take, string? models)
{
    var root = RepositoryRoot.Find();
    var products = ProductCatalogue.Read(Path.Combine(root, "seed", "products.sample.json"));
    var colours = ColourLexicon.Read(Path.Combine(root, "seed", "attributes.sample.json"));
    var images = Path.Combine(root, "seed", "images");

    var wanted = products
        .Where(product => only is null || String.Equals(product.ItemId, only, StringComparison.OrdinalIgnoreCase))
        .Where(product => only is not null || !File.Exists(Path.Combine(images, $"{product.ItemId}.webp")))
        .Take(take)
        .ToArray();

    if (wanted.Length == 0)
    {
        Console.WriteLine(only is null
            ? "Every product already has a picture."
            : $"No product '{only}' in the catalogue.");
        return 0;
    }

    // Optional, because the point of dry-run is that it answers with no model
    // loaded. The tokenizer is two text files, though -- no GPU, no weights --
    // so when the export is at hand the cost of each prompt comes for free, and
    // that number is the one that decides whether the prompt survives at all.
    var tokenizer = models is not null
        ? ClipTokenizer.Load(Path.Combine(models, "tokenizer"))
        : null;

    var missing = products.Count(product => !File.Exists(Path.Combine(images, $"{product.ItemId}.webp")));
    Console.WriteLine($"{missing} of {products.Count} products have no picture. Showing {wanted.Length}.");
    Console.WriteLine();

    foreach (var product in wanted)
    {
        var prompt = PromptTemplate.Positive(product, colours);

        Console.WriteLine($"=== {product.ItemId} -> seed/images/{product.ItemId}.webp");
        Console.WriteLine($"    {product.EnglishName}");
        Console.WriteLine($"    colour: {product.SpanishColour ?? "(none declared)"}");

        if (tokenizer is not null)
        {
            var cost = tokenizer.CountContentTokens(prompt);
            var room = ClipTokenizer.ContextLength - 2;

            Console.WriteLine($"    tokens: {cost} of {room}{(cost > room ? $"  TRUNCATED, {cost - room} lost" : string.Empty)}");
        }
        Console.WriteLine();
        Console.WriteLine(prompt);
        Console.WriteLine();
    }

    Console.WriteLine("negative:");
    Console.WriteLine(PromptTemplate.Negative);

    return 0;
}

// Where the 77 tokens go, line by line. Tuning a prompt down to a budget by
// deleting whatever looks long is guesswork; this says which line is expensive
// and why, and it needs no GPU -- a tokenizer is two text files.
static int Tokens(string? models)
{
    if (models is null || !Directory.Exists(models))
    {
        Console.Error.WriteLine($"No model directory at '{models}'. Pass --models <dir>.");
        return 2;
    }

    var tokenizer = ClipTokenizer.Load(Path.Combine(models, "tokenizer"));
    var room = ClipTokenizer.ContextLength - 2;

    Console.WriteLine($"budget: {room} content tokens (CLIP takes {ClipTokenizer.ContextLength}, two go to the markers)");
    Console.WriteLine();

    var style = 0;

    foreach (var line in PromptTemplate.StyleLines)
    {
        var cost = tokenizer.CountContentTokens(line);
        style += cost;
        Console.WriteLine($"  {cost,4}  {line}");
    }

    Console.WriteLine();
    Console.WriteLine($"  {style,4}  ALL STYLE LINES");
    Console.WriteLine($"  {tokenizer.CountContentTokens(PromptTemplate.Negative),4}  the negative prompt (its own window, not this budget)");

    // The style block is fixed; the subject is not, and the worst case is what
    // the budget has to survive. A template that fits the average product is a
    // template that quietly truncates the long ones.
    var root = RepositoryRoot.Find();
    var products = ProductCatalogue.Read(Path.Combine(root, "seed", "products.sample.json"));
    var colours = ColourLexicon.Read(Path.Combine(root, "seed", "attributes.sample.json"));

    var costs = products
        .Select(product => tokenizer.CountContentTokens(PromptTemplate.Positive(product, colours)))
        .Order()
        .ToArray();

    var over = costs.Count(cost => cost > room);

    Console.WriteLine();
    Console.WriteLine($"across all {costs.Length} products: shortest {costs[0]}, median {costs[costs.Length / 2]}, longest {costs[^1]}");
    Console.WriteLine(over == 0
        ? $"  {costs.Length} of {costs.Length} fit in one {room}-token window."
        : $"  {over} of {costs.Length} spill into a second window, which is encoded rather than discarded.");

    return 0;
}

// The one that draws. Everything above exists so that by the time this runs, the
// only thing left to be wrong is the picture itself.
static int Generate(
    string? models, string? only, int take, int steps, float guidance,
    string? outDirectory, string provider, bool force, ulong? rngSeed)
{
    if (models is null || !Directory.Exists(models))
    {
        Console.Error.WriteLine($"No model directory at '{models}'. Pass --models <dir>.");
        return 2;
    }

    var root = RepositoryRoot.Find();
    var products = ProductCatalogue.Read(Path.Combine(root, "seed", "products.sample.json"));
    var colours = ColourLexicon.Read(Path.Combine(root, "seed", "attributes.sample.json"));

    var destination = outDirectory ?? Path.Combine(root, "seed", "images");
    Directory.CreateDirectory(destination);

    var wanted = products
        .Where(product => only is null || String.Equals(product.ItemId, only, StringComparison.OrdinalIgnoreCase))
        .Where(product => force || only is not null
            || !File.Exists(Path.Combine(destination, $"{product.ItemId}.webp")))
        .Take(take)
        .ToArray();

    if (wanted.Length == 0)
    {
        Console.WriteLine("Nothing to draw.");
        return 0;
    }

    var jobs = wanted
        .Select(product => (product.ItemId, Prompt: PromptTemplate.Positive(product, colours)))
        .ToArray();

    var pipeline = new SdxlPipeline(models, provider, steps, guidance, rngSeed);
    var images = pipeline.Generate(jobs, PromptTemplate.Negative, Console.WriteLine);

    var failures = 0;

    Console.WriteLine();

    foreach (var image in images)
    {
        var path = Path.Combine(destination, $"{image.ItemId}.webp");
        File.WriteAllBytes(path, image.Webp);

        var kilobytes = image.Webp.Length / 1024;
        var quality = image.Quality == ImageSpec.Quality ? "" : $" at quality {image.Quality}";

        Console.WriteLine($"{image.ItemId}  {kilobytes} KB{quality}  {image.Took.TotalSeconds:F1}s  bg {image.Background}  -> {path}");
        Console.WriteLine($"    {image.SafeArea.Detail}{(image.SafeArea.Passed ? "" : "  OUTSIDE THE SAFE AREA")}");

        if (!image.SafeArea.Passed)
            failures++;
    }

    return failures == 0 ? 0 : 1;
}

// Where the object sits in images that already exist. It decodes rather than
// generates, so it costs milliseconds -- which makes it the way to check a whole
// folder, including the eight illustrations that were made by hand and have
// never been measured against the rule they were written for.
static int Probe(string? input)
{
    input ??= Path.Combine(RepositoryRoot.Find(), "seed", "images");

    var files = Directory.Exists(input)
        ? Directory.EnumerateFiles(input, "*.webp").Order().ToArray()
        : File.Exists(input) ? [input] : [];

    if (files.Length == 0)
    {
        Console.Error.WriteLine($"Nothing to probe at '{input}'.");
        return 2;
    }

    var failures = 0;

    foreach (var file in files)
    {
        using var bitmap = SkiaSharp.SKBitmap.Decode(file);

        if (bitmap is null)
        {
            Console.WriteLine($"{Path.GetFileName(file),-24}  not an image this can read");
            failures++;
            continue;
        }

        var size = Math.Min(bitmap.Width, bitmap.Height);
        var rgb = new byte[size * size * 3];

        for (var row = 0; row < size; row++)
            for (var column = 0; column < size; column++)
            {
                var pixel = bitmap.GetPixel(column, row);
                var at = ((row * size) + column) * 3;

                rgb[at] = pixel.Red;
                rgb[at + 1] = pixel.Green;
                rgb[at + 2] = pixel.Blue;
            }

        var result = SafeAreaProbe.Probe(rgb, size);
        var verdict = result.Passed ? "ok  " : "CROP";

        Console.WriteLine($"{verdict}  {Path.GetFileName(file),-24}  {bitmap.Width}x{bitmap.Height}  {result.Detail}");

        if (!result.Passed)
            failures++;
    }

    Console.WriteLine();
    Console.WriteLine($"{files.Length - failures} of {files.Length} keep the object inside the central {ImageSpec.SafeArea:P0}.");

    return failures == 0 ? 0 : 1;
}

// The half of the job Ollama is actually for. It looks at finished pictures and
// answers closed questions about them: is there text, what colour is the object,
// and is anything drawn besides the product.
//
// That last one is why this exists. A tiled sheet of products fills the frame and
// the pixel probe catches it; a lamp standing on a desk leaves a perfectly good
// margin and no amount of counting will say so.
static int Verify(string? input, string ollama, string model, bool ci, bool describe)
{
    var root = RepositoryRoot.Find();
    input ??= Path.Combine(root, "seed", "images");

    var files = Directory.Exists(input)
        ? Directory.EnumerateFiles(input, "*.webp").Order().ToArray()
        : File.Exists(input) ? [input] : [];

    if (files.Length == 0)
    {
        Console.Error.WriteLine($"Nothing to verify at '{input}'.");
        return 2;
    }

    var products = ProductCatalogue.Read(Path.Combine(root, "seed", "products.sample.json"))
        .ToDictionary(product => product.ItemId, StringComparer.OrdinalIgnoreCase);

    var colours = ColourLexicon.Read(Path.Combine(root, "seed", "attributes.sample.json"));
    var vocabulary = colours.EnglishLabels;

    var vision = new OllamaVisionClient(ollama, model);

    try
    {
        vision.EnsureReachable();
    }
    catch (InvalidOperationException failure)
    {
        Console.Error.WriteLine(failure.Message);
        return 2;
    }

    Console.WriteLine($"asking {model} about {files.Length} image(s)...");
    Console.WriteLine();

    var flagged = 0;

    foreach (var file in files)
    {
        var itemId = Path.GetFileNameWithoutExtension(file);

        if (describe)
        {
            Console.WriteLine($"--- {itemId}");
            Console.WriteLine(vision.Describe(File.ReadAllBytes(file), Path.ChangeExtension(file, ".sent.png")));
            Console.WriteLine();
            continue;
        }

        var verdict = vision.Ask(File.ReadAllBytes(file), vocabulary);

        List<string> faults = [];

        if (verdict.HasScene)
            faults.Add("something else is drawn beside the product");

        if (verdict.HasText)
            faults.Add("there is text in it");

        // A colour mismatch is reported and NOT treated the same way. The
        // catalogue's colour for an invented product is arbitrary and the drawing
        // is the expensive part, so seed/IMAGES-TODO.md says the cheap repair is
        // to change the catalogue -- which is a person's call, not a retry.
        if (products.GetValueOrDefault(itemId)?.SpanishColour is { } spanish
            && colours.English(spanish) is { } declared
            && !string.Equals(declared, verdict.Colour, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(verdict.Colour, "other", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"note  {itemId,-14}  catalogue says {declared}, the picture looks {verdict.Colour}");
        }

        if (faults.Count == 0)
        {
            Console.WriteLine($"ok    {itemId,-14}  {verdict.Colour}");
            continue;
        }

        flagged++;
        Console.WriteLine($"REDO  {itemId,-14}  {string.Join("; ", faults)}");
    }

    Console.WriteLine();
    Console.WriteLine($"{files.Length - flagged} of {files.Length} are usable as they are.");

    return flagged == 0 || !ci ? 0 : 1;
}
