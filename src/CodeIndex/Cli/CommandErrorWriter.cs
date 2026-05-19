using System.Text.Json;

namespace CodeIndex.Cli;

internal static class CommandErrorWriter
{
    internal const string DefaultHint = "Run '<cmd> --help' for usage information.";

    internal static void Write(string message, string? hint = null, string? usage = null, string? errorCode = null)
    {
        var prefix = errorCode is null ? "Error" : $"Error [{errorCode}]";
        Console.Error.WriteLine($"{prefix}: {message}");
        Console.Error.WriteLine($"Hint: {hint ?? DefaultHint}");
        if (usage != null)
            Console.Error.WriteLine($"Usage: {usage}");
    }

    internal static int Write(
        string message,
        int exitCode,
        string? hint = null,
        string? usage = null,
        string? errorCode = null)
    {
        Write(message, hint, usage, errorCode);
        return exitCode;
    }

    internal static int WriteJsonOrHuman(
        bool json,
        JsonSerializerOptions jsonOptions,
        string message,
        int exitCode,
        string? hint = null,
        string? usage = null,
        string? errorCode = null)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new CommandErrorJsonResult("error", message, hint, errorCode),
                CliJsonSerializerContextFactory.Create(jsonOptions).CommandErrorJsonResult));
            return exitCode;
        }

        Write(message, exitCode, hint, usage, errorCode);
        return exitCode;
    }
}
