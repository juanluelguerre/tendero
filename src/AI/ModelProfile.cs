namespace ElGuerre.Tendero.AI;

public enum ModelKind
{
    OpenAICompatible,
    OnnxInProcess,
}

public enum ModelAuth
{
    None,
    Bearer,
}

/// <summary>
/// One way to reach a model, from configuration (`Steward:Models:&lt;name&gt;`): a new
/// provider is an entry, not a class. Prices are € per million tokens, so a token
/// costs the price in micro-euros, and a row keeps the price of its moment.
/// </summary>
public sealed class ModelProfile
{
    public ModelKind Kind { get; init; } = ModelKind.OpenAICompatible;

    /// <summary>The provider's OpenAI base URL, /v1 included.</summary>
    public Uri? Endpoint { get; init; }

    public string? Model { get; init; }

    public ModelAuth Auth { get; init; } = ModelAuth.None;

    /// <summary>An Aspire secret parameter, never the repository.</summary>
    public string? ApiKey { get; init; }

    /// <summary>The model's folder, for an in-process ONNX profile.</summary>
    public string? Directory { get; init; }

    public decimal InputPrice { get; init; }

    public decimal OutputPrice { get; init; }

    /// <summary>The window the profile is driven against.</summary>
    public int ContextTokens { get; init; } = 8192;

    /// <summary>A local model on the CPU and a hosted one answer at very different
    /// speeds, so each profile says how long is too long.</summary>
    public int TimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// Whether this host can reach it. An OpenAI-compatible profile with no endpoint
    /// is not here — the local one until the AppHost hands it an address — and is
    /// left out without complaint; one that is half configured is a mistake.
    /// </summary>
    public bool IsAvailable => Kind != ModelKind.OpenAICompatible || Endpoint is not null;

    public long CostMicroEuros(long inputTokens, long outputTokens) =>
        (long)Math.Round(inputTokens * InputPrice + outputTokens * OutputPrice, MidpointRounding.AwayFromZero);

    /// <summary>What keeps this profile from being called, said at start rather
    /// than at the first call.</summary>
    public IReadOnlyList<string> Problems(string name)
    {
        List<string> problems = [];
        if (Kind == ModelKind.OpenAICompatible)
        {
            if (string.IsNullOrWhiteSpace(Model))
                problems.Add($"Model profile '{name}' has no Model.");
            if (Auth == ModelAuth.Bearer && string.IsNullOrWhiteSpace(ApiKey))
                problems.Add($"Model profile '{name}' uses Bearer and has no ApiKey.");
        }
        else if (string.IsNullOrWhiteSpace(Directory))
        {
            problems.Add($"Model profile '{name}' is in-process and has no Directory.");
        }

        return problems;
    }
}
