using System.Text.Json;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Search.Kits.Seed;

public sealed class KitSeedOptions
{
    public const string SectionName = "Kits:Seed";

    public string TemplatesPath { get; set; } = Path.Combine("seed", "kits.sample.json");
}

/// <summary>
/// The shop's kits from a committed file — **and not from a table**, for the
/// reason the promotions are not in one: nothing can edit a template yet, and a
/// table nothing writes is the mistake phase 2 made and undid the next day.
///
/// With no file the shop offers no kits, which is a shop without the feature
/// and not a shop that is down. A file written wrong is another matter: it is
/// refused whole and by name, because a template that half loaded is a kit that
/// silently lost its pan.
/// </summary>
internal sealed class SeedFileKitTemplates(IOptions<KitSeedOptions> options) : IKitTemplateReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly string[] Cultures = ["es", "en"];

    private IReadOnlyList<KitTemplate>? cached;

    public async Task<IReadOnlyList<KitTemplate>> AllAsync(CancellationToken cancellationToken = default)
    {
        if (this.cached is not null)
            return this.cached;

        var path = options.Value.TemplatesPath;
        if (!File.Exists(path))
            return this.cached = [];

        await using var stream = File.OpenRead(path);
        var written = await JsonSerializer.DeserializeAsync<Written[]>(stream, Json, cancellationToken)
            ?? throw new InvalidDataException($"'{path}' is not a list of kits.");

        return this.cached = Read(written, path);
    }

    /// <summary>The templates as written, held to what a kit has to be before anybody is offered one.</summary>
    internal static IReadOnlyList<KitTemplate> Read(IReadOnlyList<Written> written, string path)
    {
        List<KitTemplate> templates = [];

        foreach (var kit in written)
        {
            if (String.IsNullOrWhiteSpace(kit.Code))
                throw new InvalidDataException($"A kit of '{path}' has no code.");

            var slots = kit.Slots ?? [];
            if (slots.Count is 0 or > KitTemplate.MaxSlots)
                throw new InvalidDataException($"Kit '{kit.Code}' has {slots.Count} slots: a kit has one to {KitTemplate.MaxSlots}.");
            if (slots.Select(slot => slot.Code).Distinct(StringComparer.Ordinal).Count() != slots.Count)
                throw new InvalidDataException($"Kit '{kit.Code}' names a slot twice.");

            templates.Add(new KitTemplate(
                kit.Code,
                Both(kit.Name, $"kit '{kit.Code}'"),
                [
                    .. slots.Select(slot => String.IsNullOrWhiteSpace(slot.Code) || String.IsNullOrWhiteSpace(slot.Category)
                        ? throw new InvalidDataException($"A slot of kit '{kit.Code}' has no code or no category.")
                        : new KitTemplateSlot(
                            slot.Code,
                            Both(slot.Name, $"slot '{slot.Code}' of kit '{kit.Code}'"),
                            slot.Category.Trim().ToUpperInvariant(),
                            Both(slot.Query, $"the search of slot '{slot.Code}' of kit '{kit.Code}'"),
                            slot.Required)),
                ]));
        }

        if (templates.Select(template => template.Code).Distinct(StringComparer.Ordinal).Count() != templates.Count)
            throw new InvalidDataException($"'{path}' names a kit twice.");

        return templates;
    }

    /// <summary>Invariant 6 has no exceptions: a kit's name, a slot's, and the words it is searched with exist in es and en.</summary>
    private static LocalizedText Both(Dictionary<string, string>? text, string of)
    {
        if (text is null || Cultures.Any(culture => !text.TryGetValue(culture, out var value) || String.IsNullOrWhiteSpace(value)))
            throw new InvalidDataException($"The text of {of} is not written in both es and en.");

        return new LocalizedText(text);
    }

    internal sealed record Written(string Code, Dictionary<string, string>? Name, List<WrittenSlot>? Slots);

    internal sealed record WrittenSlot(
        string Code, Dictionary<string, string>? Name, string Category, Dictionary<string, string>? Query, bool Required);
}
