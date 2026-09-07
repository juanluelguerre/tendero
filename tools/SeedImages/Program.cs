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

    case "dry-run":
        return DryRun(Value("--only"), Int32.TryParse(Value("--take"), out var take) ? take : Int32.MaxValue);

    default:
        Console.Error.WriteLine(
            """
            Usage: dotnet run --project tools/SeedImages -- <command> [options]

              inspect --models <dir> [--ep dml|cpu]   print what the ONNX graphs declare
              dry-run [--only <item_id>] [--take <n>] print the prompts and the plan
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
static int DryRun(string? only, int take)
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

    var missing = products.Count(product => !File.Exists(Path.Combine(images, $"{product.ItemId}.webp")));
    Console.WriteLine($"{missing} of {products.Count} products have no picture. Showing {wanted.Length}.");
    Console.WriteLine();

    foreach (var product in wanted)
    {
        var prompt = PromptTemplate.Positive(product, colours);

        Console.WriteLine($"=== {product.ItemId} -> seed/images/{product.ItemId}.webp");
        Console.WriteLine($"    {product.EnglishName}");
        Console.WriteLine($"    colour: {product.SpanishColour ?? "(none declared)"}");
        Console.WriteLine();
        Console.WriteLine(prompt);
        Console.WriteLine();
    }

    Console.WriteLine("negative:");
    Console.WriteLine(PromptTemplate.Negative);

    return 0;
}
