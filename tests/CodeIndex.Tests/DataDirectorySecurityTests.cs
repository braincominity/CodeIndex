using System.Runtime.InteropServices;
using CodeIndex.Cli;

namespace CodeIndex.Tests;

public class DataDirectorySecurityTests
{
    [Fact]
    public void CreatePrivateDirectory_OnPosix_Forces0700Mode()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var root = Path.Combine(Path.GetTempPath(), $"cdidx_data_dir_security_{Guid.NewGuid():N}");
        var cdidxDir = Path.Combine(root, ".cdidx");
        try
        {
            DataDirectorySecurity.CreatePrivateDirectory(cdidxDir);

            Assert.Equal("0700", DataDirectorySecurity.GetUnixModeString(cdidxDir));
            Assert.Equal(
                DataDirectorySecurity.PrivateDirectoryMode,
                File.GetUnixFileMode(cdidxDir) & DataDirectorySecurity.PrivateDirectoryMode);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetUnixModeString_OnMissingDirectory_ReturnsNull()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"cdidx_missing_data_dir_{Guid.NewGuid():N}");

        Assert.Null(DataDirectorySecurity.GetUnixModeString(missing));
    }
}
