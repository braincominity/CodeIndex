namespace CodeIndex.Indexer.Extensibility;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class CdidxPluginAttribute : Attribute
{
    public CdidxPluginAttribute(int minApiVersion, int maxApiVersion)
    {
        MinApiVersion = minApiVersion;
        MaxApiVersion = maxApiVersion;
    }

    public int MinApiVersion { get; }

    public int MaxApiVersion { get; }
}
