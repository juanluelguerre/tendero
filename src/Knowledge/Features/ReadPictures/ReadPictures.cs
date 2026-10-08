using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Carter;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.AI.Chat;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Knowledge.Features.ReadPictures;

/// <summary>The picture-reading job's settings (`PictureReading:` in configuration).</summary>
public sealed class PictureReadingOptions
{
    public const string SectionName = "PictureReading";

    /// <summary>
    /// Pictures a batch reads at most. Five, as the alt-text job's (the author's
    /// ruling, 2026-10-06): what a batch leaves is what a person reviews at once.
    /// </summary>
    public const int MaxBatch = 5;

    /// <summary>The profile that reads the pictures, by its name among the shop's model profiles.</summary>
    public string? Model { get; init; } = "local";

    /// <summary>The description at most: a few sentences, in each language. A closed question's answer is a number.</summary>
    public int MaxOutputTokens { get; init; } = 500;
}

/// <summary>
/// The model's instructions, versioned: a reading records the version it was made
/// under, and a picture read under an older one is read again — two readings are
/// only comparable when the same instructions wrote them.
/// </summary>
public static class PictureReadingPrompt
{
    public const string Version = "picture-reading-2026-10-08b";

    /// <summary>What the answer is held to: the description, in each language.</summary>
    public static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": { "es": { "type": "string" }, "en": { "type": "string" } },
          "required": ["es", "en"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public const string Instructions = """
        You describe the pictures of an online shop's products for the shop's own
        records. Look at the picture and write what it shows, as plain facts.

        - Two to four short sentences: the kind of object, how many of them there
          are, its colours, what it appears to be made of, and the parts that can
          be seen.
        - Only what the picture shows. No brand, no price, no size, no use, and
          nothing you would have to guess: what cannot be told from the picture is
          left out.
        - You are not told what the product is. Name the object as it looks, in
          plain words, and do not guess a model, a capacity or a set it belongs to.
        - Reply in JSON: "es" is the description in Spanish, "en" the same
          description in English.
        """;

    /// <summary>
    /// What goes beside the picture: **nothing of the product.** The first
    /// version gave its name, "to name the object correctly", and the shop's 4B
    /// handed the name back as something seen — one slipper read as "a pair of
    /// slippers", a moka pot as "a six-cup coffee maker". A reading is worth
    /// keeping as a source only while nothing in it came from the catalogue: a
    /// claim taken from it cites a picture.
    /// </summary>
    public const string Ask = "Describe the picture.";
}

/// <summary>
/// Have the next pictures read that nobody has read under today's instructions
/// (phase 13a, I-4). A batch a shopkeeper asks for, never a queue that drains on
/// its own. It leaves documents — a model's description of each picture, in each
/// language, and under it what the picture answered — and proposes nothing:
/// claims are proposed from those documents by the step that proposes them from
/// a description, and a person approves each (invariant 12).
///
/// **Two questions a picture.** What it shows, in prose, for a person to read;
/// and how many items are drawn (<see cref="PictureQuestions.Count"/>), a number
/// code turns into a claim. The second is asked by itself, with the wording the
/// known-bad set measures: nothing is extracted from the prose afterwards.
/// **And as plain text, not under a schema** (<see cref="ClosedQuestion"/>): the
/// number is one token instead of the twenty-five the schema's grammar padded it
/// to, and it comes with the probability the model gave it — which the reading
/// keeps, and which decides whether its claim may be approved with others.
///
/// **And its colour, when it has one to be held to** (I-4d): a picture that
/// shows variants of one colour is asked which of the catalogue's colours it
/// is. What it answers is kept under the reading, and a sure difference from
/// what the catalogue holds is a doubt about the picture — never a claim.
///
/// **A picture is asked what its product is offered** (ADR 0056): how many,
/// only of a product whose category declares its pieces. A shoe's does not — a
/// pair is not two pieces — so its picture is described and not counted.
/// </summary>
/// <param name="Count">How many pictures, at most <see cref="PictureReadingOptions.MaxBatch"/>.</param>
public sealed record ReadPicturesCommand(int Count) : ICommand<ReadPicturesResult>;

public enum PictureReadingStatus
{
    /// <summary>The batch ran: see how many were read, skipped and failed.</summary>
    Ran,

