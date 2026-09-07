using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// Where the SDXL export is, for the tests that need a real one.
///
/// It is ten gigabytes and it is not in the repository, so these tests **skip
/// with a reason** rather than fail — the same bargain the Testcontainers tests
/// already make about Docker. A test that cannot run says so; it does not go
/// quietly green.
/// </summary>
public static class ModelDirectory
{
    private const string Variable = "TENDERO_SDXL_MODELS";

    /// <summary>The tokenizer folder, or a skip.</summary>
    public static string Tokenizer(string which = "tokenizer")
    {
        var root = Environment.GetEnvironmentVariable(Variable)
            ?? Path.Combine(RepositoryRoot.Find(), "models");

        var path = Path.Combine(root, which);

        if (!File.Exists(Path.Combine(path, "vocab.json")))
            Assert.Skip($"No SDXL export at '{root}'. Set {Variable} to one to run this.");

        return path;
    }
}
