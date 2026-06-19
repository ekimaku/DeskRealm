using System.Text;

namespace DeskRealm.App.Services;

internal static class IconWorkerProtocol
{
    // JSON Lines is a text protocol. A UTF-8 BOM before the first '{' is not JSON
    // and causes System.Text.Json to reject the command. Keep both redirected pipe
    // directions explicitly BOM-free and fail on malformed UTF-8 instead of replacing bytes.
    public static Encoding Utf8NoBom { get; } = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static void ValidateJsonLine(string line, string direction)
    {
        if (line.Length == 0)
        {
            throw new InvalidOperationException($"Persistent icon worker {direction} is empty.");
        }

        if (line[0] == '\uFEFF')
        {
            throw new InvalidOperationException(
                $"Persistent icon worker {direction} starts with a UTF-8 BOM. " +
                "The JSON-lines protocol requires BOM-free UTF-8.");
        }

        if (line[0] != '{')
        {
            throw new InvalidOperationException(
                $"Persistent icon worker {direction} is contaminated before the JSON object. " +
                $"First character: U+{(int)line[0]:X4}.");
        }
    }
}
