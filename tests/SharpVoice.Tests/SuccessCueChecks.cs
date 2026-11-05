using SharpVoice.UI;

internal static class SuccessCueChecks
{
    public static Task QuestionAsset()
    {
        var wav = Read("follow-up-question");
        Check(BitConverter.ToUInt16(wav, 22) == 1 && BitConverter.ToUInt32(wav, 24) == 44100 && BitConverter.ToUInt16(wav, 34) == 16, "Question cue must match PCM output.");
        Check((wav.Length - 44) / 88200d is >= .26 and <= .28, "Question cue should be a short two-note chime.");
        Check(Peak(wav) > 100 && Peak(wav) < 5000, "Question cue should be audible and restrained.");
        Check(Energy(wav, .09, .14) == 0 && Energy(wav, 0, .07) > 0 && Energy(wav, .16, .24) > 0, "Question cue requires separated notes.");
        foreach (var name in new[] { "recording-start", "recording-stop", "warning", "insertion-success" }) Check(!wav.SequenceEqual(Read(name)), "Question cue must be distinct.");
        return Task.CompletedTask;
    }
    public static Task Asset()
    {
        var success = Read("insertion-success");
        var start = Read("recording-start");
        var stop = Read("recording-stop");
        var warning = Read("warning");
        Check(success.Length >= 44 && success.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && success.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8)
            && BitConverter.ToUInt32(success, 16) == 16 && BitConverter.ToUInt16(success, 20) == 1
            && BitConverter.ToUInt16(success, 22) == 1 && BitConverter.ToUInt32(success, 24) == 44100
            && BitConverter.ToUInt32(success, 28) == 88200 && BitConverter.ToUInt16(success, 32) == 2
            && BitConverter.ToUInt16(success, 34) == 16 && success.AsSpan(36, 4).SequenceEqual("data"u8)
            && BitConverter.ToUInt32(success, 40) == success.Length - 44, "Success cue must match prepared mono 44.1 kHz PCM16 output.");
        var seconds = (success.Length - 44) / 88200d;
        Check(seconds is >= .13 and <= .15, "Success cue should last about 140 ms.");
        Check(Peak(success) > 100 && Peak(success) < .6 * Math.Min(Peak(start), Peak(stop)), "Success cue should be audible and subdued relative to recording cues.");
        Check(!success.SequenceEqual(start) && !success.SequenceEqual(stop) && !success.SequenceEqual(warning), "Success must have a distinct audio asset.");
        Check(Energy(success, 0, .05) > 4 * Energy(success, .09, .14), "Success ping should gently decay.");
        return Task.CompletedTask;
    }

    private static byte[] Read(string name)
    {
        using var stream = typeof(NativeDesktop).Assembly.GetManifestResourceStream($"SharpVoice.UI.Assets.{name}.wav") ?? throw new Exception("Missing cue asset: " + name);
        using var result = new MemoryStream();
        stream.CopyTo(result);
        return result.ToArray();
    }
    private static int Peak(byte[] wav)
    {
        var peak = 0;
        for (var i = 44; i < wav.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(wav, i)));
        return peak;
    }
    private static double Energy(byte[] wav, double start, double end)
    {
        double energy = 0;
        for (var sample = (int)(start * 44100); sample < (int)(end * 44100); sample++)
        {
            var value = (double)BitConverter.ToInt16(wav, 44 + 2 * sample);
            energy += value * value;
        }
        return energy / ((end - start) * 44100);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