    /// <summary>Every picture has been read under these instructions.</summary>
    NothingToRead,

    /// <summary>No profile that reads pictures is here: nothing was opened and nothing kept.
    /// Claims go on being proposed from the descriptions alone.</summary>
    NoModel,
}

/// <param name="Read">Pictures whose reading was kept.</param>
/// <param name="Skipped">Pictures whose bytes the store did not have, or were not a picture.</param>
/// <param name="Failed">Pictures the model did not describe in a way that can be kept.</param>
public sealed record ReadPicturesResult(PictureReadingStatus Status, int Read, int Skipped, int Failed);

public sealed class ReadPicturesValidator : AbstractValidator<ReadPicturesCommand>
{
    public ReadPicturesValidator()
    {
        RuleFor(x => x.Count).InclusiveBetween(1, PictureReadingOptions.MaxBatch);
    }
}

public sealed class ReadPicturesHandler(
    IProductPictures pictures,
    IImageStore images,
    IKnowledgeRepository knowledge,
    IAttributeVocabulary vocabulary,
    ICommandDispatcher commands,
    IServiceProvider services,
    ModelProfiles profiles,
    IPrincipalAccessor principal,
    IOptions<PictureReadingOptions> options,
    ILogger<ReadPicturesHandler> logger) : ICommandHandler<ReadPicturesCommand, ReadPicturesResult>
{
    /// <summary>What the model's calls are metered under.</summary>
    public const string Operation = "knowledge.read_picture";

    private static readonly ActivitySource Telemetry = new(TelemetrySources.Knowledge);
    private static readonly Meter Meter = new(TelemetrySources.Knowledge);

    /// <summary>`tendero.picture_reading.pictures`: pictures a batch took, by what became of each.</summary>
    private static readonly Counter<long> Taken = Meter.CreateCounter<long>(
        "tendero.picture_reading.pictures", unit: "{picture}", description: "Pictures the reading job took, by what became of each.");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ReadPicturesResult> HandleAsync(ReadPicturesCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("knowledge.pictures.read");
        var settings = options.Value;

        // Asked before anything is opened: a profile that does not say it reads
        // pictures is not sent one (invariant 8).
        if (settings.Model is not { Length: > 0 } name
            || profiles.Find(name) is not { ReadsPictures: true } profile
            || services.GetKeyedService<IChatClient>(name) is not { } model)
        {
            activity?.SetTag("knowledge.unavailable", true);
            return new ReadPicturesResult(PictureReadingStatus.NoModel, 0, 0, 0);
        }

        // Who read it, for the document: the model the profile names, or the
        // profile itself when it names none.
        var reader = profile.Model ?? name;

        var batch = await NextAsync(reader, command.Count, cancellationToken);
        if (batch.Count == 0)
            return new ReadPicturesResult(PictureReadingStatus.NothingToRead, 0, 0, 0);

        var colours = await pictures.ColoursAsync(cancellationToken);

        var (read, skipped, failed) = (0, 0, 0);
        foreach (var picture in batch)
        {
            if (await OpenAsync(picture, cancellationToken) is not { } content)
            {
                skipped++;
                continue;
            }

            if (await DescribeAsync(model, picture, content, settings, cancellationToken) is not { } said)
            {
                failed++;
                continue;
            }

            // Its words and the answers it was asked for, or nothing: a picture
            // the model described and did not count is read again, whole, by the
            // next batch.
            Counted? counted = null;
            if (await IsCountedAsync(picture, cancellationToken))
            {
                counted = await CountAsync(model, picture, content, cancellationToken);
                if (counted is null)
                {
                    failed++;
                    continue;
                }
            }

            ColourSeen? colour = null;
            if (Held(picture, colours) is { } held)
            {
                var answered = await ColourAsync(model, picture, content, held, colours, cancellationToken);
                if (answered.Failed)
                {
                    failed++;
                    continue;
                }

                colour = answered.Seen;
            }

            await commands.SendAsync(
                new RecordPictureReadingCommand(
                    new ProductId(picture.ProductId),
                    new PictureReading(new ImageId(picture.ImageId), reader, PictureReadingPrompt.Version),
                    said.Es,
                    said.En,
                    counted?.Number,
                    counted?.Confidence,
                    colour),
                cancellationToken);
            read++;
        }

        Count("read", read);
        Count("skipped", skipped);
        Count("failed", failed);
        activity?.SetTag("knowledge.pictures.read", read);
        return new ReadPicturesResult(PictureReadingStatus.Ran, read, skipped, failed);
    }

    /// <summary>
    /// The next pictures with no reading in both languages made by this model under
    /// these instructions, in the catalogue's order. A hundred products and one
    /// look-up a picture: the scale rule's "deferred with its number".
    /// </summary>
    private async Task<IReadOnlyList<ProductPicture>> NextAsync(string model, int count, CancellationToken cancellationToken)
    {
        List<ProductPicture> next = [];
        foreach (var picture in await pictures.AllAsync(cancellationToken))
        {
            var readings = await knowledge.CurrentReadingsAsync(new ProductId(picture.ProductId), new ImageId(picture.ImageId), cancellationToken);
            var current = readings.Count == 2 && readings.All(document =>
                document.Reading is { } reading && reading.Model == model && reading.PromptVersion == PictureReadingPrompt.Version);

            if (current)
                continue;

            next.Add(picture);
            if (next.Count == count)
                break;
        }

        return next;
    }

    /// <summary>The colour a picture is held to, when it has one and the catalogue still defines it.</summary>
    private static PictureColour? Held(ProductPicture picture, IReadOnlyList<PictureColour> colours) =>
        picture.Colour is { } code
            ? colours.FirstOrDefault(colour => string.Equals(colour.Code, code, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>
    /// The colour the model chooses for the picture among the catalogue's, beside
    /// the one the picture is held to. An answer that is none of them is no
    /// answer — the reading is kept without a colour; a model that did not
    /// answer at all is a failed picture, read again.
    /// </summary>
    private async Task<(ColourSeen? Seen, bool Failed)> ColourAsync(
        IChatClient model, ProductPicture picture, DataContent content, PictureColour held, IReadOnlyList<PictureColour> colours,
        CancellationToken cancellationToken)
    {
        try
        {
            var choice = await ClosedQuestion.AskAsync(
                model, [content], PictureQuestions.Colour, [.. colours.Select(colour => colour.English)],
                ModelCall.Options(Operation, promptVersion: PictureReadingPrompt.Version, person: principal.Current.Subject),
                cancellationToken);

            return (PictureColours.Seen(choice, held, colours), false);
        }
        catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(failure, "The picture-reading job's model did not say the colour of a picture of {Code}.", picture.Code);
            return (null, true);
        }
    }

    /// <summary>Whether a picture of this product is asked how many: when the product is offered its pieces from a picture.</summary>
    private async Task<bool> IsCountedAsync(ProductPicture picture, CancellationToken cancellationToken) =>
        (await vocabulary.CurrentAsync(new ProductId(picture.ProductId), SourceKind.ProductImage, cancellationToken))
            .Any(attribute => string.Equals(attribute.Code, PictureAnswers.Pieces, StringComparison.OrdinalIgnoreCase));

    /// <summary>The picture as a model takes it, or null when the store has no such bytes or they are no picture.</summary>
    private async Task<DataContent?> OpenAsync(ProductPicture picture, CancellationToken cancellationToken)
    {
        var stored = await images.OpenAsync(new ImageId(picture.ImageId), cancellationToken);
        if (stored is null)
            return null;

        await using var content = stored.Content;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        try
        {
            return Pictures.ForModel(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// What the model says the picture shows, or null when what it wrote cannot be
    /// kept: not the shape asked for, a language missing, or a description that is
    /// not in the language it is filed under — told by code, not by asking again.
    /// </summary>
    private async Task<Said?> DescribeAsync(
        IChatClient model, ProductPicture picture, DataContent content, PictureReadingOptions settings, CancellationToken cancellationToken)
    {
        var said = await AskAsync<Said>(
            model, picture,
            [
                new ChatMessage(ChatRole.System, PictureReadingPrompt.Instructions),
                new ChatMessage(ChatRole.User, [content, new TextContent(PictureReadingPrompt.Ask)]),
            ],
            PictureReadingPrompt.Schema, "picture_reading", settings.MaxOutputTokens, cancellationToken);

        return said is { Es.Length: > 0, En.Length: > 0 }
            && !string.IsNullOrWhiteSpace(said.Es) && !string.IsNullOrWhiteSpace(said.En)
            && WrittenLanguage.Is(said.Es, "es") && WrittenLanguage.Is(said.En, "en")
                ? new Said(said.Es.Trim(), said.En.Trim())
                : null;
    }

    /// <summary>
    /// How many items the model counts in the picture and how sure it was, or null
    /// when it answered no number. The question alone, with no instructions
    /// before it: that is how it was measured.
    /// </summary>
    private async Task<Counted?> CountAsync(IChatClient model, ProductPicture picture, DataContent content, CancellationToken cancellationToken)
    {
        try
        {
            var counted = await ClosedQuestion.CountAsync(
                model, [content], PictureQuestions.Count,
                ModelCall.Options(Operation, promptVersion: PictureReadingPrompt.Version, person: principal.Current.Subject),
                cancellationToken);

            return counted.Number is null ? null : counted;
        }
        catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(failure, "The picture-reading job's model did not count a picture of {Code}.", picture.Code);
            return null;
        }
    }

    /// <summary>The model's answer to one question about the picture, held to a schema; null when it cannot be read as one.</summary>
    private async Task<T?> AskAsync<T>(
        IChatClient model, ProductPicture picture, IReadOnlyList<ChatMessage> messages, JsonElement schema, string schemaName, int maxOutputTokens,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var response = await model.GetResponseAsync(
                messages,
                ModelCall.Options(Operation, promptVersion: PictureReadingPrompt.Version,
                    options: new ChatOptions
                    {
                        ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, schemaName),
                        MaxOutputTokens = maxOutputTokens,
                        Temperature = 0,
                    },
                    person: principal.Current.Subject),
                cancellationToken);

            return JsonSerializer.Deserialize<T>(response.Text, Json);
        }
        catch (Exception failure) when (failure is JsonException or HttpRequestException or InvalidOperationException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(failure, "The picture-reading job's model did not answer \"{Question}\" about a picture of {Code}.", schemaName, picture.Code);
            return null;
        }
    }

    private static void Count(string outcome, int count)
    {
        if (count > 0)
            Taken.Add(count, new KeyValuePair<string, object?>("outcome", outcome));
    }

    private sealed record Said(string Es, string En);
}

/// <summary>What a model's choice of colour is, beside the colour the picture is held to.</summary>
public static class PictureColours
{
    /// <summary>What the colour question is about, as a doubt records it.</summary>
    public const string Attribute = "COLOR";

    /// <summary>
    /// The colour chosen as values a doubt can be raised from — or null when what
    /// the model chose is none of the catalogue's. The two are **related** when
    /// the catalogue says the one held may be taken for the one seen: that is
    /// the catalogue's to say, and it is carried here, not judged.
    /// </summary>
    public static ColourSeen? Seen(Choice choice, PictureColour held, IReadOnlyList<PictureColour> colours)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(colours);

        return colours.FirstOrDefault(colour => colour.English == choice.Option) is { } seen
            ? new ColourSeen(
                Attribute,
                seen.Code, LocalizedText.From("es", seen.Spanish).With("en", seen.English),
                held.Code, LocalizedText.From("es", held.Spanish).With("en", held.English),
                choice.Confidence,
                Related: held.MistakenFor.Contains(seen.Code, StringComparer.OrdinalIgnoreCase))
            : null;
    }
}

// ---------- The wire ----------

public sealed record ReadPicturesRequest(int? Count);

public sealed class ReadPicturesEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/knowledge/pictures/read  { "count": 5 }
        // A shopkeeper asks for a batch: what comes back is how many pictures were
        // read — nothing is proposed and nothing is written to the catalogue here.
        app.MapPost("/api/knowledge/pictures/read",
                async Task<Ok<ReadPicturesResult>> (ReadPicturesRequest? body, ICommandDispatcher dispatcher, CancellationToken ct) =>
                    TypedResults.Ok(await dispatcher.SendAsync(new ReadPicturesCommand(body?.Count ?? PictureReadingOptions.MaxBatch), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Knowledge")
            .WithName("ReadPictures");
    }
}
