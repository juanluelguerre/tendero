using Microsoft.ML.OnnxRuntime;

// The `inspect` command, and for now the whole tool.
//
// **It exists because every other file in this project would otherwise be built
// on a guess.** An SDXL ONNX export is five graphs whose input and output names
// differ between exporters — `text_embeds` or `add_text_embeds`, a pooled output
// called `pooler_output` or `text_embeds`, hidden states exposed or not — and a
// pipeline written against the wrong ones fails as noise rather than as an
// error. So the first thing the tool can do is read the real graph and print it.
//
// It opens each component one at a time — the UNet's weights are five gigabytes
// — and on the provider you name. That default is not cosmetic: this export is
// optimised with Microsoft contrib operators (`com.microsoft.NhwcConv`), so the
// UNet **cannot** be opened on the plain CPU provider at all. Which makes this
// command double as the proof that DirectML binds.
if (args.Length < 2 || args[0] != "inspect")
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/SeedImages -- inspect --models <dir>");
    return 2;
}

var models = args[Array.IndexOf(args, "--models") + 1];
var provider = Array.IndexOf(args, "--ep") is var flag && flag >= 0 ? args[flag + 1] : "dml";

if (!Directory.Exists(models))
{
    Console.Error.WriteLine($"No model directory at '{models}'.");
    return 2;
}

Console.WriteLine($"providers: {string.Join(", ", OrtEnv.Instance().GetAvailableProviders())}");
Console.WriteLine();

string[] components = ["text_encoder", "text_encoder_2", "unet", "vae_decoder"];

foreach (var component in components)
{
    var path = Path.Combine(models, component, "model.onnx");

    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"{component}: missing {path}");
        return 2;
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
        Console.WriteLine($"  in   {name,-24} {meta.ElementDataType,-10} [{string.Join(",", meta.Dimensions)}]");

    foreach (var (name, meta) in session.OutputMetadata)
        Console.WriteLine($"  out  {name,-24} {meta.ElementDataType,-10} [{string.Join(",", meta.Dimensions)}]");

    Console.WriteLine();
}

return 0;
