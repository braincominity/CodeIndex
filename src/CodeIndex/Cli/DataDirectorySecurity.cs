using System.Runtime.InteropServices;

namespace CodeIndex.Cli;

internal static class DataDirectorySecurity
{
    internal const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode PermissionBits =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    public static DirectoryInfo CreatePrivateDirectory(string path)
    {
        var directory = Directory.CreateDirectory(path);
        if (IsCdidxDirectory(directory.FullName))
            ApplyPrivateMode(directory.FullName);
        return directory;
    }

    public static void ApplyPrivateMode(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        File.SetUnixFileMode(path, PrivateDirectoryMode);
    }

    public static string? GetUnixModeString(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
            !IsCdidxDirectory(path) ||
            !Directory.Exists(path))
        {
            return null;
        }

        var mode = File.GetUnixFileMode(path) & PermissionBits;
        return Convert.ToString((int)mode, 8).PadLeft(4, '0');
    }

    private static bool IsCdidxDirectory(string path) =>
        string.Equals(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
            ".cdidx",
            StringComparison.Ordinal);
}
