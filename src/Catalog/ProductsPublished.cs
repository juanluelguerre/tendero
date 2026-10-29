using System.Diagnostics.Metrics;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog;

/// <summary>
/// `tendero.catalog.products.published` (the tracking plan): does the review
/// queue move, and — from phase 10 — is it a person approving what a model
/// proposed. Only a real Draft → Active counts; publishing twice changes nothing.
/// Shared by the slices that publish, one product or what a migration brought
/// (P16-12), so both count on one instrument.
/// </summary>
internal static class ProductsPublished
{
    private static readonly Meter Meter = new(TelemetrySources.Catalog);

    private static readonly Counter<long> Published = Meter.CreateCounter<long>(
        "tendero.catalog.products.published", unit: "{product}", description: "Products moved from Draft to Active.");

    public static void Record(Actor actor, int count = 1)
    {
        if (count > 0)
            Published.Add(count, actor.KindLabel);
    }
}
