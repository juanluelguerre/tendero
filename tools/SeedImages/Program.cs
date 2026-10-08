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

// Which model looks: Ollama, where the checks started, or the shop's own model on
// llama.cpp's server. `--ollama` is the first one's old way of naming its url.
IVisionModel Vision() => (Value("--provider") ?? "ollama") switch
{
    "llama" => new LlamaVision(Value("--url") ?? "http://localhost:39481", Value("--model") ?? "Qwen/Qwen3.5-4B"),
    _ => new OllamaVision(Value("--url") ?? Value("--ollama") ?? "http://localhost:11434", Value("--model") ?? "qwen3.5:9b"),
};

switch (command)
{
    case "inspect":
        return Inspect(Value("--models"), Value("--ep") ?? "dml");

    case "generate":
        return Generate(
            Value("--models"),
            Value("--only"),
            Int32.TryParse(Value("--take"), out var many) ? many : 1,
            Int32.TryParse(Value("--steps"), out var steps) ? steps : 40,
            Single.TryParse(Value("--guidance"), out var guidance) ? guidance : 7f,
            Value("--out"),
            Value("--ep") ?? "dml",
            args.Contains("--force"),
            UInt64.TryParse(Value("--rng-seed"), out var rng) ? rng : null,
            Int32.TryParse(Value("--size"), out var size) ? size : 1024,
            // gemini by default, because it is what drew the catalogue. The
            // local one is the offline path and asks to be named.
            Value("--provider") ?? "gemini",
            Value("--model"),
            Value("--hint"));

    case "recolour":
        return Recolour(Value("--only"), Value("--colour"), Value("--as"), Value("--out"), Value("--model"), Value("--hint"));

    case "verify":
        return Verify(
            Value("--in"),
            Vision(),
            Value("--only"),
            Value("--reference"),
            args.Contains("--ci"),
            args.Contains("--describe"));

    case "pairs":
        return Pairs(Value("--in"), Vision(), int.TryParse(Value("--offset"), out var offset) ? offset : 1);

    case "defects":
        return Defects(
            Value("--in"),
            Vision(),
            Value("--only"),
            Int32.TryParse(Value("--take"), out var pictures) ? pictures : Int32.MaxValue,
            Value("--reference"),
            Value("--dump"),
            args.Contains("--draw-only"),
            args.Contains("--record"),
            args.Contains("--ci"));

    case "reject":
        return Reject(Value("--in"), Value("--only"), Value("--defect"), Value("--note"));

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
              generate [--provider gemini|sdxl] [--only <id> [--hint "<what the last drawing got wrong>"]] [--take <n>]
                       [--out <dir>] [--force] [--size <px>] [--model <hosted id>]
                       [--models <dir>] [--steps <n>] [--guidance <f>]
                       [--ep dml|cpu] [--rng-seed <n>]
                                                      draw the missing illustrations.
                                                      gemini by default and needs
                                                      GEMINI_API_KEY; sdxl needs --models
              recolour --only <id> --colour <a colour of the catalogue> [--as <file id>] [--hint "<what to mind>"]
                       [--out <dir>] [--model <hosted id>]
                                                      the same picture in another colour: the model is
                                                      shown the one that exists and changes only the
                                                      colour, and the background is restored from the
                                                      original. Writes <id>-<COLOUR>.webp unless --as
                                                      names it. Needs GEMINI_API_KEY. A variant's
                                                      picture named in the seed is made this way by
                                                      generate, with no need to ask
              verify [--in <dir>] [--only <id,id>] [--provider ollama|llama] [--url <url>]
                     [--model <name>] [--reference <id>] [--ci]
                                                      ask a vision model what it sees: text, a mark
                                                      on the product, colour, a scene, how many,
                                                      whether it is the product described, and
                                                      whether it is drawn as the reference picture is
              pairs [--in <dir>] [--offset <n>] [--provider ollama|llama] [--url <url>] [--model <name>]
                                                      measure the description check: every picture
                                                      against its own product and against another's
              defects [--in <dir>] [--only <id,id>] [--take <n>] [--dump <dir> [--draw-only]] [--record] [--ci]
                      [--provider ollama|llama] [--url <url>] [--model <name>] [--reference <id>]
                                                      measure every question: each picture as it is,
                                                      and with one defect of each kind drawn into it,
                                                      beside the pictures a person rejected. --record
                                                      writes the board; --ci fails below the recorded one
              reject --only <id> --defect <kind> --note "<what is wrong>" [--in <dir>]
                                                      keep a picture a person rejected, with its defect,
                                                      before it is drawn over
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
    string? outDirectory, string provider, bool force, ulong? rngSeed, int size,
    string illustrator, string? hostedModel, string? hint)
{
    // Only the local generator needs a model folder. The hosted one needs a key,
    // and it says so itself when it does not find one.
    // A hint says what one drawing got wrong: on a batch it would be said of every product.
    if (hint is not null && only is null)
    {
        Console.Error.WriteLine("--hint is for one product's redraw: it needs --only <id>.");
        return 2;
    }

    if (illustrator == "sdxl" && (models is null || !Directory.Exists(models)))
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

    // A variant's picture is its product's cover in another colour, when the
    // cover is there and the generator can do that: the same object, not a
    // second drawing from the same words.
    byte[]? CoverOf(SeedProduct product) =>
        product.VariantOf is { } cover && File.Exists(Path.Combine(destination, $"{cover}.webp"))
            ? VisionChecks.AsPng(File.ReadAllBytes(Path.Combine(destination, $"{cover}.webp")))
            : null;

    var jobs = wanted
        .Where(product => illustrator != "gemini" || CoverOf(product) is null)
        .Select(product => (product.ItemId, Prompt: PromptTemplate.Positive(product, colours, hint)))
        .ToArray();

    using IIllustrationGenerator generator = illustrator switch
    {
        "gemini" => new GeminiIllustrator(hostedModel ?? "gemini-2.5-flash-image", size),
        "sdxl" => new SdxlPipeline(models!, provider, steps, guidance, rngSeed, size),
        _ => throw new InvalidOperationException($"No illustrator called '{illustrator}'. Try sdxl or gemini.")
    };

    Console.WriteLine($"drawing with {generator.Name}"
        + (generator.IsDeterministic ? " · redrawing a product reproduces it" : " · no seed, so redrawing is a new roll"));

    IReadOnlyList<GeneratedImage> images;

    try
    {
        List<GeneratedImage> made = [.. jobs.Length == 0 ? [] : generator.Draw(jobs, PromptTemplate.Negative, Console.WriteLine)];

        if (generator is IRecolourer recolourer)
        {
            foreach (var variant in wanted.Where(product => CoverOf(product) is not null))
            {
                var cover = products.First(product => product.ItemId == variant.VariantOf);
                made.Add(recolourer.Recolour(
                    variant.ItemId,
                    CoverOf(variant)!,
                    PromptTemplate.Recolour(
                        cover.SpanishColour is { } was ? colours.English(was) : null,
                        variant.SpanishColour is { } becomes ? colours.English(becomes) ?? becomes : "another colour",
                        hint),
                    Console.WriteLine));
            }
        }

        images = made;
    }
    catch (InvalidOperationException refusal)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(refusal.Message);
        return 2;
    }

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

