namespace SharpVoice.Core;

public sealed record SmartAskResult(string Route, string Text)
{
    public static SmartAskResult Parse(string json)
    {
        const string error = "AI returned an invalid Smart Ask response. Your request is retained; nothing was inserted.";
        if (json.Length > 1_500_000) throw new InvalidDataException(error);
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json, new() { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) throw new InvalidDataException(error);
            var names = new HashSet<string>();
            foreach (var property in root.EnumerateObject())
                if (property.Name is not ("route" or "text") || !names.Add(property.Name) || property.Value.ValueKind != System.Text.Json.JsonValueKind.String) throw new InvalidDataException(error);
            if (names.Count != 2) throw new InvalidDataException(error);
            var route = root.GetProperty("route").GetString()!; var text = root.GetProperty("text").GetString()!;
            if (route is not ("answer" or "insert") || string.IsNullOrWhiteSpace(text) || text.Length > 200_000 || text.Contains('\0')) throw new InvalidDataException(error);
            return new(route, text);
        }
        catch (System.Text.Json.JsonException) { throw new InvalidDataException(error); }
    }
}
