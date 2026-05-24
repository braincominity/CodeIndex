using System.Text.Json;
using Xunit;

namespace CodeIndex.Tests;

public class PackagesLockTests
{
    [Theory]
    [InlineData("System.Net.Http", "4.3.4")]
    [InlineData("System.Text.RegularExpressions", "4.3.1")]
    public void CodeIndexTests_Net9LockFile_IncludesDirectCompatibilityReferences(string packageName, string version)
    {
        var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../.."));
        var lockFilePath = Path.Combine(projectDirectory, "packages.lock.json");

        using var document = JsonDocument.Parse(File.ReadAllText(lockFilePath));
        var net9Dependencies = document.RootElement
            .GetProperty("dependencies")
            .GetProperty("net9.0");

        Assert.True(net9Dependencies.TryGetProperty(packageName, out var package), $"{packageName} is missing from net9.0 lock dependencies.");
        Assert.Equal("Direct", package.GetProperty("type").GetString());
        Assert.Equal($"[{version}, )", package.GetProperty("requested").GetString());
        Assert.Equal(version, package.GetProperty("resolved").GetString());
    }
}
