namespace DeskBox.Tests;

internal static class TestPaths
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static string FromRepository(string relativePath) =>
        Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public static string DotnetWorkingDirectory =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR"))
            ? RepositoryRoot
            : Path.GetTempPath();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "DeskBox", "DeskBox.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the DeskBox repository root.");
    }
}
