using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeIndex.Models;

namespace CodeIndex.Cli;

/// <summary>
/// Reads and writes improvement suggestions to .cdidx/suggestions.json.
/// Provides deduplication via SHA256 hash of (category + language + normalized description).
/// 改善提案を .cdidx/suggestions.json に読み書きする。
/// (category + language + 正規化済み description) のSHA256ハッシュで重複排除する。
/// </summary>
public class SuggestionStore
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Create a new SuggestionStore for the given .cdidx directory.
    /// 指定した .cdidx ディレクトリ用の SuggestionStore を作成する。
    /// </summary>
    /// <param name="cdidxDir">Path to the .cdidx directory / .cdidxディレクトリのパス</param>
    public SuggestionStore(string cdidxDir)
    {
        _filePath = Path.Combine(cdidxDir, "suggestions.json");
    }

    /// <summary>
    /// Compute the dedup hash for a suggestion.
    /// The hash is derived from category, language (lowered), and description (trimmed + lowered).
    /// This ensures that trivially different phrasings (e.g. different casing) produce the same hash.
    /// 提案の重複排除用ハッシュを計算する。
    /// category、language（小文字化）、description（trim + 小文字化）から導出する。
    /// 些細な表現差（大小文字等）で同じハッシュが生成されるようにする。
    /// </summary>
    public static string ComputeHash(string category, string? language, string description)
    {
        // Normalize: category is already constrained to enum values,
        // language is lowered, description is trimmed and lowered.
        // 正規化: category は enum 値に制約済み、
        // language は小文字化、description は trim + 小文字化。
        var normalized = $"{category}|{(language ?? "").ToLowerInvariant()}|{description.Trim().ToLowerInvariant()}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Try to add a suggestion. Returns false if a suggestion with the same hash already exists.
    /// 提案の追加を試みる。同一ハッシュの提案が既にあれば false を返す。
    /// </summary>
    public bool TryAdd(SuggestionRecord record)
    {
        var existing = LoadAll();
        if (existing.Any(s => s.Hash == record.Hash))
            return false;

        existing.Add(record);
        Save(existing);
        return true;
    }

    /// <summary>
    /// Load all suggestions from disk. Returns an empty list if the file
    /// does not exist, is empty, or contains invalid JSON.
    /// ディスクから全提案を読み込む。ファイルが存在しない、空、
    /// または不正なJSONの場合は空リストを返す。
    /// </summary>
    public List<SuggestionRecord> LoadAll()
    {
        if (!File.Exists(_filePath))
            return new List<SuggestionRecord>();

        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new List<SuggestionRecord>();

            return JsonSerializer.Deserialize<List<SuggestionRecord>>(json, s_readOptions)
                   ?? new List<SuggestionRecord>();
        }
        catch (JsonException)
        {
            // Corrupt file — treat as empty / 壊れたファイル — 空として扱う
            return new List<SuggestionRecord>();
        }
        catch (IOException)
        {
            // File access error — treat as empty / ファイルアクセスエラー — 空として扱う
            return new List<SuggestionRecord>();
        }
    }

    /// <summary>
    /// Mark a suggestion as submitted to GitHub by updating its URL and flag.
    /// 提案を GitHub 送信済みとしてマークし、URL とフラグを更新する。
    /// </summary>
    public void MarkSubmitted(string hash, string issueUrl)
    {
        var all = LoadAll();
        var record = all.FirstOrDefault(s => s.Hash == hash);
        if (record == null)
            return;

        record.SubmittedToGitHub = true;
        record.GitHubIssueUrl = issueUrl;
        Save(all);
    }

    /// <summary>
    /// Write the full suggestion list to disk, creating the directory if needed.
    /// 提案リスト全体をディスクに書き込む。必要に応じてディレクトリを作成する。
    /// </summary>
    private void Save(List<SuggestionRecord> records)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(records, s_jsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
