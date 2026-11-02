using System.Text;
using System.Text.RegularExpressions;

namespace SharpVoice.Speech;

/// <summary>Exact SentencePiece unigram scoring for restricted ASCII English. The pinned bpe.model is unigram, despite its name.</summary>
public sealed partial class WakeTokenizer
{
    private readonly List<(string Text, float Score)> pieces = [];
    public WakeTokenizer(string model, string tokens)
    {
        var bytes = File.ReadAllBytes(model);
        if (bytes.Length > 1_000_000) throw new InvalidDataException("Invalid wake vocabulary size.");
        var expected = File.ReadLines(tokens).Select(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToDictionary(x => int.Parse(x[1], System.Globalization.CultureInfo.InvariantCulture), x => x[0]);
        using var reader = new BinaryReader(new MemoryStream(bytes));
        var index = 0; var unigram = false;
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var tag = Varint(reader);
            if (tag is 10 or 18)
            {
                var data = Data(reader); using var field = new BinaryReader(new MemoryStream(data));
                string text = ""; float score = 0; ulong type = 1;
                while (field.BaseStream.Position < field.BaseStream.Length)
                {
                    var key = Varint(field);
                    if (tag == 10 && key == 10) text = Encoding.UTF8.GetString(Data(field));
                    else if (tag == 10 && key == 21) score = field.ReadSingle();
                    else if (tag == 10 && key == 24) type = Varint(field);
                    else if (tag == 18 && key == 24) unigram = Varint(field) == 1;
                    else Skip(field, key);
                }
                if (tag == 10)
                {
                    if (!expected.TryGetValue(index++, out var value) || value != text) throw new InvalidDataException("Wake vocabulary does not match its acoustic model.");
                    if (type == 1 && text.Length > 0 && float.IsFinite(score)) pieces.Add((text, score));
                }
            }
            else Skip(reader, tag);
        }
        if (!unigram || index != expected.Count || pieces.Count == 0) throw new InvalidDataException("Unsupported wake tokenizer model.");
    }
    public string Encode(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase) || phrase.Length > 80 || !English().IsMatch(phrase)) throw new ArgumentException("Use an English wake phrase with letters, spaces and apostrophes.");
        // NFKC is identity for the restricted alphabet. Collapse whitespace, add
        // the SentencePiece dummy prefix and escape spaces exactly as the model does.
        var words = Brand().Replace(phrase, "sharp voice").ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var text = "▁" + string.Join('▁', words);
        var scores = new float[text.Length + 1]; Array.Fill(scores, float.NegativeInfinity); scores[0] = 0;
        var starts = new int[text.Length + 1]; var selected = new string?[text.Length + 1];
        for (var position = 0; position < text.Length; position++)
        {
            if (!float.IsFinite(scores[position])) continue;
            foreach (var piece in pieces)
            {
                var end = position + piece.Text.Length;
                if (end > text.Length || !text.AsSpan(position, piece.Text.Length).SequenceEqual(piece.Text)) continue;
                var score = scores[position] + piece.Score;
                if (score > scores[end]) { scores[end] = score; starts[end] = position; selected[end] = piece.Text; }
            }
        }
        if (selected[^1] is null) throw new ArgumentException("This wake phrase contains sounds the model cannot represent.");
        var result = new List<string>();
        for (var end = text.Length; end > 0; end = starts[end]) result.Add(selected[end]!);
        result.Reverse(); return string.Join(' ', result);
    }
    private static ulong Varint(BinaryReader reader)
    {
        ulong value = 0;
        for (var shift = 0; shift < 70; shift += 7) { var b = reader.ReadByte(); value |= (ulong)(b & 127) << shift; if ((b & 128) == 0) return value; }
        throw new InvalidDataException("Invalid wake model field.");
    }
    private static byte[] Data(BinaryReader reader)
    {
        var size = checked((int)Varint(reader));
        if (size < 0 || size > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Incomplete wake vocabulary.");
        return reader.ReadBytes(size);
    }
    private static void Skip(BinaryReader reader, ulong tag)
    {
        switch (tag & 7)
        {
            case 0: Varint(reader); break;
            case 1: reader.ReadUInt64(); break;
            case 2: Data(reader); break;
            case 5: reader.ReadUInt32(); break;
            default: throw new InvalidDataException("Unsupported wake model field.");
        }
    }
    [GeneratedRegex("^[A-Za-z' ]+$", RegexOptions.CultureInvariant)] private static partial Regex English();
    [GeneratedRegex(@"\bsharpvoice\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex Brand();
}
