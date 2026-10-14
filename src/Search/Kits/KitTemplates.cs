using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Search.Kits;

/// <summary>
/// One thing a kit is made of, as the shop wrote it: **a search it already
/// knows how to run** — a category and words, in both languages — and whether
/// the kit needs it.
/// </summary>
/// <param name="Name">What the slot is called on the page: "Sartén", "Frying pan".</param>
/// <param name="Category">The category its candidates are looked for in, by code.</param>
/// <param name="Query">The words they are looked for with, in each culture.</param>
public sealed record KitTemplateSlot(string Code, LocalizedText Name, string Category, LocalizedText Query, bool Required);

/// <summary>
/// A kit the shop offers (phase 13c): authored by the shopkeeper, never made up
/// from a request. A model may be asked which of these somebody means; it is
/// never asked what a kit is made of.
/// </summary>
public sealed record KitTemplate(string Code, LocalizedText Name, IReadOnlyList<KitTemplateSlot> Slots)
{
    /// <summary>What the solver is exact and instant at, and what a page can show.</summary>
    public const int MaxSlots = 5;
}

/// <summary>
/// The kits the shop offers. A port, with a committed file behind it today, for
/// the reason the price lists are one: nothing can edit a template yet, so a
/// table would be state with no writer. The day a shopkeeper edits them, an
/// adapter over Postgres registers in this one's place.
/// </summary>
public interface IKitTemplateReader
{
    Task<IReadOnlyList<KitTemplate>> AllAsync(CancellationToken cancellationToken = default);
}
