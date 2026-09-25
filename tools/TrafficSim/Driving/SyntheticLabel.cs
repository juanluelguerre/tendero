namespace ElGuerre.Tendero.TrafficSim.Driving;

/// <summary>
/// How every request a simulated session makes says that it is simulated (ADR
/// 0037, decision 4): a header with the run, W3C baggage with the run and the
/// persona, and a User-Agent suffix. Agents carry it too — the label is the only
/// thing that will tell a simulated agent from a real one the day there are real
/// ones.
/// </summary>
public sealed record SyntheticLabel(string RunId, string Persona)
{
    public const string Header = "X-Tendero-Synthetic";
    public const string UserAgentSuffix = "TenderoTrafficSim/1.0 (synthetic)";

    public IReadOnlyDictionary<string, string> Headers => new Dictionary<string, string>
    {
        [Header] = RunId,
        ["baggage"] = $"synthetic.run_id={RunId},synthetic.persona={Persona}",
    };
}
