using System.Text;
using ElGuerre.Tendero.Catalog.Adapters;
using ElGuerre.Tendero.Catalog.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Adapters;

/// <summary>
/// The store addresses by content, and two properties the application takes for
/// granted follow from that: the key is stable (the same bytes always give the
/// same key) and storing twice does not duplicate.
/// </summary>
public sealed class FileSystemImageStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "tendero-image-store-tests", Guid.CreateVersion7().ToString());

    private IImageStore Store() => new FileSystemImageStore(
        Options.Create(new FileSystemImageStoreOptions { RootPath = _root }),
        NullLogger<FileSystemImageStore>.Instance);

    private static Stream Bytes(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task The_same_content_always_gets_the_same_key()
    {
        var store = Store();

        var first = await store.SaveAsync(Bytes("una foto"), "image/png", TestContext.Current.CancellationToken);
        var second = await store.SaveAsync(Bytes("una foto"), "image/png", TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Different_content_gets_a_different_key()
    {
        var store = Store();

        var first = await store.SaveAsync(Bytes("una foto"), "image/png", TestContext.Current.CancellationToken);
        var second = await store.SaveAsync(Bytes("otra foto"), "image/png", TestContext.Current.CancellationToken);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Storing_the_same_image_twice_writes_one_file()
    {
        var store = Store();

        await store.SaveAsync(Bytes("una foto"), "image/png", TestContext.Current.CancellationToken);
        await store.SaveAsync(Bytes("una foto"), "image/png", TestContext.Current.CancellationToken);

        // This is the deduplication that makes a catalogue cheap where half a
        // dozen products share the same family photo.
        Assert.Single(Directory.EnumerateFiles(_root, "*.*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task What_goes_in_comes_out_with_its_content_type()
    {
        var store = Store();
        var id = await store.SaveAsync(Bytes("una foto"), "image/png", TestContext.Current.CancellationToken);

        var stored = await store.OpenAsync(id, TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal("image/png", stored.ContentType);
        using var reader = new StreamReader(stored.Content);
        Assert.Equal("una foto", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unknown_key_is_absent_not_an_error()
    {
        // Keys arrive in the URL and anybody can invent one: that is a 404, not
        // an exception.
        var stored = await Store().OpenAsync(
            new SharedKernel.ImageId(new string('0', 64)), TestContext.Current.CancellationToken);

        Assert.Null(stored);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
