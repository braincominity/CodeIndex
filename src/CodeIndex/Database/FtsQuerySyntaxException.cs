namespace CodeIndex.Database;

internal sealed class FtsQuerySyntaxException : Exception
{
    public FtsQuerySyntaxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public FtsQuerySyntaxException(string message)
        : base(message)
    {
    }
}
