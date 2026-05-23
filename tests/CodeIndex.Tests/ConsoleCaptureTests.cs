namespace CodeIndex.Tests;

public class ConsoleCaptureTests
{
    [Fact]
    public void CaptureError_RestoresConsoleError_WhenActionThrows()
    {
        var originalError = Console.Error;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConsoleCapture.CaptureError(() => throw new InvalidOperationException("boom")));

        Assert.Equal("boom", ex.Message);
        Assert.Same(originalError, Console.Error);
    }

    [Fact]
    public void Capture_RestoresConsoleStreams_WhenActionThrows()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConsoleCapture.Capture(() => throw new InvalidOperationException("boom")));

        Assert.Equal("boom", ex.Message);
        Assert.Same(originalOut, Console.Out);
        Assert.Same(originalError, Console.Error);
    }
}
