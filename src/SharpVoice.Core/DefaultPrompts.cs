namespace SharpVoice.Core;

public static class DefaultPrompts
{
    public static string MasterPrompt { get; } = ReadMasterPrompt();

    private static string ReadMasterPrompt()
    {
        using var stream = typeof(DefaultPrompts).Assembly.GetManifestResourceStream("SharpVoice.MasterPrompt.txt")
            ?? throw new InvalidOperationException("The default cleanup prompt is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
