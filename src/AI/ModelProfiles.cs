namespace ElGuerre.Tendero.AI;

/// <summary>
/// The profiles this host can reach, by name: a turn reads its cost's price and its
/// context window from the one that answered. Empty where no profile is configured.
/// </summary>
public sealed class ModelProfiles(IReadOnlyDictionary<string, ModelProfile> profiles)
{
    public static readonly ModelProfiles None = new(new Dictionary<string, ModelProfile>());

    public ModelProfile? Find(string name) => profiles.GetValueOrDefault(name);
}
