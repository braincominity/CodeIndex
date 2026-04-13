using CodeIndex.Database;

namespace CodeIndex.Tests;

public class NameFoldTests
{
    [Fact]
    public void Fold_UsesUnicodeCaseFoldSemantics()
    {
        Assert.Equal(NameFold.Fold("Straße"), NameFold.Fold("STRASSE"));
        Assert.Equal(NameFold.Fold("Σ"), NameFold.Fold("ς"));
        Assert.Equal(NameFold.Fold("Σ"), NameFold.Fold("σ"));
    }

    [Fact]
    public void Fold_RemainsLocaleInvariantForTurkishDottedI()
    {
        Assert.Equal("i\u0307", NameFold.Fold("İ"));
        Assert.Equal("i", NameFold.Fold("i"));
        Assert.NotEqual(NameFold.Fold("İ"), NameFold.Fold("i"));
    }
}
