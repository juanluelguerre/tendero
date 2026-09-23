using Xunit;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// The committed <c>docs/domain-model.md</c> is the one the code produces — the
/// same contract the OpenAPI document keeps with the running API, applied to the
/// domain. A diagram nobody regenerates is the mockup nobody read.
/// </summary>
public sealed class DomainModelDocumentTests
{
    private const string UpdateVariable = "UPDATE_DOMAIN_MODEL";

    [Fact]
    public void The_committed_domain_model_is_the_one_the_code_describes()
    {
        var generated = DomainModelDocument.Render();
        var committed = Path.Combine(RepositoryRoot(), "docs", "domain-model.md");

        if (Environment.GetEnvironmentVariable(UpdateVariable) is not (null or ""))
        {
            File.WriteAllText(committed, generated);
            return;
        }

        Assert.True(File.Exists(committed),
            $"{committed} does not exist. Generate it with: {UpdateVariable}=1 dotnet test --project tests/Architecture.Tests");

        // Compared with line endings normalised: git may check the file out with CRLF.
        var onDisk = File.ReadAllText(committed).ReplaceLineEndings("\n");

        Assert.True(onDisk == generated, $"""
            The domain model in {committed} no longer describes the code.
            If the change to the domain is intended, regenerate it and commit the result:

                {UpdateVariable}=1 dotnet test --project tests/Architecture.Tests
            """);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tendero.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Tendero.slnx not found above the test output.");
    }
}