// The same picture in another colour (phase 13a, I-4d). A product sold in two
// colours is one object photographed twice, and two drawings from the same
// words are two different objects -- so the second colour is made from the
// first picture, by a model that is shown it and told to change only that.
static int Recolour(string? only, string? colour, string? asId, string? outDirectory, string? hostedModel, string? hint)
{
    if (only is null || colour is null)
    {
        Console.Error.WriteLine("recolour needs --only <id> (the picture to start from) and --colour <a colour of the catalogue>.");
        return 2;
    }

    var root = RepositoryRoot.Find();
    var colours = ColourLexicon.Read(Path.Combine(root, "seed", "attributes.sample.json"));
    // Read from the catalogue's pictures; written there too unless --out says elsewhere.
    var destination = outDirectory ?? Path.Combine(root, "seed", "images");
    var source = Path.Combine(root, "seed", "images", $"{only}.webp");
    Directory.CreateDirectory(destination);

    // The colour as the catalogue says it, in either of its languages.
    var to = colours.English(colour)
        ?? colours.EnglishLabels.FirstOrDefault(label => String.Equals(label, colour, StringComparison.OrdinalIgnoreCase));

    if (to is null)
    {
        Console.Error.WriteLine($"'{colour}' is not a colour of the catalogue. It has: {String.Join(", ", colours.EnglishLabels)}.");
        return 2;
    }

    if (!File.Exists(source))
    {
        Console.Error.WriteLine($"No picture at {source}: there is nothing to recolour.");
        return 2;
    }

    var product = ProductCatalogue.Read(Path.Combine(root, "seed", "products.sample.json"))
        .FirstOrDefault(known => String.Equals(known.ItemId, only, StringComparison.OrdinalIgnoreCase));
    var from = product?.SpanishColour is { } was ? colours.English(was) : null;
    var id = asId ?? $"{only}-{to.ToUpperInvariant().Replace(' ', '_')}";

    using var generator = new GeminiIllustrator(hostedModel ?? "gemini-2.5-flash-image", 1024);
    Console.WriteLine($"recolouring with {generator.Name} · {from ?? "its colour"} -> {to}");

    GeneratedImage image;
    try
    {
        image = generator.Recolour(id, VisionChecks.AsPng(File.ReadAllBytes(source)), PromptTemplate.Recolour(from, to, hint), Console.WriteLine);
    }
    catch (InvalidOperationException refusal)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(refusal.Message);
        return 2;
    }

    var path = Path.Combine(destination, $"{image.ItemId}.webp");
    File.WriteAllBytes(path, image.Webp);

    Console.WriteLine();
    Console.WriteLine($"{image.ItemId}  {image.Webp.Length / 1024} KB  {image.Took.TotalSeconds:F1}s  bg {image.Background}  -> {path}");
    Console.WriteLine($"    {image.SafeArea.Detail}{(image.SafeArea.Passed ? "" : "  OUTSIDE THE SAFE AREA")}");

    return image.SafeArea.Passed ? 0 : 1;
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

