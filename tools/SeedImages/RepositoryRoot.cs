namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// Where the repository is, found by walking up until the solution file is
/// under foot.
///
/// <c>SearchEval</c> takes plain relative paths and assumes you ran it from the
/// root; that works and it is one `cd` away from not working. The marker walk is
/// the idiom <c>SeedFileWiringTests</c> uses, and it is the one that survives
/// being run from a test's output directory — which is exactly where the tests
/// for this tool run.
/// </summary>
public static class RepositoryRoot
{
    private const string Marker = "Tendero.slnx";

    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, Marker)))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find {Marker} above {AppContext.BaseDirectory}.");
    }
}
