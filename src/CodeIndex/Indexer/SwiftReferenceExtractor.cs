namespace CodeIndex.Indexer;

internal static class SwiftReferenceExtractor
{
    public static void EmitTrailingClosureReferences(
        string preparedLine,
        Action<string, int> addCallLikeReference)
        => TrailingLambdaReferenceExtractor.EmitReferences(preparedLine, addCallLikeReference);
}
