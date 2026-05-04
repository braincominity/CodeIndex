using System.Text.RegularExpressions;

namespace CodeIndex.Indexer;

internal static class PowerShellReferenceExtractor
{
    // PowerShell cmdlet / function calls are statement-start or pipeline-stage forms such as
    // `Get-ChildItem -Path .`, `Write-Host "x"`, and `$items | ForEach-Object { ... }`.
    // PowerShell の cmdlet / function 呼び出しは statement-start / pipeline 形で現れる。
    private static readonly Regex CallRegex = new(
        @"(?:^|[|;&{=]\s*)\s*(?<name>[A-Za-z][A-Za-z0-9]*(?:-[A-Za-z][A-Za-z0-9]*)+)\b",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static void EmitCallReferences(string preparedLine, Action<string, int> addCallLikeReference)
    {
        foreach (Match match in CallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            var callIndex = match.Groups["name"].Index;
            addCallLikeReference(name, callIndex);
        }
    }
}
