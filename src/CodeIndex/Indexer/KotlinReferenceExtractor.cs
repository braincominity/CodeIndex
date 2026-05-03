namespace CodeIndex.Indexer;

internal static class KotlinReferenceExtractor
{
    public static void EmitTrailingLambdaReferences(
        string preparedLine,
        Action<string, int> addCallLikeReference)
        => TrailingLambdaReferenceExtractor.EmitReferences(preparedLine, addCallLikeReference);
}
