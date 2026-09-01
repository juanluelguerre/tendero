using System.Text;
using ElGuerre.Tendero.Catalog.Adapters;
using ElGuerre.Tendero.Catalog.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Adapters;

/// <summary>
/// El almacén direcciona por contenido, y de ahí salen dos propiedades que la
/// aplicación da por hechas: la clave es estable (el mismo byte-a-byte da la
/// misma clave siempre) y guardar dos veces no duplica.
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

        // Es la deduplicación que hace barato un catálogo donde media docena de
        // productos comparten la misma foto de familia.
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
        // Las claves llegan por la URL y cualquiera puede inventarse una: eso es
        // un 404, no una excepción.
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
