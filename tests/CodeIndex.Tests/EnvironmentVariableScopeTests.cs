namespace CodeIndex.Tests;

[Collection("SQLite pool sensitive")]
public class EnvironmentVariableScopeTests
{
    [Fact]
    public void Dispose_RestoresOriginalValue()
    {
        var name = $"CDIDX_TEST_SCOPE_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, "before");
        try
        {
            using (var env = EnvironmentVariableScope.Capture(name))
            {
                env.Set(name, "during");
                Assert.Equal("during", Environment.GetEnvironmentVariable(name));
            }

            Assert.Equal("before", Environment.GetEnvironmentVariable(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Dispose_RestoresMissingValue()
    {
        var name = $"CDIDX_TEST_SCOPE_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, null);

        using (var env = EnvironmentVariableScope.Capture(name))
        {
            env.Set(name, "during");
            Assert.Equal("during", Environment.GetEnvironmentVariable(name));
        }

        Assert.Null(Environment.GetEnvironmentVariable(name));
    }
}
