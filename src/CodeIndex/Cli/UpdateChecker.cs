using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CodeIndex.Cli;

internal static class UpdateChecker
{
    internal const string DisableEnvVar = "CDIDX_DISABLE_UPDATE_CHECK";
    private const string LatestReleaseUrl = "https://api.github.com/repos/Widthdom/CodeIndex/releases/latest";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    internal static string? GetNewerReleaseHint(string currentVersion)
        => GetNewerReleaseHint(
            currentVersion,
            ResolveDefaultCachePath(),
            DateTimeOffset.UtcNow,
            FetchLatestReleaseTagAsync);

    internal static string? GetNewerReleaseHint(
        string currentVersion,
        string cachePath,
        DateTimeOffset now,
        Func<CancellationToken, Task<string?>> fetchLatestReleaseTagAsync)
    {
        if (IsDisabled())
            return null;

        var cache = ReadCache(cachePath);
        if (cache is not null
            && now - cache.CheckedAt < CacheTtl
            && cache.LatestTag is not null
            && IsNewerRelease(cache.LatestTag, currentVersion))
        {
            return FormatHint(cache.LatestTag);
        }

        if (cache is not null && now - cache.CheckedAt < CacheTtl)
            return null;

        string? latestTag = null;
        try
        {
            latestTag = fetchLatestReleaseTagAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            latestTag = cache?.LatestTag;
        }

        TryWriteCache(cachePath, new UpdateCheckCache(now, latestTag));
        return IsNewerRelease(latestTag, currentVersion) ? FormatHint(latestTag!) : null;
    }

    internal static bool IsNewerRelease(string? latestTag, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(latestTag) || string.IsNullOrWhiteSpace(currentVersion))
            return false;

        return TryParseVersion(latestTag, out var latest)
            && TryParseVersion(currentVersion, out var current)
            && latest > current;
    }

    private static bool IsDisabled()
    {
        var value = Environment.GetEnvironmentVariable(DisableEnvVar);
        return value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatHint(string latestTag)
        => $"A newer release is available: {latestTag}";

    private static async Task<string?> FetchLatestReleaseTagAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = RequestTimeout };
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("cdidx", ConsoleUi.LoadVersion()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("tag_name", out var tag)
            ? tag.GetString()
            : null;
    }

    private static string ResolveDefaultCachePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Path.Combine(Path.GetTempPath(), "cdidx")
            : Path.Combine(localAppData, "cdidx");
        return Path.Combine(root, "update-check.json");
    }

    private static UpdateCheckCache? ReadCache(string cachePath)
    {
        try
        {
            if (!File.Exists(cachePath))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(cachePath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("checked_at", out var checkedAtElement)
                || !DateTimeOffset.TryParse(
                    checkedAtElement.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var checkedAt))
            {
                return null;
            }

            var latestTag = root.TryGetProperty("latest_tag", out var tagElement)
                ? tagElement.GetString()
                : null;
            return new UpdateCheckCache(checkedAt, latestTag);
        }
        catch
        {
            return null;
        }
    }

    private static void TryWriteCache(string cachePath, UpdateCheckCache cache)
    {
        try
        {
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var payload = new
            {
                checked_at = cache.CheckedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                latest_tag = cache.LatestTag,
            };
            File.WriteAllText(cachePath, JsonSerializer.Serialize(payload));
        }
        catch
        {
        }
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];

        var prereleaseStart = trimmed.IndexOfAny(['-', '+']);
        if (prereleaseStart >= 0)
            trimmed = trimmed[..prereleaseStart];

        return Version.TryParse(trimmed, out version!);
    }

    private sealed record UpdateCheckCache(DateTimeOffset CheckedAt, string? LatestTag);
}
