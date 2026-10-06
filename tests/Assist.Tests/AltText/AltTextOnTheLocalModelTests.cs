using System.Text.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Assist.AltText;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests.AltText;

/// <summary>
/// The job against the shop's own model, started with its vision projector
/// (Tendero__Vision=true, the steward-model container on its pinned port), over a
/// picture of the seed catalogue. A CI runner cannot hold the model, so with no
/// such server answering the test skips; what it holds is that a real picture
/// comes back as a proposal with a sentence in each language — what the sentence
/// says is a person's to judge, and the test writes it out for one to read.
/// </summary>
public sealed class AltTextOnTheLocalModelTests(ITestOutputHelper output)
{
    private static readonly Uri Server = new("http://localhost:38085/");

    private static bool ReadsPictures()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var props = JsonDocument.Parse(http.GetStringAsync(new Uri(Server, "props")).GetAwaiter().GetResult());
            return props.RootElement.TryGetProperty("modalities", out var modalities)
                && modalities.TryGetProperty("vision", out var sees) && sees.ValueKind == JsonValueKind.True;
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static string SeedPicture(string itemId)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Tendero.slnx")))
            folder = folder.Parent;
        return Path.Combine(folder!.FullName, "seed", "images", itemId + ".webp");
    }

    private sealed class OnePicture(UndescribedPicture picture) : IUndescribedPictures
    {
        public Task<IReadOnlyList<UndescribedPicture>> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UndescribedPicture>>([picture]);
    }

    private sealed class FromDisk(string path) : IImageStore
    {
        public Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default) =>
            Task.FromResult<StoredImage?>(new StoredImage(File.OpenRead(path), "image/webp"));
    }

    /// <summary>Accepts what the real command accepts of length, and keeps what it was given.</summary>
    private sealed class Entry : IProposableCommand
    {
        public string Name => ProposeAltTextHandler.Command;

        public LocalizedText Label { get; } = LocalizedText.Both("Describe a product picture", "Describir una imagen de producto");

        public JsonElement PayloadSchema { get; } = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

        public Task<ProposalCheck> CheckAsync(JsonElement payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(payload.GetProperty("spanish").GetString()!.Length <= 300 && payload.GetProperty("english").GetString()!.Length <= 300
                ? ProposalCheck.Valid("SLIPPER", "{}")
                : ProposalCheck.Invalid("Too long."));

        public Task<string?> NowAsync(JsonElement payload, CancellationToken cancellationToken = default) => Task.FromResult<string?>("{}");

        public Task<ProposalApplication> ApplyAsync(JsonElement payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProposalApplication.Done);
    }

    [Theory]
    [InlineData("B10SHO0106", "Hogar house slippers", "Wool-lined house slipper with a non-slip sole. Hand washable.")]
    [InlineData("B13BAG0703", "Trayecto 40L travel backpack", null)]
    public async Task A_seed_picture_comes_back_as_a_proposal_with_a_sentence_in_each_language(string itemId, string name, string? description)
    {
        Assert.SkipUnless(ReadsPictures(), "needs the local model started with its vision projector (Tendero__Vision=true)");
        Assert.SkipUnless(File.Exists(SeedPicture(itemId)), $"needs seed/images/{itemId}.webp");

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AltText:Model"] = "local",
            ["Steward:Models:local:Endpoint"] = new Uri(Server, "v1/").ToString(),
            ["Steward:Models:local:Model"] = "qwen3.5-4b-q8_0",
            ["Steward:Models:local:ReadsPictures"] = "true",
        }).Build();
        var proposals = new InMemoryProposals();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(ProposeAltTextCommand).Assembly);
        services.AddSingleton<TimeProvider>(new TestClock());
        services.AddSingleton<IPrincipalAccessor>(new FixedPrincipal("Lucía"));
        services.AddSingleton<IProposalRepository>(proposals);
        services.AddSingleton<IUnitOfWork>(new CountingUnitOfWork());
        services.AddSingleton<IUndescribedPictures>(new OnePicture(new UndescribedPicture(Guid.CreateVersion7(), itemId, name, description, itemId, ["es", "en"])));
        services.AddSingleton<IImageStore>(new FromDisk(SeedPicture(itemId)));
        services.AddScoped<IProposableCommand, Entry>();
        services.AddAssist(configuration);
        services.AddModelProfiles(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(
            new ProposeAltTextCommand(1, "es"), TestContext.Current.CancellationToken);

        Assert.Equal((AltTextStatus.Ran, 1, 0, 0), (result.Status, result.Proposals.Count, result.Skipped, result.Failed));
        using var payload = JsonDocument.Parse(proposals.All.Single().Payload);
        var (spanish, english) = (payload.RootElement.GetProperty("spanish").GetString()!, payload.RootElement.GetProperty("english").GetString()!);
        output.WriteLine($"{itemId} · es ({spanish.Length}): {spanish}");
        output.WriteLine($"{itemId} · en ({english.Length}): {english}");
        Assert.Equal(("es", "en"), (WrittenLanguage.Of(spanish), WrittenLanguage.Of(english)));
    }
}
