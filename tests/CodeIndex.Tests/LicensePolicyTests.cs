namespace CodeIndex.Tests;

/// <summary>
/// Guards licensing and distribution metadata from silently drifting back to
/// permissive productization defaults.
/// </summary>
public class LicensePolicyTests
{
    [Fact]
    public void LicenseFile_UsesFslWithFutureApacheLicenseNotice()
    {
        var license = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "LICENSE"));

        Assert.Contains("CodeIndex is source-available under a Fair Source-style license.", license);
        Assert.Contains("Functional Source License, Version 1.1, ALv2 Future License (FSL-1.1-ALv2).", license);
        Assert.Contains("Copyright 2026 Widthdom.", license);
        Assert.Contains("LICENSES/FSL-1.1-ALv2.txt", license);
        Assert.Contains("LICENSES/Apache-2.0.txt", license);
        Assert.DoesNotContain("PolyForm Perimeter", license);
        Assert.DoesNotContain("MIT License", license);
    }

    [Fact]
    public void NuGetPackage_EmbedsCustomLicenseAndRequiresAcceptance()
    {
        var project = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "src", "CodeIndex", "CodeIndex.csproj"));

        Assert.Contains("fair-source", project);
        Assert.Contains("<PackageLicenseFile>LICENSE</PackageLicenseFile>", project);
        Assert.Contains("<PackageRequireLicenseAcceptance>true</PackageRequireLicenseAcceptance>", project);
        Assert.Contains(@"<None Include=""..\..\LICENSE"" Pack=""true"" PackagePath=""\""", project);
        Assert.Contains(@"<None Include=""..\..\COMMERCIAL_LICENSE.md"" Pack=""true"" PackagePath=""\""", project);
        Assert.Contains(@"<None Include=""..\..\INTEGRATION_POLICY.md"" Pack=""true"" PackagePath=""\""", project);
        Assert.Contains(@"<None Include=""..\..\TRADEMARKS.md"" Pack=""true"" PackagePath=""\""", project);
        Assert.Contains(@"<None Include=""..\..\LICENSES\FSL-1.1-ALv2.txt"" Pack=""true"" PackagePath=""LICENSES\""", project);
        Assert.Contains(@"<None Include=""..\..\LICENSES\Apache-2.0.txt"" Pack=""true"" PackagePath=""LICENSES\""", project);
        Assert.Equal(6, CountOccurrences(project, @"CopyToPublishDirectory=""PreserveNewest"""));
        Assert.DoesNotContain("<PackageLicenseExpression>MIT</PackageLicenseExpression>", project);
    }

    [Fact]
    public void Readme_AdvertisesFairSourceLicenseInBothLanguageSections()
    {
        var readme = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "README.md"));

        Assert.Equal(2, CountOccurrences(readme, "License-FSL--1.1--ALv2-orange"));
        Assert.Contains("License and Fair Source Use", readme);
        Assert.Contains("ライセンスと Fair Source の扱い", readme);
        Assert.Equal(2, CountOccurrences(readme, "Fair Source-style software"));
        Assert.Contains("INTEGRATION_POLICY.md", readme);
        Assert.Contains("LICENSES/Apache-2.0.txt", readme);
        Assert.DoesNotContain("License-MIT", readme);
    }

    [Fact]
    public void ReleaseWorkflow_IsLimitedToCanonicalRepository()
    {
        var workflow = File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".github", "workflows", "release.yml"));

        Assert.True(CountOccurrences(workflow, "if: github.repository == 'Widthdom/CodeIndex'") >= 3);
        Assert.Contains("environment: release-production", workflow);
        Assert.Contains("environment: nuget-production", workflow);
        Assert.Contains("LICENSE COMMERCIAL_LICENSE.md INTEGRATION_POLICY.md TRADEMARKS.md", workflow);
        Assert.Contains("LICENSES/FSL-1.1-ALv2.txt", workflow);
        Assert.Contains("LICENSES/Apache-2.0.txt", workflow);
    }

    [Fact]
    public void TrademarkAndCommercialPolicies_BlockCompetingDerivativeBranding()
    {
        var root = GetRepositoryRoot();
        var commercial = File.ReadAllText(Path.Combine(root, "COMMERCIAL_LICENSE.md"));
        var trademarks = File.ReadAllText(Path.Combine(root, "TRADEMARKS.md"));

        Assert.Contains("Allowed Without a Separate Agreement", commercial);
        Assert.Contains("commercial product or service", commercial);
        Assert.Contains("substitutes for CodeIndex", commercial);
        Assert.Contains("AI coding agents", commercial);
        Assert.Contains("compatible with CodeIndex", trademarks);
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
