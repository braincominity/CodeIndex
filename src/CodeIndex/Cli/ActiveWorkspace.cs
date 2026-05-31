using System.Text.Json;
using CodeIndex.Indexer;

namespace CodeIndex.Cli;

internal sealed record ActiveWorkspaceState(string Name, string Root, string DbPath);

internal static class ActiveWorkspace
{
    internal const string EnvironmentVariable = "CDIDX_ACTIVE_WORKSPACE";

    internal static string StatePath
    {
        get
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var root = string.IsNullOrWhiteSpace(configHome)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : configHome;
            return Path.Combine(root, "cdidx", "active.json");
        }
    }

    internal static ActiveWorkspaceState? Load()
    {
        var envPath = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(envPath))
            return new ActiveWorkspaceState("env", Path.GetDirectoryName(Path.GetFullPath(envPath)) ?? Environment.CurrentDirectory, Path.GetFullPath(envPath));

        var path = StatePath;
        if (!File.Exists(LongPath.EnsureWindowsPrefix(path)))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var name = ReadString(root, "name") ?? "default";
        var workspaceRoot = ReadString(root, "root") ?? Environment.CurrentDirectory;
        var dbPath = ReadString(root, "db_path");
        if (string.IsNullOrWhiteSpace(dbPath))
            return null;
        return new ActiveWorkspaceState(name, Path.GetFullPath(workspaceRoot), Path.GetFullPath(dbPath));
    }

    internal static void Save(ActiveWorkspaceState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        var payload = new Dictionary<string, string>
        {
            ["name"] = state.Name,
            ["root"] = Path.GetFullPath(state.Root),
            ["db_path"] = Path.GetFullPath(state.DbPath),
        };
        File.WriteAllText(StatePath, JsonSerializer.Serialize(payload, ProgramRunner.CreateDefaultJsonOptions()));
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
