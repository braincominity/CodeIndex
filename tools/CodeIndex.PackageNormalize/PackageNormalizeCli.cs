using System.IO.Compression;
using System.Text;

namespace CodeIndex.PackageNormalize;

public static class PackageNormalizeCli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args.Any(arg => arg is "-h" or "--help"))
        {
            Console.Error.WriteLine("Usage: dotnet run --project tools/CodeIndex.PackageNormalize -- <package.nupkg|package.snupkg> [...]");
            return args.Length == 0 ? 1 : 0;
        }

        foreach (var packagePath in args)
        {
            PackageCorePropertiesNormalizer.NormalizePackage(packagePath);
            Console.WriteLine($"Normalized {packagePath}");
        }

        return 0;
    }
}

public static class PackageCorePropertiesNormalizer
{
    public const string CanonicalCorePropertiesPath = "package/services/metadata/core-properties/core-properties.psmdcp";

    private static readonly DateTimeOffset StableZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static void NormalizePackage(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var fullPath = Path.GetFullPath(packagePath);
        var tempPath = fullPath + ".normalize-tmp";
        if (File.Exists(tempPath))
            File.Delete(tempPath);

        using (var sourceStream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false))
        using (var destinationStream = File.Open(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var destinationArchive = new ZipArchive(destinationStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            var corePropertiesEntries = sourceArchive.Entries
                .Where(entry => IsCorePropertiesPart(entry.FullName))
                .ToArray();

            if (corePropertiesEntries.Length != 1)
                throw new InvalidOperationException($"Expected exactly one NuGet core-properties part in {packagePath}, found {corePropertiesEntries.Length}.");

            var originalCorePropertiesPath = corePropertiesEntries[0].FullName;
            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sourceEntry in sourceArchive.Entries)
            {
                var destinationName = sourceEntry.FullName == originalCorePropertiesPath
                    ? CanonicalCorePropertiesPath
                    : sourceEntry.FullName;

                if (!usedNames.Add(destinationName))
                    throw new InvalidOperationException($"Duplicate ZIP entry after normalization: {destinationName}");

                var destinationEntry = destinationArchive.CreateEntry(destinationName, CompressionLevel.Optimal);
                destinationEntry.LastWriteTime = StableZipTimestamp;
                destinationEntry.ExternalAttributes = sourceEntry.ExternalAttributes;

                using var sourceEntryStream = sourceEntry.Open();
                using var destinationEntryStream = destinationEntry.Open();

                if (NeedsXmlReferenceRewrite(sourceEntry.FullName))
                {
                    using var reader = new StreamReader(sourceEntryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
                    using var writer = new StreamWriter(destinationEntryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: false);
                    writer.Write(RewriteCorePropertiesReferences(reader.ReadToEnd(), originalCorePropertiesPath));
                }
                else
                {
                    sourceEntryStream.CopyTo(destinationEntryStream);
                }
            }
        }

        File.Move(tempPath, fullPath, overwrite: true);
    }

    private static bool IsCorePropertiesPart(string entryName)
    {
        return entryName.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)
            && entryName.EndsWith(".psmdcp", StringComparison.Ordinal);
    }

    private static bool NeedsXmlReferenceRewrite(string entryName)
    {
        return entryName.Equals("[Content_Types].xml", StringComparison.Ordinal)
            || entryName.EndsWith(".rels", StringComparison.Ordinal);
    }

    private static string RewriteCorePropertiesReferences(string content, string originalCorePropertiesPath)
    {
        var canonical = CanonicalCorePropertiesPath;
        return content
            .Replace(originalCorePropertiesPath, canonical, StringComparison.Ordinal)
            .Replace("/" + originalCorePropertiesPath, "/" + canonical, StringComparison.Ordinal);
    }
}