// The half of the job a vision model is actually for. It looks at finished
// pictures and answers closed questions about them: is there text, what colour is
// the object, is anything drawn besides the product, is it the product its data
// describes, and is it drawn as the reference pictures are.
//
// The scene question is why this exists. A tiled sheet of products fills the frame
// and the pixel probe catches it; a lamp standing on a desk leaves a perfectly good
// margin and no amount of counting will say so. The last two arrived with phase
// 13a: a picture is accepted only when it is right for its product's description
// and in the register of the ones that exist.
//
// Every line it prints is a proposal: a person accepts the batch.
static int Verify(string? input, IVisionModel model, string? only, string? reference, bool ci, bool describe)
{
    var root = RepositoryRoot.Find();
    input ??= Path.Combine(root, "seed", "images");

    var wanted = only?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var files = Directory.Exists(input)
        ? Directory.EnumerateFiles(input, "*.webp").Order()
            .Where(file => wanted is null || wanted.Contains(Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase))
            .ToArray()
        : File.Exists(input) ? [input] : [];

    if (files.Length == 0)
    {
        Console.Error.WriteLine($"Nothing to verify at '{input}'.");
        return 2;
    }

    var products = ProductCatalogue.Read(Path.Combine(root, "seed", "products.sample.json"))
        .ToDictionary(product => product.ItemId, StringComparer.OrdinalIgnoreCase);

    var colours = ColourLexicon.Read(Path.Combine(root, "seed", "attributes.sample.json"));

    try
    {
        model.EnsureReachable();
    }
    catch (InvalidOperationException failure)
    {
        Console.Error.WriteLine(failure.Message);
        return 2;
    }

    var vision = new VisionChecks(model);
    var examiner = new PictureExaminer(vision, colours);

    // One of the pictures drawn by hand, which set the register every other one
    // is held to. It is never compared with itself.
    reference ??= "B10SHO0101";
    var referenceFile = Path.Combine(root, "seed", "images", reference + ".webp");
    var referenceBytes = File.Exists(referenceFile) ? File.ReadAllBytes(referenceFile) : null;

    Console.WriteLine($"asking {model.Name} about {files.Length} image(s)...");
    Console.WriteLine();

    var flagged = 0;

    foreach (var file in files)
    {
        var itemId = Path.GetFileNameWithoutExtension(file);
        var bytes = File.ReadAllBytes(file);

        if (describe)
        {
            Console.WriteLine($"--- {itemId}");
            Console.WriteLine(vision.Describe(bytes, Path.ChangeExtension(file, ".sent.png")));
            Console.WriteLine();
            continue;
        }

        var itself = string.Equals(itemId, reference, StringComparison.OrdinalIgnoreCase);
        var examination = examiner.Examine(bytes, products.GetValueOrDefault(itemId), itself ? null : referenceBytes, reference);

        // A colour mismatch is reported and NOT treated the same way. The
        // catalogue's colour for an invented product is arbitrary and the drawing
        // is the expensive part, so seed/IMAGES-TODO.md says the cheap repair is
        // to change the catalogue -- which is a person's call, not a retry.
        foreach (var colour in examination.Doubts.Where(doubt => doubt.Question == Question.Colour))
            Console.WriteLine($"note  {itemId,-14}  {colour.Says}");

        var faults = examination.Doubts.Where(doubt => doubt.Question != Question.Colour).Select(doubt => doubt.Says).ToArray();

        if (faults.Length == 0)
        {
            Console.WriteLine($"ok    {itemId,-14}  {examination.Colour}");
            continue;
        }

        flagged++;
        Console.WriteLine($"LOOK  {itemId,-14}  {string.Join("; ", faults)}");
    }

    Console.WriteLine();
    Console.WriteLine($"{files.Length - flagged} of {files.Length} pass every check; the rest are for a person to look at.");

    return flagged == 0 || !ci ? 0 : 1;
}

