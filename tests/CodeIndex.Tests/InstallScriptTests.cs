using System.Diagnostics;
using System.Runtime.Versioning;

namespace CodeIndex.Tests;

/// <summary>
/// Regression tests for the published one-liner installer script.
/// 公開ワンライナーインストーラーのリグレッションテスト。
/// </summary>
public sealed class InstallScriptTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"cdidx_install_script_{Guid.NewGuid():N}");

    public InstallScriptTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        TestProjectHelper.DeleteDirectory(_tempRoot);
    }

    [Fact]
    public void Main_WithoutExplicitVersion_SkipsLatestLookupWhenCdidxAlreadyInstalled()
    {
        if (OperatingSystem.IsWindows())
            return;

        var installDir = Path.Combine(_tempRoot, "bin");
        var (exitCode, stdout, stderr) = RunInstallerSnippet(
            $$"""
            detect_platform() { OS_NAME="linux"; ARCH_NAME="x64"; RID="linux-x64"; }
            download_and_install() { echo "DOWNLOAD_SHOULD_NOT_RUN"; }
            check_path() { :; }
            curl() { echo "CURL_SHOULD_NOT_RUN"; return 99; }

            mkdir -p "{{installDir}}"
            cat > "{{Path.Combine(installDir, "cdidx")}}" <<'EOF'
            #!/usr/bin/env bash
            echo "cdidx v1.10.0"
            EOF
            chmod +x "{{Path.Combine(installDir, "cdidx")}}"
            printf '{"version":"1.10.0"}' > "{{Path.Combine(installDir, "version.json")}}"
            : > "{{Path.Combine(installDir, "libe_sqlite3.so")}}"

            main
            """,
            new Dictionary<string, string?>
            {
                ["CDIDX_INSTALL_DIR"] = installDir,
            });

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("cdidx 1.10.0 is already installed", stdout);
        Assert.Contains("Skipping latest-release lookup", stdout);
        Assert.DoesNotContain("Fetching latest release version", stdout);
        Assert.DoesNotContain("DOWNLOAD_SHOULD_NOT_RUN", stdout);
        Assert.DoesNotContain("CURL_SHOULD_NOT_RUN", stdout);
    }

    [Fact]
    public void Main_WithoutExplicitVersion_DoesNotShortCircuitBrokenZeroVersionInstall()
    {
        if (OperatingSystem.IsWindows())
            return;

        var installDir = Path.Combine(_tempRoot, "broken_bin");
        var (exitCode, stdout, stderr) = RunInstallerSnippet(
            $$"""
            detect_platform() { OS_NAME="linux"; ARCH_NAME="x64"; RID="linux-x64"; }
            download_and_install() { echo "DOWNLOAD_RAN"; }
            check_path() { :; }
            curl() {
                local output_path=""
                while [ $# -gt 0 ]; do
                    case "$1" in
                        -o)
                            output_path="$2"
                            shift 2
                            ;;
                        -w)
                            shift 2
                            ;;
                        *)
                            shift
                            ;;
                    esac
                done

                printf '{"tag_name":"v1.2.3"}' > "$output_path"
                printf '200'
                return 0
            }

            mkdir -p "{{installDir}}"
            cat > "{{Path.Combine(installDir, "cdidx")}}" <<'EOF'
            #!/usr/bin/env bash
            echo "cdidx v0.0.0"
            EOF
            chmod +x "{{Path.Combine(installDir, "cdidx")}}"

            main
            """,
            new Dictionary<string, string?>
            {
                ["CDIDX_INSTALL_DIR"] = installDir,
            },
            enforceStrictMode: false);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("Fetching latest release version", stdout);
        Assert.Contains("Version: v1.2.3", stdout);
        Assert.Contains("DOWNLOAD_RAN", stdout);
        Assert.DoesNotContain("Skipping latest-release lookup", stdout);
    }

    [Fact]
    public void ResolveVersion_RateLimitedLatestLookup_PrintsSpecificError()
    {
        if (OperatingSystem.IsWindows())
            return;

        var (exitCode, stdout, stderr) = RunInstallerSnippet(
            """
            curl() {
                local output_path=""
                while [ $# -gt 0 ]; do
                    case "$1" in
                        -o)
                            output_path="$2"
                            shift 2
                            ;;
                        -w)
                            shift 2
                            ;;
                        *)
                            shift
                            ;;
                    esac
                done

                printf '{"message":"API rate limit exceeded for this IP."}' > "$output_path"
                printf '403'
                return 0
            }

            resolve_version ""
            """);

        Assert.Equal(1, exitCode);
        Assert.Contains("Fetching latest release version", stdout);
        Assert.Contains("GitHub API rate limit exceeded while fetching the latest release", stderr);
        Assert.Contains("pass an explicit version", stderr);
        Assert.DoesNotContain("Check your network connection", stderr);
    }

    [Fact]
    public void Main_RateLimitedLatestLookup_StopsBeforeSuccessPath()
    {
        if (OperatingSystem.IsWindows())
            return;

        var (exitCode, stdout, stderr) = RunInstallerSnippet(
            """
            detect_platform() { OS_NAME="linux"; ARCH_NAME="x64"; RID="linux-x64"; }
            download_and_install() { echo "DOWNLOAD_SHOULD_NOT_RUN"; }
            check_path() { :; }
            curl() {
                local output_path=""
                while [ $# -gt 0 ]; do
                    case "$1" in
                        -o)
                            output_path="$2"
                            shift 2
                            ;;
                        -w)
                            shift 2
                            ;;
                        *)
                            shift
                            ;;
                    esac
                done

                printf '{"message":"API rate limit exceeded for this IP."}' > "$output_path"
                printf '403'
                return 0
            }

            main
            """,
            enforceStrictMode: false);

        Assert.Equal(1, exitCode);
        Assert.Contains("Fetching latest release version", stdout);
        Assert.DoesNotContain("Done!", stdout);
        Assert.DoesNotContain("DOWNLOAD_SHOULD_NOT_RUN", stdout);
        Assert.Contains("GitHub API rate limit exceeded while fetching the latest release", stderr);
    }

    [Fact]
    public void Main_NetworkFailureDuringLatestLookup_StopsBeforeSuccessPath()
    {
        if (OperatingSystem.IsWindows())
            return;

        var (exitCode, stdout, stderr) = RunInstallerSnippet(
            """
            detect_platform() { OS_NAME="linux"; ARCH_NAME="x64"; RID="linux-x64"; }
            download_and_install() { echo "DOWNLOAD_SHOULD_NOT_RUN"; }
            check_path() { :; }
            curl() {
                return 7
            }

            main
            """,
            enforceStrictMode: false);

        Assert.Equal(1, exitCode);
        Assert.Contains("Fetching latest release version", stdout);
        Assert.DoesNotContain("Done!", stdout);
        Assert.DoesNotContain("DOWNLOAD_SHOULD_NOT_RUN", stdout);
        Assert.Contains("Network error reaching GitHub while fetching", stderr);
        Assert.Contains("curl exit 7", stderr);
    }

    [Fact]
    public void DownloadReleaseFile_Http404_PrintsHttpSpecificError()
    {
        if (OperatingSystem.IsWindows())
            return;

        var (exitCode, _, stderr) = RunInstallerSnippet(
            $$"""
            VERSION="v0.99.0"
            RID="linux-x64"
            curl() {
                local output_path=""
                while [ $# -gt 0 ]; do
                    case "$1" in
                        -o)
                            output_path="$2"
                            shift 2
                            ;;
                        -w)
                            shift 2
                            ;;
                        *)
                            shift
                            ;;
                    esac
                done

                : > "$output_path"
                printf '404'
                return 0
            }

            download_release_file "https://example.test/releases/download/v0.99.0/CodeIndex-linux-x64.tar.gz" "{{Path.Combine(_tempRoot, "archive.tar.gz")}}" "CodeIndex-linux-x64.tar.gz"
            """);

        Assert.Equal(1, exitCode);
        Assert.Contains("HTTP 404", stderr);
        Assert.Contains("version v0.99.0 exists and publishes linux-x64 assets", stderr);
        Assert.DoesNotContain("Check your network connection", stderr);
    }

    [Fact]
    public void CheckExisting_DifferentVersion_UsesNeutralSwitchingWording()
    {
        if (OperatingSystem.IsWindows())
            return;

        var installDir = Path.Combine(_tempRoot, "bin_switch");
        var (exitCode, stdout, stderr) = RunInstallerSnippet(
            $$"""
            mkdir -p "{{installDir}}"
            cat > "{{Path.Combine(installDir, "cdidx")}}" <<'EOF'
            #!/usr/bin/env bash
            echo "cdidx v1.10.0"
            EOF
            chmod +x "{{Path.Combine(installDir, "cdidx")}}"

            VERSION="v0.99.0"
            detect_existing_install
            check_existing
            """,
            new Dictionary<string, string?>
            {
                ["CDIDX_INSTALL_DIR"] = installDir,
            });

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("Switching cdidx from 1.10.0 to 0.99.0", stdout);
        Assert.DoesNotContain("Upgrading cdidx from 1.10.0 to 0.99.0", stdout);
    }

    [Fact]
    public void ResolveVersion_UsesJqWhenAvailable()
    {
        if (OperatingSystem.IsWindows())
            return;

        var (exitCode, stdout, stderr) = RunInstallerSnippet(
            """
            jq() {
                printf 'v9.9.9\n'
            }

            curl() {
                local output_path=""
                while [ $# -gt 0 ]; do
                    case "$1" in
                        -o)
                            output_path="$2"
                            shift 2
                            ;;
                        -w)
                            shift 2
                            ;;
                        *)
                            shift
                            ;;
                    esac
                done

                printf '{"tag_name":"v1.2.3"}' > "$output_path"
                printf '200'
                return 0
            }

            resolve_version ""
            """);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("Version: v9.9.9", stdout);
    }

    [UnsupportedOSPlatform("windows")]
    private static (int ExitCode, string StdOut, string StdErr) RunInstallerSnippet(string snippet, IReadOnlyDictionary<string, string?>? extraEnvironment = null, bool enforceStrictMode = true)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"cdidx_install_snippet_{Guid.NewGuid():N}.sh");
        try
        {
            File.WriteAllText(scriptPath, $$"""
                #!/usr/bin/env bash
                {{(enforceStrictMode ? "set -euo pipefail" : "")}}
                export CDIDX_INSTALL_SH_LIB_ONLY=1
                source "{{GetInstallScriptPath()}}"
                {{snippet}}
                """);
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                WorkingDirectory = GetRepositoryRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(scriptPath);
            if (extraEnvironment != null)
            {
                foreach (var kvp in extraEnvironment)
                    psi.Environment[kvp.Key] = kvp.Value;
            }

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start bash install snippet / bash install snippet の起動に失敗");
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdOut, stdErr);
        }
        finally
        {
            TestProjectHelper.DeleteFile(scriptPath);
        }
    }

    private static string GetInstallScriptPath() => Path.Combine(GetRepositoryRoot(), "install.sh");

    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CodeIndex.sln")) || File.Exists(Path.Combine(dir.FullName, "install.sh")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root / リポジトリルートを特定できませんでした");
    }
}
