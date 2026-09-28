using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Pricing.Features.ManageCampaigns;

/// <param name="ImageId">The key to send as the campaign's <c>image.imageId</c>; served at <c>/api/images/{imageId}</c>.</param>
public sealed record CampaignImageUploaded(string ImageId);

/// <summary>
/// A campaign's picture into the image store, stored and served as a product's
/// is (ADR 0044): by the hash of its content, so uploading the same file twice
/// is one image, and the URL can be cached for a year.
///
/// **Not a command, and not audited by itself.** Storing bytes changes nothing
/// the shop shows: the change is the campaign that references them, and that is
/// a create or an update with its own audit row. An image uploaded and never
/// used is an orphan in the store, as an import's rejected photo is.
///
/// **What is accepted is decided by the bytes, not the header.** WebP, AVIF, PNG
/// and JPEG, recognised by their signature; the content type the store records is
/// the one the signature says. No GIF — heavy for what it gives and deaf to
/// <i>reduce motion</i> (the spec) — and no SVG, which is a document that can
/// carry script, served from the shop's own origin.
/// </summary>
public sealed class UploadCampaignImageEndpoint : ICarterModule
{
    /// <summary>A wide campaign picture in WebP is a few hundred kilobytes; two megabytes is room, not a target.</summary>
    public const long MaxBytes = 2 * 1024 * 1024;

    private static readonly ActivitySource Telemetry = new(TelemetrySources.Pricing);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pricing/campaign-images",
                async Task<Results<Ok<CampaignImageUploaded>, BadRequest<string>>> (
                    IFormFile file, IImageStore store, CancellationToken ct) =>
                {
                    using var activity = Telemetry.StartActivity("pricing.upload_campaign_image");
                    activity?.SetTag("pricing.image_bytes", file.Length);

                    if (file.Length == 0)
                        return TypedResults.BadRequest("The file is empty.");
                    if (file.Length > MaxBytes)
                        return TypedResults.BadRequest($"A campaign image is at most {MaxBytes / (1024 * 1024)} MB.");

                    using var buffer = new MemoryStream(capacity: (int)file.Length);
                    await using (var upload = file.OpenReadStream())
                        await upload.CopyToAsync(buffer, ct);

                    var contentType = ImageSignature.ContentTypeOf(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
                    if (contentType is null)
                        return TypedResults.BadRequest("A campaign image is WebP, AVIF, PNG or JPEG.");

                    activity?.SetTag("pricing.image_type", contentType);

                    buffer.Position = 0;
                    var id = await store.SaveAsync(buffer, contentType, ct);
                    return TypedResults.Ok(new CampaignImageUploaded(id.Value));
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            // A bearer token, not a cookie: nothing here for a cross-site form to
            // ride on, which is what antiforgery protects against.
            .DisableAntiforgery()
            .WithTags("Pricing")
            .WithName("UploadCampaignImage");
    }
}

/// <summary>The four formats a campaign picture may be, by their first bytes.</summary>
public static class ImageSignature
{
    public static string? ContentTypeOf(ReadOnlySpan<byte> bytes)
    {
        // RIFF....WEBP
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";

        // ....ftypavif / ftypavis (an ISO-BMFF box whose brand is AVIF)
        if (bytes.Length >= 12 && bytes[4..8].SequenceEqual("ftyp"u8)
            && (bytes[8..12].SequenceEqual("avif"u8) || bytes[8..12].SequenceEqual("avis"u8)))
            return "image/avif";

        if (bytes.Length >= 8 && bytes[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return "image/png";

        if (bytes.Length >= 3 && bytes[..3].SequenceEqual((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return "image/jpeg";

        return null;
    }
}
