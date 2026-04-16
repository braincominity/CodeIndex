using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

/// <summary>
/// Scans directories for source files and builds FileRecords.
/// ディレクトリを走査してソースファイルからFileRecordを構築する。
/// </summary>
public class FileIndexer
{
    internal enum FileProbeStatus
    {
        Supported,
        Unsupported,
        ProbeFailed,
    }

    internal readonly record struct LanguageDetectionResult(FileProbeStatus Status, string? Language);

    public readonly record struct ScanError(string Path, string Message);

    public readonly record struct ScanFilesResult(
        IReadOnlyList<string> Files,
        IReadOnlyList<ScanError> Errors,
        IReadOnlyList<string> NonIndexablePaths,
        IReadOnlyList<string> ProbeFailedFilePaths,
        IReadOnlyList<string> ListedDirectories,
        IReadOnlyList<string> FullyScannedDirectories)
    {
        public bool HadErrors => Errors.Count > 0;
    }

    private static readonly string[] HotspotFamilyMarkerLanguages = ["csharp", "vb", "fsharp"];
    private static readonly string[] IgnoreFileNames = [".gitignore", ".cdidxignore"];
    // Extension-to-language mapping / 拡張子→言語名マッピング
    private static readonly Dictionary<string, string> LangMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".py"]     = "python",
        [".js"]     = "javascript",
        [".cjs"]    = "javascript",
        [".mjs"]    = "javascript",
        [".ts"]     = "typescript",
        [".cts"]    = "typescript",
        [".mts"]    = "typescript",
        [".jsx"]    = "javascript",
        [".tsx"]    = "typescript",
        [".rb"]     = "ruby",
        [".go"]     = "go",
        [".rs"]     = "rust",
        [".java"]   = "java",
        [".kt"]     = "kotlin",
        [".swift"]  = "swift",
        [".c"]      = "c",
        [".cpp"]    = "cpp",
        [".cc"]     = "cpp",
        [".cxx"]    = "cpp",
        [".h"]      = "c",       // Could be C or C++; defaults to C for symbol extraction
        [".hpp"]    = "cpp",
        [".hxx"]    = "cpp",
        [".cs"]     = "csharp",
        [".cshtml"] = "csharp",  // Razor (ASP.NET MVC/Pages) / Razor テンプレート
        [".razor"]  = "csharp",  // Blazor component / Blazor コンポーネント
        [".php"]    = "php",
        [".sh"]     = "shell",
        [".sql"]    = "sql",
        [".md"]     = "markdown",
        [".yaml"]   = "yaml",
        [".yml"]    = "yaml",
        [".json"]   = "json",
        [".toml"]   = "toml",
        [".xaml"]   = "xml",    // WPF/MAUI/Avalonia XAML / XAML テンプレート
        [".axaml"]  = "xml",    // Avalonia XAML / Avalonia XAML
        [".csproj"] = "xml",    // C# project file / C# プロジェクトファイル
        [".fsproj"] = "xml",    // F# project file / F# プロジェクトファイル
        [".vbproj"] = "xml",    // VB.NET project file / VB.NET プロジェクトファイル
        [".props"]  = "xml",    // MSBuild props / MSBuild プロパティ
        [".targets"]= "xml",    // MSBuild targets / MSBuild ターゲット
        [".html"]   = "html",
        [".css"]    = "css",
        [".scss"]   = "css",
        [".vue"]    = "vue",
        [".svelte"] = "svelte",
        [".tf"]     = "terraform",
        [".dart"]   = "dart",
        [".scala"]  = "scala",
        [".sc"]     = "scala",
        [".r"]      = "r",
        [".R"]      = "r",
        [".ex"]     = "elixir",
        [".exs"]    = "elixir",
        [".lua"]    = "lua",
        [".fs"]     = "fsharp",
        [".fsx"]    = "fsharp",
        [".fsi"]    = "fsharp",
        [".vb"]     = "vb",
        [".vbs"]    = "vb",
        [".hs"]     = "haskell",
        [".lhs"]    = "haskell",
        [".zig"]    = "zig",
        [".proto"]  = "protobuf",  // Protocol Buffers / Protocol Buffers 定義
        [".graphql"]= "graphql",   // GraphQL schema/queries / GraphQL スキーマ・クエリ
        [".gql"]    = "graphql",
        [".gradle"] = "gradle",    // Gradle build scripts / Gradle ビルドスクリプト
        [".cmake"]  = "cmake",     // CMake scripts / CMake スクリプト
        [".ps1"]    = "powershell",// PowerShell scripts / PowerShell スクリプト
        [".psm1"]   = "powershell",// PowerShell modules / PowerShell モジュール
        [".psd1"]   = "powershell",// PowerShell data files / PowerShell データファイル
        [".bat"]    = "batch",     // Windows batch files / Windows バッチファイル
        [".cmd"]    = "batch",
        [".bash"]   = "shell",
        [".zsh"]    = "shell",
        [".fish"]   = "shell",
    };

    // Exact file names (case-insensitive) mapped to language / 完全一致ファイル名→言語マッピング
    private static readonly Dictionary<string, string> FileNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dockerfile"]    = "dockerfile",
        ["Makefile"]      = "makefile",
        ["Justfile"]      = "justfile",     // Just command runner / Just コマンドランナー
        ["CMakeLists.txt"]= "cmake",
        ["Vagrantfile"]   = "ruby",         // Vagrant uses Ruby DSL / Vagrant は Ruby DSL
        [".editorconfig"] = "editorconfig",
        [".gitignore"]    = "gitignore",
        [".dockerignore"] = "dockerignore",
    };

    // Directories to skip (case-insensitive for cross-platform) / スキップするディレクトリ（クロスプラットフォーム対応で大文字小文字を区別しない）
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg",
        "node_modules", "__pycache__", ".pytest_cache",
        "venv", ".venv", "env",
        "dist", "build", ".build", "out",
        "bin", "obj",                   // .NET build outputs / .NETビルド出力
        "target",                       // Rust/Java/Maven build output / Rust/Java/Mavenビルド出力
        ".gradle",                      // Gradle cache / Gradleキャッシュ
        ".next", ".nuxt",
        ".idea", ".vscode",
        "coverage", "vendor",
        ".terraform",                   // Terraform state/plugin cache / Terraformステート・プラグインキャッシュ
        ".cargo",                       // Cargo registry cache / Cargoレジストリキャッシュ
        ".pub-cache",                   // Dart pub cache / Dart pubキャッシュ
        "_build",                       // Elixir/Mix build output / Elixir/Mixビルド出力
    };

    // Files to skip (case-insensitive for cross-platform consistency with SkipDirs)
    // スキップするファイル名（SkipDirsと同様にクロスプラットフォーム対応で大文字小文字を区別しない）
    private static readonly HashSet<string> SkipFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store", "Thumbs.db",
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml",
        "Gemfile.lock", "Cargo.lock", "composer.lock", "poetry.lock", "bun.lockb",
    };

    // Maximum file size to index (10 MB) / インデックス対象の最大ファイルサイズ (10 MB)
    private const long MaxFileSize = 10 * 1024 * 1024;

    private readonly string _projectRoot;

    private sealed class IgnoreRuleSet
    {
        internal static readonly IgnoreRuleSet Empty = new(null, []);

        private readonly IgnoreRuleSet? _parent;
        private readonly IReadOnlyList<IgnoreRule> _rules;

        private IgnoreRuleSet(IgnoreRuleSet? parent, IReadOnlyList<IgnoreRule> rules)
        {
            _parent = parent;
            _rules = rules;
        }

        internal static IgnoreRuleSet CreateChild(IgnoreRuleSet parent, IReadOnlyList<IgnoreRule> rules)
            => rules.Count == 0 ? parent : new IgnoreRuleSet(parent, rules);

        internal bool IsIgnored(string absolutePath, bool isDirectory)
        {
            var ignored = _parent?.IsIgnored(absolutePath, isDirectory) ?? false;
            foreach (var rule in _rules)
            {
                if (rule.IsMatch(absolutePath, isDirectory))
                    ignored = !rule.Negated;
            }

            return ignored;
        }
    }

    private sealed class IgnoreRule
    {
        private readonly string _sourceDirectory;
        private readonly Regex _matcher;
        private readonly bool _directoryOnly;
        private readonly bool _matchBasenameOnly;

        private IgnoreRule(
            string sourceDirectory,
            Regex matcher,
            bool negated,
            bool directoryOnly,
            bool matchBasenameOnly)
        {
            _sourceDirectory = sourceDirectory;
            _matcher = matcher;
            Negated = negated;
            _directoryOnly = directoryOnly;
            _matchBasenameOnly = matchBasenameOnly;
        }

        internal bool Negated { get; }

        internal static bool TryParse(string sourceDirectory, string rawLine, out IgnoreRule? rule)
        {
            rule = null;
            if (string.IsNullOrWhiteSpace(rawLine))
                return false;

            var pattern = rawLine.TrimEnd();
            if (pattern.Length == 0)
                return false;

            if (pattern[0] == '#' && !pattern.StartsWith(@"\#", StringComparison.Ordinal))
                return false;

            var negated = false;
            if (pattern[0] == '!' && !pattern.StartsWith(@"\!", StringComparison.Ordinal))
            {
                negated = true;
                pattern = pattern[1..];
            }
            else if (pattern.StartsWith(@"\#", StringComparison.Ordinal) ||
                     pattern.StartsWith(@"\!", StringComparison.Ordinal))
            {
                pattern = pattern[1..];
            }

            if (pattern.Length == 0)
                return false;

            var directoryOnly = pattern.EndsWith("/", StringComparison.Ordinal);
            if (directoryOnly)
                pattern = pattern[..^1];

            if (pattern.Length == 0)
                return false;

            var anchoredToSourceDirectory = pattern.StartsWith("/", StringComparison.Ordinal);
            if (anchoredToSourceDirectory)
                pattern = pattern[1..];

            pattern = pattern.Replace(@"\#", "#", StringComparison.Ordinal)
                .Replace(@"\!", "!", StringComparison.Ordinal);

            var matchBasenameOnly = !anchoredToSourceDirectory && !pattern.Contains('/', StringComparison.Ordinal);
            var matcher = BuildMatcher(pattern);
            rule = new IgnoreRule(sourceDirectory, matcher, negated, directoryOnly, matchBasenameOnly);
            return true;
        }

        internal bool IsMatch(string absolutePath, bool isDirectory)
        {
            if (_directoryOnly && !isDirectory)
                return false;

            var relativePath = NormalizeIgnorePath(Path.GetRelativePath(_sourceDirectory, absolutePath));
            if (relativePath.Length == 0 ||
                relativePath == "." ||
                relativePath.StartsWith("../", StringComparison.Ordinal))
            {
                return false;
            }

            var candidate = _matchBasenameOnly
                ? Path.GetFileName(relativePath)
                : relativePath;

            if (string.IsNullOrEmpty(candidate))
                return false;

            return _matcher.IsMatch(candidate);
        }

        private static Regex BuildMatcher(string pattern)
        {
            var builder = new StringBuilder();
            builder.Append('^');

            for (var i = 0; i < pattern.Length; i++)
            {
                var ch = pattern[i];
                if (ch == '*')
                {
                    var isDoubleStar = i + 1 < pattern.Length && pattern[i + 1] == '*';
                    if (isDoubleStar)
                    {
                        var nextChar = i + 2 < pattern.Length ? pattern[i + 2] : '\0';
                        if (nextChar == '/')
                        {
                            builder.Append("(?:[^/]+/)*");
                            i += 2;
                            continue;
                        }

                        builder.Append(".*");
                    }
                    else
                    {
                        builder.Append("[^/]*");
                    }

                    if (isDoubleStar)
                        i++;
                    continue;
                }

                if (ch == '?')
                {
                    builder.Append("[^/]");
                    continue;
                }

                builder.Append(Regex.Escape(ch.ToString()));
            }

            builder.Append('$');
            var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
            if (OperatingSystem.IsWindows())
                options |= RegexOptions.IgnoreCase;
            return new Regex(builder.ToString(), options);
        }
    }

    public FileIndexer(string projectRoot)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    /// <summary>
    /// Return all file patterns (extensions and filenames) mapped to their language names.
    /// 全ファイルパターン（拡張子とファイル名）と対応する言語名のマッピングを返す。
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetLanguageExtensions()
    {
        // Merge extension map and filename map for a complete view
        // 完全な一覧のため拡張子マップとファイル名マップを統合
        var merged = new Dictionary<string, string>(LangMap, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, lang) in FileNameMap)
            merged.TryAdd(name, lang);
        return merged;
    }

    public static string? DetectLanguage(string filePath)
        => TryDetectLanguage(filePath).Language;

    internal static LanguageDetectionResult TryDetectLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (LangMap.TryGetValue(ext, out var lang))
            return new LanguageDetectionResult(FileProbeStatus.Supported, lang);

        // Fall back to exact file name matching / ファイル名の完全一致で言語を検出
        var fileName = Path.GetFileName(filePath);
        if (FileNameMap.TryGetValue(fileName, out var nameLang))
            return new LanguageDetectionResult(FileProbeStatus.Supported, nameLang);

        if (!string.IsNullOrEmpty(ext))
            return new LanguageDetectionResult(FileProbeStatus.Unsupported, null);

        return TryDetectLanguageFromShebang(filePath);
    }

    internal static bool CanIndexFile(string filePath)
        => GetFileIndexability(filePath) == FileProbeStatus.Supported;

    internal static FileProbeStatus GetFileIndexability(string filePath)
    {
        if (OperatingSystem.IsWindows())
            return FileProbeStatus.Supported;

        if (!UnixFileStatus.TryGetFileMode(filePath, out var mode))
            return FileProbeStatus.ProbeFailed;

        return (mode & UnixFileStatus.FileTypeMask) == UnixFileStatus.RegularFile
            ? FileProbeStatus.Supported
            : FileProbeStatus.Unsupported;
    }

    public string GetFamilyScopeKey(string absolutePath, string? lang)
    {
        var projectMarkerPattern = GetProjectMarkerPattern(lang);
        if (projectMarkerPattern != null)
        {
            var currentDir = Path.GetDirectoryName(Path.GetFullPath(absolutePath));
            while (!string.IsNullOrEmpty(currentDir))
            {
                var markerCount = Directory.EnumerateFiles(currentDir, projectMarkerPattern, SearchOption.TopDirectoryOnly).Take(2).Count();
                if (markerCount == 1)
                    return NormalizeScopeKey(Path.GetRelativePath(_projectRoot, currentDir));
                if (markerCount > 1)
                    return DeriveAmbiguousProjectScopeKey(Path.GetFullPath(absolutePath), currentDir);

                if (PathsEqual(currentDir, _projectRoot))
                    break;

                currentDir = Path.GetDirectoryName(currentDir);
            }
        }

        var relativePath = Path.GetRelativePath(_projectRoot, absolutePath);
        return DeriveFallbackFamilyScopeKey(relativePath);
    }

    public static IReadOnlyList<string> GetHotspotFamilyMarkerLanguages() => HotspotFamilyMarkerLanguages;

    public static bool SupportsHotspotFamilyMarkerLanguage(string? lang) =>
        GetProjectMarkerPattern(lang) != null;

    public string? GetProjectMarkerFingerprint(string? lang)
    {
        var projectMarkerPattern = GetProjectMarkerPattern(lang);
        if (projectMarkerPattern == null)
            return null;

        var projectMarkers = new List<string>();
        CollectProjectMarkerFiles(_projectRoot, projectMarkerPattern, projectMarkers);
        projectMarkers.Sort(StringComparer.Ordinal);

        var payload = string.Join('\n', projectMarkers);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static string DeriveFallbackFamilyScopeKey(string relativePath)
    {
        var normalized = NormalizeScopeKey(relativePath);
        if (normalized == ".")
            return ".";

        var firstSeparator = normalized.IndexOf('/');
        if (firstSeparator < 0)
            return ".";

        return normalized[..firstSeparator];
    }

    private static string NormalizeScopeKey(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(normalized) || normalized == "."
            ? "."
            : normalized;
    }

    private string DeriveAmbiguousProjectScopeKey(string absolutePath, string anchorDir)
    {
        var anchorScope = NormalizeScopeKey(Path.GetRelativePath(_projectRoot, anchorDir));
        var relativeFromAnchor = NormalizeScopeKey(Path.GetRelativePath(anchorDir, absolutePath));
        if (relativeFromAnchor == ".")
            return anchorScope;

        var firstSeparator = relativeFromAnchor.IndexOf('/');
        if (firstSeparator < 0)
            return JoinScope(anchorScope, $"__file__/{relativeFromAnchor}");

        return JoinScope(anchorScope, relativeFromAnchor[..firstSeparator]);
    }

    private static string JoinScope(string left, string right)
    {
        if (left == ".")
            return right;

        return $"{left}/{right}";
    }

    private void CollectProjectMarkerFiles(string dir, string pattern, List<string> projectMarkers)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly))
                projectMarkers.Add(NormalizeScopeKey(Path.GetRelativePath(_projectRoot, file)));

            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                if (SkipDirs.Contains(Path.GetFileName(subDir)))
                    continue;

                CollectProjectMarkerFiles(subDir, pattern, projectMarkers);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort like ScanFiles(): unreadable directories do not abort the whole index.
            // ScanFiles() と同じく best-effort。読めないディレクトリでは index 全体を落とさない。
        }
        catch (IOException)
        {
            // Best-effort like ScanFiles(): transient IO failures should only skip that subtree.
            // ScanFiles() と同じく best-effort。IO 失敗はその subtree だけスキップする。
        }
    }

    private static string? GetProjectMarkerPattern(string? lang) => lang switch
    {
        "csharp" => "*.csproj",
        "vb" => "*.vbproj",
        "fsharp" => "*.fsproj",
        _ => null,
    };

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Enumerate all indexable files under the project root.
    /// プロジェクトルート以下のインデックス対象ファイルを列挙する。
    /// </summary>
    public IReadOnlyList<string> ScanFiles()
        => ScanFilesDetailed().Files;

    internal ScanFilesResult ScanFilesDetailed()
    {
        var files = new List<string>();
        var errors = new List<ScanError>();
        var nonIndexablePaths = new HashSet<string>(StringComparer.Ordinal);
        var probeFailedFilePaths = new HashSet<string>(StringComparer.Ordinal);
        var listedDirectories = new HashSet<string>(StringComparer.Ordinal);
        var fullyScannedDirectories = new HashSet<string>(StringComparer.Ordinal);
        EnumerateDirectory(_projectRoot, files, errors, nonIndexablePaths, probeFailedFilePaths, listedDirectories, fullyScannedDirectories, IgnoreRuleSet.Empty);
        return new ScanFilesResult(
            files,
            errors,
            nonIndexablePaths.ToList(),
            probeFailedFilePaths.ToList(),
            listedDirectories.ToList(),
            fullyScannedDirectories.ToList());
    }

    private bool ScanDirectory(
        string dir,
        List<string> results,
        List<ScanError> errors,
        HashSet<string> nonIndexablePaths,
        HashSet<string> probeFailedFilePaths,
        HashSet<string> listedDirectories,
        HashSet<string> fullyScannedDirectories,
        IgnoreRuleSet activeIgnoreRules)
    {
        var relativeDir = ToRelativePath(dir);

        // Check for skip directories / スキップ対象ディレクトリかチェック
        var dirName = Path.GetFileName(dir);
        if (SkipDirs.Contains(dirName) || activeIgnoreRules.IsIgnored(dir, isDirectory: true))
        {
            listedDirectories.Add(relativeDir);
            fullyScannedDirectories.Add(relativeDir);
            return true;
        }

        return EnumerateDirectory(dir, results, errors, nonIndexablePaths, probeFailedFilePaths, listedDirectories, fullyScannedDirectories, activeIgnoreRules);
    }

    private bool EnumerateDirectory(
        string dir,
        List<string> results,
        List<ScanError> errors,
        HashSet<string> nonIndexablePaths,
        HashSet<string> probeFailedFilePaths,
        HashSet<string> listedDirectories,
        HashSet<string> fullyScannedDirectories,
        IgnoreRuleSet inheritedIgnoreRules)
    {
        var fullyScanned = true;
        try
        {
            var activeIgnoreRules = LoadIgnoreRulesForDirectory(dir, inheritedIgnoreRules, errors, ref fullyScanned);

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var fileName = Path.GetFileName(file);

                // Skip excluded file names / 除外ファイル名をスキップ
                if (SkipFiles.Contains(fileName))
                    continue;

                if (activeIgnoreRules.IsIgnored(file, isDirectory: false))
                    continue;

                // Only regular files are indexable on Unix. This avoids blocking on FIFOs/sockets/devices.
                // Unix では通常ファイルのみをインデックス対象にする。FIFO/socket/device でのブロックを避ける。
                var indexability = GetFileIndexability(file);
                if (indexability == FileProbeStatus.ProbeFailed)
                {
                    var relativePath = ToRelativePath(file);
                    errors.Add(new ScanError(relativePath, "Could not probe file for indexability/language."));
                    probeFailedFilePaths.Add(relativePath);
                    continue;
                }

                if (indexability != FileProbeStatus.Supported)
                {
                    nonIndexablePaths.Add(ToRelativePath(file));
                    continue;
                }

                // Include files with a known extension/filename or an extensionless recognized shebang
                // 既知の拡張子・既知ファイル名、または拡張子なしで shebang を認識できるファイルを含める
                var language = TryDetectLanguage(file);
                if (language.Status == FileProbeStatus.ProbeFailed)
                {
                    var relativePath = ToRelativePath(file);
                    errors.Add(new ScanError(relativePath, "Could not probe file for indexability/language."));
                    probeFailedFilePaths.Add(relativePath);
                    continue;
                }

                if (language.Status == FileProbeStatus.Supported)
                    results.Add(file);
                else
                    nonIndexablePaths.Add(ToRelativePath(file));
            }

            // A successful file listing proves the direct children of this directory.
            // Child subtree failures must not revoke that authority for sibling-file purge.
            // ファイル列挙が成功した時点で、このディレクトリ直下の子要素については authoritative とみなせる。
            // 子サブツリー失敗が sibling file purge の authority を奪ってはいけない。
            listedDirectories.Add(ToRelativePath(dir));

            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                fullyScanned &= ScanDirectory(subDir, results, errors, nonIndexablePaths, probeFailedFilePaths, listedDirectories, fullyScannedDirectories, activeIgnoreRules);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip inaccessible directories / アクセス不可ディレクトリはスキップ
            errors.Add(new ScanError(ToRelativePath(dir), "Could not scan directory due to permissions."));
            fullyScanned = false;
        }
        catch (IOException)
        {
            // Skip on I/O errors / I/Oエラー時はスキップ
            errors.Add(new ScanError(ToRelativePath(dir), "Could not scan directory due to an I/O error."));
            fullyScanned = false;
        }

        if (fullyScanned)
            fullyScannedDirectories.Add(ToRelativePath(dir));

        return fullyScanned;
    }

    private IgnoreRuleSet LoadIgnoreRulesForDirectory(
        string dir,
        IgnoreRuleSet inheritedIgnoreRules,
        List<ScanError> errors,
        ref bool fullyScanned)
    {
        var rules = new List<IgnoreRule>();

        foreach (var ignoreFileName in IgnoreFileNames)
        {
            var ignorePath = Path.Combine(dir, ignoreFileName);
            if (!File.Exists(ignorePath))
                continue;

            try
            {
                foreach (var line in File.ReadLines(ignorePath))
                {
                    if (IgnoreRule.TryParse(dir, line, out var rule) && rule != null)
                        rules.Add(rule);
                }
            }
            catch (UnauthorizedAccessException)
            {
                errors.Add(new ScanError(ToRelativePath(ignorePath), $"Could not read {ignoreFileName}."));
                fullyScanned = false;
            }
            catch (IOException)
            {
                errors.Add(new ScanError(ToRelativePath(ignorePath), $"Could not read {ignoreFileName}."));
                fullyScanned = false;
            }
        }

        return IgnoreRuleSet.CreateChild(inheritedIgnoreRules, rules);
    }

    private static string NormalizeIgnorePath(string path)
        => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>
    /// Build a FileRecord and return file content (avoids reading the file twice).
    /// FileRecordを構築しファイル内容も返す（二重読み込み防止）。
    /// </summary>
    public (FileRecord record, string content, string? warning) BuildRecord(string absolutePath)
    {
        var (record, content, _, warning) = BuildRecordWithRawBytes(absolutePath);
        return (record, content, warning);
    }

    /// <summary>
    /// Build a FileRecord and return both decoded content and raw bytes.
    /// Callers can run encoding validation without a second file read.
    /// FileRecordを構築し、デコード済み内容とraw bytesを返す。
    /// 呼び出し側は再読込なしでエンコーディング検証できる。
    /// </summary>
    public (FileRecord record, string content, byte[] rawBytes, string? warning) BuildRecordWithRawBytes(string absolutePath)
    {
        var indexability = GetFileIndexability(absolutePath);
        if (indexability != FileProbeStatus.Supported)
            throw new InvalidOperationException("Only regular files can be indexed");

        var relativePath = Path.GetRelativePath(_projectRoot, absolutePath);
        var info = new FileInfo(absolutePath);

        // Skip files exceeding size limit to avoid OutOfMemoryException
        // OOM防止のためサイズ上限を超えるファイルをスキップ
        if (info.Length > MaxFileSize)
            throw new InvalidOperationException($"File too large ({info.Length / 1024 / 1024} MB > {MaxFileSize / 1024 / 1024} MB limit)");

        // Read raw bytes and decode UTF-8; detect invalid sequences
        // 生バイト読み込み後UTF-8デコード、不正シーケンスを検出
        var bytes = File.ReadAllBytes(absolutePath);

        // Compute checksum from raw bytes to avoid re-encoding the string (~10MB saved for large files)
        // 文字列の再エンコードを回避するためraw bytesからチェックサムを算出（大ファイルで約10MB節約）
        var checksum = ComputeChecksum(bytes);

        string content;
        string? warning = null;
        try
        {
            content = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Fall back to replacement mode but warn / 置換モードにフォールバックし警告
            content = new UTF8Encoding(false, throwOnInvalidBytes: false).GetString(bytes);
            warning = $"{relativePath}: contains invalid UTF-8 bytes (replaced with U+FFFD)";
        }
        // Normalize line endings to LF / 改行をLFに正規化
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");
        // Accurate line count: ignore trailing newline / 正確な行数: 末尾改行を無視
        var lines = content.EndsWith('\n')
            ? content[..^1].Split('\n')
            : content.Split('\n');

        var record = new FileRecord
        {
            Path = relativePath.Replace('\\', '/'),
            Lang = TryDetectLanguage(absolutePath).Language,
            Size = info.Length,
            Lines = lines.Length,
            Checksum = checksum,
            Modified = info.LastWriteTimeUtc,
        };

        return (record, content, bytes, warning);
    }

    /// <summary>
    /// Validate file content for encoding issues.
    /// ファイル内容のエンコーディング問題を検証する。
    /// </summary>
    public static List<FileIssue> ValidateContent(string relativePath, byte[] rawBytes, string content)
    {
        var issues = new List<FileIssue>();

        // U+FFFD replacement characters baked into the file / ファイルに焼き付いたU+FFFD置換文字
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\uFFFD')
            {
                // Find line number / 行番号を特定
                var lineNum = content[..i].Count(c => c == '\n') + 1;
                issues.Add(new FileIssue
                {
                    Path = relativePath,
                    Kind = "replacement_char",
                    Line = lineNum,
                    Message = $"U+FFFD replacement character at line {lineNum}",
                });
                // Skip to next line to avoid reporting every char on the same line
                // 同じ行の連続報告を避けるため次の行までスキップ
                var nextNewline = content.IndexOf('\n', i);
                if (nextNewline >= 0) i = nextNewline;
            }
        }

        // BOM marker / BOMマーカー
        if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
        {
            issues.Add(new FileIssue
            {
                Path = relativePath,
                Kind = "bom",
                Line = 1,
                Message = "UTF-8 BOM marker detected",
            });
        }

        // NULL bytes (likely binary content) / NULLバイト（バイナリ混入の可能性）
        if (rawBytes.Any(b => b == 0))
        {
            issues.Add(new FileIssue
            {
                Path = relativePath,
                Kind = "null_byte",
                Line = 0,
                Message = "File contains NULL bytes (possible binary content)",
            });
        }

        // Mixed line endings — check raw bytes before LF normalization
        // 混在改行コード — LF正規化前のrawBytesで確認
        var hasCrlf = false;
        var hasLfOnly = false;
        for (int i = 0; i < rawBytes.Length; i++)
        {
            if (rawBytes[i] == 0x0D && i + 1 < rawBytes.Length && rawBytes[i + 1] == 0x0A)
            {
                hasCrlf = true;
                i++; // skip the LF after CR
            }
            else if (rawBytes[i] == 0x0A)
            {
                hasLfOnly = true;
            }
        }
        if (hasCrlf && hasLfOnly)
        {
            issues.Add(new FileIssue
            {
                Path = relativePath,
                Kind = "mixed_line_endings",
                Line = 0,
                Message = "Mixed line endings (CRLF and LF)",
            });
        }

        return issues;
    }

    /// <summary>
    /// Compute SHA256 checksum from raw file bytes.
    /// ファイルのraw bytesからSHA256チェックサムを算出する。
    /// </summary>
    private static string ComputeChecksum(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Try to infer a language from an extensionless script shebang.
    /// This is a cheap fallback used only after extension and exact-filename checks fail.
    /// 拡張子・完全一致ファイル名で判定できない場合だけ、拡張子なしスクリプトの shebang から言語を推定する。
    /// </summary>
    private static LanguageDetectionResult TryDetectLanguageFromShebang(string filePath)
    {
        if (GetFileIndexability(filePath) != FileProbeStatus.Supported)
            return new LanguageDetectionResult(FileProbeStatus.Unsupported, null);

        try
        {
            using var stream = File.OpenRead(filePath);
            if (!stream.CanRead)
                return new LanguageDetectionResult(FileProbeStatus.ProbeFailed, null);

            Span<byte> buffer = stackalloc byte[256];
            var bytesRead = stream.Read(buffer);
            if (bytesRead <= 0)
                return new LanguageDetectionResult(FileProbeStatus.Unsupported, null);

            var firstLine = Encoding.UTF8.GetString(buffer[..bytesRead])
                .Split(['\r', '\n'], 2)[0];

            if (firstLine.StartsWith('\uFEFF'))
                firstLine = firstLine[1..];

            if (!firstLine.StartsWith("#!", StringComparison.Ordinal))
                return new LanguageDetectionResult(FileProbeStatus.Unsupported, null);

            var commandLine = firstLine[2..].Trim();
            if (string.IsNullOrWhiteSpace(commandLine))
                return new LanguageDetectionResult(FileProbeStatus.Unsupported, null);

            var tokens = commandLine
                .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
                return new LanguageDetectionResult(FileProbeStatus.Unsupported, null);

            var interpreter = ResolveShebangInterpreter(tokens);
            if (interpreter == null)
                return new LanguageDetectionResult(FileProbeStatus.Unsupported, null);

            var language = MapShebangInterpreterToLanguage(interpreter);
            return language != null
                ? new LanguageDetectionResult(FileProbeStatus.Supported, language)
                : new LanguageDetectionResult(FileProbeStatus.Unsupported, null);
        }
        catch (IOException)
        {
            return new LanguageDetectionResult(FileProbeStatus.ProbeFailed, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new LanguageDetectionResult(FileProbeStatus.ProbeFailed, null);
        }
    }

    private static string? ResolveShebangInterpreter(IReadOnlyList<string> tokens)
    {
        var interpreter = Path.GetFileName(tokens[0]).ToLowerInvariant();
        if (interpreter is not "env")
            return interpreter;

        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.StartsWith("-", StringComparison.Ordinal))
                continue;

            // `env FOO=bar python` style assignments before the real interpreter.
            // `env FOO=bar python` のような代入はスキップして本体の interpreter を探す。
            if (token.Contains('='))
                continue;

            return Path.GetFileName(token).ToLowerInvariant();
        }

        return null;
    }

    private static string? MapShebangInterpreterToLanguage(string interpreter) => interpreter switch
    {
        "bash" or "sh" or "zsh" or "fish" or "dash" or "ksh" or "ash" => "shell",
        "node" or "nodejs" => "javascript",
        "ruby" => "ruby",
        "php" => "php",
        "lua" => "lua",
        "pwsh" or "powershell" => "powershell",
        _ when interpreter.StartsWith("python", StringComparison.Ordinal) => "python",
        _ => null,
    };

    private string ToRelativePath(string absolutePath)
    {
        var relativePath = Path.GetRelativePath(_projectRoot, absolutePath).Replace('\\', '/');
        return relativePath == "." ? string.Empty : relativePath;
    }

    private static class UnixFileStatus
    {
        internal const int FileTypeMask = 0xF000;
        internal const int RegularFile = 0x8000;

        internal static bool TryGetFileMode(string filePath, out int mode)
        {
            mode = 0;
            if (NativeMethods.Stat(filePath, out var status) != 0)
                return false;

            mode = status.Mode;
            return true;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct FileStatus
        {
            internal FileStatusFlags Flags;
            internal int Mode;
            internal uint Uid;
            internal uint Gid;
            internal long Size;
            internal long ATime;
            internal long ATimeNsec;
            internal long MTime;
            internal long MTimeNsec;
            internal long CTime;
            internal long CTimeNsec;
            internal long BirthTime;
            internal long BirthTimeNsec;
            internal long Dev;
            internal long RDev;
            internal long Ino;
            internal uint UserFlags;
        }

        [System.Flags]
        private enum FileStatusFlags : uint
        {
            None = 0,
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("libSystem.Native", EntryPoint = "SystemNative_Stat", CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
            internal static extern int Stat(string path, out FileStatus output);
        }
    }
}
