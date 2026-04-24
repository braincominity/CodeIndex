namespace CodeIndex.Tests;

/// <summary>
/// Guards licensing and distribution metadata from silently drifting back to
/// permissive productization defaults.
/// </summary>
public class LicensePolicyTests
{
    [Fact]
    public void LicenseFile_UsesPolyFormPerimeterWithRequiredNotices()
    {
        var license = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "LICENSE"));

        Assert.Contains("PolyForm Perimeter License 1.0.0", license);
        Assert.Contains("Required Notice: Copyright 2026 Widthdom.", license);
        Assert.Contains("Any purpose is a permitted purpose, except for providing to others any product", license);
        Assert.DoesNotContain("MIT License", license);
        Assert.DoesNotContain("sublicense, and/or sell", license);
    }

    [Fact]
    public void NuGetPackage_EmbedsCustomLicenseAndRequiresAcceptance()
    {
        var project = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "src", "CodeIndex", "CodeIndex.csproj"));

        Assert.Contains("<PackageLicenseFile>LICENSE</PackageLicenseFile>", project);
        Assert.Contains("<PackageRequireLicenseAcceptance>true</PackageRequireLicenseAcceptance>", project);
        Assert.Contains(@"<None Include=""..\..\LICENSE"" Pack=""true"" PackagePath=""\""", project);
        Assert.Contains(@"<None Include=""..\..\COMMERCIAL_LICENSE.md"" Pack=""true"" PackagePath=""\""", project);
        Assert.Contains(@"<None Include=""..\..\TRADEMARKS.md"" Pack=""true"" PackagePath=""\""", project);
        Assert.Equal(3, CountOccurrences(project, @"CopyToPublishDirectory=""PreserveNewest"""));
        Assert.DoesNotContain("<PackageLicenseExpression>MIT</PackageLicenseExpression>", project);
    }

    [Fact]
    public void Readme_AdvertisesPerimeterLicenseInBothLanguageSections()
    {
        var readme = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "README.md"));

        Assert.Equal(2, CountOccurrences(readme, "License-PolyForm%20Perimeter%201.0.0-orange"));
        Assert.Contains("License and Commercial Use", readme);
        Assert.Contains("ライセンスと商用利用", readme);
        Assert.Contains("competing products or services require a separate agreement", readme);
        Assert.Contains("not intended to retroactively rewrite copies", readme);
        Assert.DoesNotContain("License-MIT", readme);
    }

    [Fact]
    public void ReleaseWorkflow_IsLimitedToCanonicalRepository()
    {
        var workflow = File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".github", "workflows", "release.yml"));

        Assert.True(CountOccurrences(workflow, "if: github.repository == 'Widthdom/CodeIndex'") >= 3);
        Assert.Contains("environment: release-production", workflow);
        Assert.Contains("environment: nuget-production", workflow);
        Assert.Contains("LICENSE COMMERCIAL_LICENSE.md TRADEMARKS.md", workflow);
    }

    [Fact]
    public void TrademarkAndCommercialPolicies_BlockCompetingDerivativeBranding()
    {
        var root = GetRepositoryRoot();
        var commercial = File.ReadAllText(Path.Combine(root, "COMMERCIAL_LICENSE.md"));
        var trademarks = File.ReadAllText(Path.Combine(root, "TRADEMARKS.md"));

        Assert.Contains("competing product or service based on CodeIndex", commercial);
        Assert.Contains("Requires a Separate Written Agreement", commercial);
        Assert.Contains("CodeIndex and cdidx are not licensed", trademarks);
        Assert.Contains("confusingly similar name", trademarks);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CodeIndex.sln")) || Directory.Exists(Path.Combine(dir.FullName, "src", "CodeIndex")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root / リポジトリルートを特定できませんでした");
    }
}
