using System.Diagnostics;
using SharpVoice.Audio;
using SharpVoice.Platform.Windows;

internal static class WindowsPerformanceSmoke
{
    public static async Task Run()
    {
        var watch = Stopwatch.StartNew();
        using var recorder = new WasapiAudioRecorder(-65, 120);
        await recorder.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine($"CAPTURE_READY_MS={watch.Elapsed.TotalMilliseconds:F2}");
        var process = Process.GetCurrentProcess();
        async Task Measure(string label)
        {
            process.Refresh();
            var cpu = process.TotalProcessorTime;
            watch.Restart();
            await Task.Delay(5000);
            process.Refresh();
            Console.WriteLine($"{label}_CPU_ONE_CORE_PERCENT={(process.TotalProcessorTime - cpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds * 100:F3}");
            Console.WriteLine($"{label}_WORKING_SET_MIB={process.WorkingSet64 / 1048576d:F2}");
            Console.WriteLine($"{label}_PRIVATE_MIB={process.PrivateMemorySize64 / 1048576d:F2}");
        }
        await Measure("CAPTURING_IDLE");
        recorder.Begin();
        await Task.Delay(200);
        using (var audio = await recorder.EndAsync(default))
            if (audio.Samples.Length < 800) throw new Exception("Capture did not produce tail frames.");
        if (!MemoryTrimmer.Trim()) throw new Exception("Working-set trim failed.");
        await Measure("AFTER_RECORDING_TRIM");
        recorder.Configure(-65, true);
        await Measure("PAUSED");
        Console.WriteLine("PASS capture performance measured without saving microphone audio.");
    }
}
