using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpVoice.Platform.Windows;

/// <summary>Bundled edge-tts client; stdin text, bounded binary stdout, app-owned temporary audio.</summary>
internal static partial class EdgeAnswerSpeech
{
    internal static async Task SpeakAsync(string text, string voice, Action<string> status, CancellationToken ct)
    {
        status("Preparing Edge voice…");
        var executable = await Task.Run(() => EdgeSpeechBundle.Ensure(Path.Combine(SharpVoice.Core.AppPaths.Root, "speech"), ct), ct).ConfigureAwait(false);
        status("Generating Edge speech…");
        var root = Path.Combine(Path.GetTempPath(), "SharpVoice-speech-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var file = Path.Combine(root, "answer.mp3");
        try
        {
            await SynthesizeAsync(executable, text, voice, file, ct, privatePython: true).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); status("Reading with Edge voice…");
            await Task.Run(() => Play(file, ct), ct).ConfigureAwait(false);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    internal static async Task SynthesizeAsync(string executable, string text, string voice, string file, CancellationToken ct, bool privatePython = false)
    {
        ct.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), WorkingDirectory = Path.GetDirectoryName(file)! };
        if (privatePython)
        {
            ConfigureInterpreter(start, file); start.ArgumentList.Add("-m"); start.ArgumentList.Add("edge_tts");
        }
        foreach (var arg in new[] { "--file", "-", "--voice", voice, "--write-media", "-", "--no-progress" }) start.ArgumentList.Add(arg);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        using var child = new Process { StartInfo = start };
        if (!child.Start()) throw new InvalidOperationException("Edge speech helper could not start.");
        using var cancel = deadline.Token.Register(() => { try { if (!child.HasExited) child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        async Task Write()
        { try { await child.StandardInput.WriteAsync(text.AsMemory(), deadline.Token).ConfigureAwait(false); child.StandardInput.Close(); } catch { deadline.Cancel(); throw; } }
        async Task ReadAudio()
        {
            try
            {
                await using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true);
                var bytes = new byte[8192]; var total = 0; int length;
                while ((length = await child.StandardOutput.BaseStream.ReadAsync(bytes, deadline.Token).ConfigureAwait(false)) != 0)
                {
                    total += length; if (total > 8_000_000) throw new InvalidDataException("Edge audio exceeded its limit.");
                    await output.WriteAsync(bytes.AsMemory(0, length), deadline.Token).ConfigureAwait(false);
                }
                if (total < 32) throw new InvalidDataException("Edge returned no audio.");
            }
            catch { deadline.Cancel(); throw; }
        }
        async Task Drain()
        { try { var chars = new char[2048]; var total = 0; int n; while ((n = await child.StandardError.ReadAsync(chars, deadline.Token).ConfigureAwait(false)) != 0) { total += n; if (total > 131072) throw new InvalidDataException("Excessive Edge diagnostics."); } } catch { deadline.Cancel(); throw; } }
        var tasks = new[] { Write(), ReadAudio(), Drain(), child.WaitForExitAsync(deadline.Token) };
        try
        {
            await Task.WhenAll(tasks).WaitAsync(deadline.Token).ConfigureAwait(false); ct.ThrowIfCancellationRequested();
            if (child.ExitCode != 0) throw new InvalidOperationException("Edge speech helper failed.");
        }
        catch
        {
            deadline.Cancel();
            // The token-bound wait may already be cancelled while Kill is still
            // completing. Reap the owned child before removing its working directory.
            try { await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch (Exception) { }
            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch (Exception) { }
            ct.ThrowIfCancellationRequested(); throw new InvalidOperationException("Edge speech is unavailable. Check your connection.");
        }
    }
    internal static void ConfigureInterpreter(ProcessStartInfo start, string audioFile)
    {
        // -I ignores PYTHON* environment settings. Force UTF-8 for redirected stdin,
        // and avoid using mutable cached bytecode beside the verified source files.
        foreach (var arg in new[] { "-I", "-B", "-X", "utf8", "-X", "pycache_prefix=" + Path.Combine(Path.GetDirectoryName(audioFile)!, "bytecode") })
            start.ArgumentList.Add(arg);
    }
    private static void Play(string file, CancellationToken ct)
    {
        var alias = "sv" + Guid.NewGuid().ToString("N");
        void Command(string command) { if (mciSendStringW(command, null, 0, 0) != 0) throw new InvalidOperationException("Windows could not play Edge speech audio."); }
        try
        {
            ct.ThrowIfCancellationRequested(); Command($"open \"{file}\" type mpegvideo alias {alias}"); Command("play " + alias);
            var state = new StringBuilder(64);
            while (true)
            {
                ct.ThrowIfCancellationRequested(); state.Clear();
                if (mciSendStringW("status " + alias + " mode", state, 64, 0) != 0) throw new InvalidOperationException("Speech playback failed.");
                if (state.ToString().Equals("stopped", StringComparison.OrdinalIgnoreCase)) break;
                Thread.Sleep(30);
            }
        }
        finally { _ = mciSendStringW("stop " + alias, null, 0, 0); _ = mciSendStringW("close " + alias, null, 0, 0); }
    }
    // StringBuilder marshalling is supported by the runtime P/Invoke stub in Native AOT.
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint mciSendStringW(string command, StringBuilder? result, uint length, nint callback);
}