// How often the description check is wrong, measured before it is trusted with a
// batch (the phase 13a spec, I-1). Every picture is shown twice: with its own
// product's name and description, where "it does not match" is a false alarm, and
// with another product's, where "it matches" is a miss. The other product is the
// next one in the folder, so the pairing is the same on every run -- and the hard
// one, because neighbours in the folder are neighbours in the catalogue: a shoe
// beside a shoe. `--offset` pairs each with one further away.
static int Pairs(string? input, IVisionModel model, int offset)
{
    var root = RepositoryRoot.Find();
    input ??= Path.Combine(root, "seed", "images");

    var products = ProductCatalogue.Read(Path.Combine(root, "seed", "products.sample.json"))
        .ToDictionary(product => product.ItemId, StringComparer.OrdinalIgnoreCase);
    var files = Directory.EnumerateFiles(input, "*.webp").Order()
        .Where(file => products.ContainsKey(Path.GetFileNameWithoutExtension(file)))
        .ToArray();

    if (files.Length < 2)
    {
        Console.Error.WriteLine($"Pairs needs at least two pictures of known products at '{input}'.");
        return 2;
    }

    try
    {
        model.EnsureReachable();
    }
    catch (InvalidOperationException failure)
    {
        Console.Error.WriteLine(failure.Message);
        return 2;
    }

    var vision = new VisionChecks(model);
    var (falseAlarms, misses) = (0, 0);

    for (var index = 0; index < files.Length; index++)
    {
        var bytes = File.ReadAllBytes(files[index]);
        var own = products[Path.GetFileNameWithoutExtension(files[index])];
        var other = products[Path.GetFileNameWithoutExtension(files[(index + offset) % files.Length])];

        var right = vision.Against(bytes, own.EnglishName, own.EnglishDescription);
        var wrong = vision.Against(bytes, other.EnglishName, other.EnglishDescription);

        if (!right.Matches)
        {
            falseAlarms++;
            Console.WriteLine($"FALSE ALARM  {own.ItemId,-12} is \"{own.EnglishName}\" and was refused: {right.Differs}");
        }

        if (wrong.Matches)
        {
            misses++;
            Console.WriteLine($"MISS         {own.ItemId,-12} shows \"{wrong.Shows}\" and passed as \"{other.EnglishName}\"");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"{model.Name}: {files.Length} pictures, each against its own product and another's.");
    Console.WriteLine($"  false alarms (its own product refused): {falseAlarms} of {files.Length}");
    Console.WriteLine($"  misses (another product accepted):      {misses} of {files.Length}");

    return 0;
}

// What each question catches, measured on pictures known to be wrong (the phase
// 13a spec, I-2b). `pairs` shows a picture with another product's data: a wrong
// thing. This shows the right thing drawn wrong -- a defect of each kind drawn
// into every picture there is, and the pictures a person really rejected -- and
// counts, per kind, which question doubted it. A doubt the picture already raised
// before the defect went in is not the defect being caught, so it is not counted.
//
// The board it prints is recorded with --record and held with --ci: a question
// that catches less than it did is a change to explain.
static int Defects(string? input, IVisionModel model, string? only, int take, string? reference, string? dump, bool drawOnly, bool record, bool ci)
{
    var root = RepositoryRoot.Find();
    input ??= Path.Combine(root, "seed", "images");

    var products = ProductCatalogue.Read(Path.Combine(root, "seed", "products.sample.json"))
        .ToDictionary(product => product.ItemId, StringComparer.OrdinalIgnoreCase);
    var wanted = only?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var files = Directory.EnumerateFiles(input, "*.webp").Order()
        .Where(file => products.ContainsKey(Path.GetFileNameWithoutExtension(file)))
        .Where(file => wanted is null || wanted.Contains(Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase))
        .Take(take)
        .ToArray();

    if (files.Length == 0)
    {
        Console.Error.WriteLine($"No picture of a known product at '{input}'.");
        return 2;
    }

    // The set, for a person to look at before any model does: a defect nobody
    // can see in the picture measures the forge and not the question.
    if (drawOnly)
    {
        if (dump is null)
        {
            Console.Error.WriteLine("--draw-only writes the pictures and asks nothing: it needs --dump <dir>.");
            return 2;
        }

        Directory.CreateDirectory(dump);
        var drawn = 0;

        foreach (var file in files)
        {
            var itemId = Path.GetFileNameWithoutExtension(file);
            foreach (var defect in Enum.GetValues<Defect>())
            {
                if (DefectForge.Draw(defect, File.ReadAllBytes(file)) is not { } bad)
                    continue;

                File.WriteAllBytes(Path.Combine(dump, $"{itemId}-{defect.ToString().ToLowerInvariant()}.png"), bad);
                drawn++;
            }
        }

        Console.WriteLine($"{drawn} picture(s) with a defect drawn in, from {files.Length}, in {dump}.");
        return 0;
    }

    try
    {
        model.EnsureReachable();
    }
    catch (InvalidOperationException failure)
    {
        Console.Error.WriteLine(failure.Message);
        return 2;
    }

    var examiner = new PictureExaminer(
        new VisionChecks(model),
        ColourLexicon.Read(Path.Combine(root, "seed", "attributes.sample.json")));

    reference ??= "B10SHO0101";
    var referenceFile = Path.Combine(root, "seed", "images", reference + ".webp");
    var referenceBytes = File.Exists(referenceFile) ? File.ReadAllBytes(referenceFile) : null;

    if (dump is not null)
        Directory.CreateDirectory(dump);

    HashSet<Question> Asked(byte[] picture, SeedProduct product) =>
    [
        .. examiner
            .Examine(picture, product, string.Equals(product.ItemId, reference, StringComparison.OrdinalIgnoreCase) ? null : referenceBytes, reference)
            .Doubts.Select(doubt => doubt.Question),
    ];

    List<Trial> trials = [];
    Dictionary<string, HashSet<Question>> asIs = new(StringComparer.OrdinalIgnoreCase);

    Console.WriteLine($"asking {model.Name} about {files.Length} picture(s), as they are and with each defect drawn in...");
    Console.WriteLine();

    foreach (var file in files)
    {
        var product = products[Path.GetFileNameWithoutExtension(file)];
        var good = File.ReadAllBytes(file);
        var before = asIs[product.ItemId] = Asked(good, product);

        trials.Add(new Trial(product.ItemId, null, before));
        Console.WriteLine($"{product.ItemId,-12} as it is     {Named(before)}");

        foreach (var defect in Enum.GetValues<Defect>())
        {
            // A count is held to the pieces a product declares, and a colour to the
            // one it declares: with nothing declared there is nothing the defect
            // could be caught against, and the miss would be the set's.
            if ((defect == Defect.Count && product.Pieces is null) || (defect == Defect.Colour && product.SpanishColour is null))
                continue;

            if (DefectForge.Draw(defect, good) is not { } bad)
                continue;

            if (dump is not null)
                File.WriteAllBytes(Path.Combine(dump, $"{product.ItemId}-{defect.ToString().ToLowerInvariant()}.png"), bad);

            var raised = Asked(bad, product);
            raised.ExceptWith(before);
            trials.Add(new Trial($"{product.ItemId}-{defect}", defect, raised));
            Console.WriteLine($"{product.ItemId,-12} {defect,-12}  {(raised.Count == 0 ? "MISSED" : Named(raised))}");
        }
    }

    var kept = new KnownBad(Path.Combine(root, "tools", "SeedImages", "known-bad"));

    foreach (var rejected in kept.Read())
    {
        if (!products.TryGetValue(rejected.ItemId, out var product)
            || (wanted is not null && !wanted.Contains(rejected.ItemId, StringComparer.OrdinalIgnoreCase)))
        {
            continue;
        }

        // Held against the picture that was accepted for the same product, when
        // this run looked at it: what that one raises too is not this defect.
        var raised = Asked(File.ReadAllBytes(Path.Combine(kept.Folder, rejected.File)), product);
        raised.ExceptWith(asIs.GetValueOrDefault(rejected.ItemId) ?? []);
        trials.Add(new Trial(rejected.File, rejected.Defect, raised));
        Console.WriteLine($"{rejected.File,-28} rejected: {rejected.Defect,-10}  {(raised.Count == 0 ? "MISSED" : Named(raised))}");
    }

    var board = Scoreboard.Of(trials);
    var questions = Enum.GetValues<Question>();

    Console.WriteLine();
    Console.WriteLine($"{model.Name}. Caught, per kind of defect and per question:");
    Console.WriteLine();
    Console.WriteLine($"  {"",-12}{"pictures",9}{string.Concat(questions.Select(question => $"{question,13}"))}{"by none",10}");

    foreach (var (defect, score) in board.Kinds)
    {
        Console.WriteLine(
            $"  {defect,-12}{score.Pictures,9}{string.Concat(questions.Select(question => $"{score.Caught.GetValueOrDefault(question),13}"))}{score.ByNone,10}");
    }

    Console.WriteLine();
    Console.WriteLine($"  {"as they are",-12}{board.Good,9}{string.Concat(questions.Select(question => $"{board.FalseAlarms.GetValueOrDefault(question),13}"))}   (doubts about a right picture)");

    var recordedFile = Path.Combine(kept.Folder, "baseline.json");
    // Held to the recorded board only when the whole set was asked: two pictures
    // of twenty-eight have shares of their own, and every one of them reads as a fall.
    var whole = wanted is null && take == Int32.MaxValue;
    var falls = whole && File.Exists(recordedFile) ? board.FallsBelow(Scoreboard.Read(File.ReadAllText(recordedFile))) : [];

    if (!whole && (record || ci))
    {
        Console.Error.WriteLine("--record and --ci are for the whole set: leave out --only and --take.");
        return 2;
    }

    if (falls.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Below the recorded board:");
        foreach (var fall in falls)
            Console.WriteLine($"  {fall}");
    }

    if (record)
    {
        Directory.CreateDirectory(kept.Folder);
        File.WriteAllText(recordedFile, board.Write() + "\n");
        Console.WriteLine();
        Console.WriteLine($"Recorded in {Path.GetRelativePath(root, recordedFile)}.");
    }

    return falls.Count == 0 || !ci ? 0 : 1;

    static string Named(IReadOnlySet<Question> raised) =>
        raised.Count == 0 ? "-" : string.Join(", ", raised.Order());
}

// A picture a person rejected is kept before it is drawn over (the phase 13a
// spec, I-2b): it is what the questions are measured against next time.
static int Reject(string? input, string? only, string? defect, string? note)
{
    var root = RepositoryRoot.Find();
    input ??= Path.Combine(root, "seed", "images");

    if (only is null || note is null || !Enum.TryParse<Defect>(defect, ignoreCase: true, out var kind))
    {
        Console.Error.WriteLine(
            $"reject needs --only <id>, --note \"<what is wrong>\" and --defect <{string.Join('|', Enum.GetNames<Defect>()).ToLowerInvariant()}>.");
        return 2;
    }

    var picture = File.Exists(input) ? input : Path.Combine(input, only + ".webp");
    if (!File.Exists(picture))
    {
        Console.Error.WriteLine($"No picture at '{picture}'.");
        return 2;
    }

    var kept = new KnownBad(Path.Combine(root, "tools", "SeedImages", "known-bad")).Keep(picture, only, kind, note);
    Console.WriteLine($"Kept as known-bad/{kept.File}: {kept.Defect}, {kept.Note}");

    return 0;
}
