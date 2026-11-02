using System.Runtime.CompilerServices;
using System.Text;

namespace SharpVoice.Refiner;

public static class SseParser
{
    public static async IAsyncEnumerable<string> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0) { yield return data.ToString().TrimEnd('\n'); data.Clear(); }
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var value = line.AsMemory(5);
            if (value.Span.StartsWith(" ")) value = value[1..];
            data.Append(value.Span).Append('\n');
            if (data.Length > 2 * 1024 * 1024) throw new InvalidDataException("Refinement event exceeded 2 MB.");
        }
        // An unterminated final event is intentionally discarded. Callers require a completion event.
    }
}
