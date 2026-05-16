using CodeIndex.Indexer;

namespace CodeIndex.Tests;

// Regression lock for #1547: FileIndexer must wrap absolute paths approaching
// MAX_PATH(260) with the Windows extended-length prefix (\\?\ or \\?\UNC\)
// before passing them to Directory.Enumerate* / File.* APIs, so deep monorepo
// paths (node_modules/.pnpm/<pkg>@<ver>/node_modules/...) are not silently
// skipped on Windows. The internal *Core overloads accept an explicit isWindows
// flag so this regression lock runs on POSIX CI without depending on the host.
// #1547 の回帰ロック: FileIndexer が Directory.Enumerate* / File.* に渡す絶対パスは
// MAX_PATH(260) に近づいたら Windows 拡張長接頭辞 (\\?\ または \\?\UNC\) を付けて
// 渡し、深い monorepo パス (node_modules/.pnpm/<pkg>@<ver>/node_modules/...) が
// Windows で silent skip されないことを保証する。内部 *Core オーバーロードは
// isWindows を明示的に取るので、POSIX CI でもホストに依存せず検証できる。
public class LongPathTests
{
    [Fact]
    public void EnsureWindowsPrefixCore_NotWindows_ReturnsInputUnchanged()
    {
        var deep = "/" + new string('a', 400);
        Assert.Equal(deep, LongPath.EnsureWindowsPrefixCore(deep, isWindows: false));
    }

    [Fact]
    public void EnsureWindowsPrefixCore_NullOrEmpty_ReturnsInputUnchanged()
    {
        Assert.Equal("", LongPath.EnsureWindowsPrefixCore("", isWindows: true));
    }

    [Fact]
    public void EnsureWindowsPrefixCore_ShortDriveAbsolutePath_ReturnsInputUnchanged()
    {
        // Anything below 248 chars stays as-is so Path.GetRelativePath / GetFullPath
        // round-trips keep working in the common short-path case.
        // 248 文字未満は元のまま返し、Path.GetRelativePath などの round-trip を保つ。
        var path = @"C:\repo\src\app.cs";
        Assert.Equal(path, LongPath.EnsureWindowsPrefixCore(path, isWindows: true));
    }

    [Fact]
    public void EnsureWindowsPrefixCore_LongDriveAbsolutePath_PrependsExtendedLengthPrefix()
    {
        var deep = @"C:\repo\" + new string('a', 300);
        var prefixed = LongPath.EnsureWindowsPrefixCore(deep, isWindows: true);

        Assert.StartsWith(@"\\?\", prefixed);
        Assert.Equal(@"\\?\" + deep, prefixed);
    }

    [Fact]
    public void EnsureWindowsPrefixCore_LongUncPath_PrependsExtendedUncPrefix()
    {
        // UNC paths take the \\?\UNC\ form (server\share\... after the prefix), not \\?\\\.
        // UNC パスは \\?\\\ ではなく \\?\UNC\<server>\<share>\... を要求する。
        var deep = @"\\server\share\" + new string('a', 300);
        var prefixed = LongPath.EnsureWindowsPrefixCore(deep, isWindows: true);

        Assert.StartsWith(@"\\?\UNC\", prefixed);
        Assert.Equal(@"\\?\UNC\server\share\" + new string('a', 300), prefixed);
    }

    [Fact]
    public void EnsureWindowsPrefixCore_AlreadyExtendedLengthPrefix_LeftAlone()
    {
        var alreadyPrefixed = @"\\?\C:\repo\" + new string('a', 300);
        Assert.Equal(alreadyPrefixed, LongPath.EnsureWindowsPrefixCore(alreadyPrefixed, isWindows: true));
    }

    [Fact]
    public void EnsureWindowsPrefixCore_AlreadyExtendedUncPrefix_LeftAlone()
    {
        var alreadyPrefixed = @"\\?\UNC\server\share\" + new string('a', 300);
        Assert.Equal(alreadyPrefixed, LongPath.EnsureWindowsPrefixCore(alreadyPrefixed, isWindows: true));
    }

    [Fact]
    public void EnsureWindowsPrefixCore_DeviceNamespacePath_LeftAlone()
    {
        var devicePath = @"\\.\PhysicalDrive0\" + new string('a', 300);
        Assert.Equal(devicePath, LongPath.EnsureWindowsPrefixCore(devicePath, isWindows: true));
    }

    [Fact]
    public void EnsureWindowsPrefixCore_RelativePath_NotPrefixed()
    {
        // Prefixing a relative path with \\?\ would resolve to the wrong place since
        // \\?\ disables normalization (no .. resolution, no current-directory join).
        // 相対パスに \\?\ を付けると normalization が無効になり .. やカレントディレクトリ結合が
        // 効かなくなり別の場所に解決されるため、相対パスはそのまま返す。
        var relative = "src\\" + new string('a', 300);
        Assert.Equal(relative, LongPath.EnsureWindowsPrefixCore(relative, isWindows: true));
    }

    [Fact]
    public void EnsureWindowsPrefixCore_PosixAbsolutePath_NotPrefixed()
    {
        // POSIX-style paths are not Windows absolute paths and should be left alone
        // even when isWindows=true, so cross-platform fixtures don't get mis-prefixed.
        // POSIX 形式の絶対パスは Windows 絶対パスではないため、isWindows=true でも触らない。
        var posix = "/usr/local/" + new string('a', 300);
        Assert.Equal(posix, LongPath.EnsureWindowsPrefixCore(posix, isWindows: true));
    }

    [Fact]
    public void RemoveWindowsPrefixCore_NotWindows_ReturnsInputUnchanged()
    {
        var prefixed = @"\\?\C:\repo\app.cs";
        Assert.Equal(prefixed, LongPath.RemoveWindowsPrefixCore(prefixed, isWindows: false));
    }

    [Fact]
    public void RemoveWindowsPrefixCore_StripsExtendedLengthPrefix()
    {
        Assert.Equal(@"C:\repo\app.cs",
            LongPath.RemoveWindowsPrefixCore(@"\\?\C:\repo\app.cs", isWindows: true));
    }

    [Fact]
    public void RemoveWindowsPrefixCore_StripsExtendedUncPrefix()
    {
        Assert.Equal(@"\\server\share\file.cs",
            LongPath.RemoveWindowsPrefixCore(@"\\?\UNC\server\share\file.cs", isWindows: true));
    }

    [Fact]
    public void RemoveWindowsPrefixCore_NoPrefix_ReturnsInputUnchanged()
    {
        Assert.Equal(@"C:\repo\app.cs",
            LongPath.RemoveWindowsPrefixCore(@"C:\repo\app.cs", isWindows: true));
    }
}
