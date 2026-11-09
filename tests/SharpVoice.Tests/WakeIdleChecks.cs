using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using SharpVoice.App;
using SharpVoice.Audio;

internal static class WakeIdleChecks
{
    public static async Task CaptureDoesNotBlockIdle()
    {
        // Run each case in a child: a regression would otherwise hang recorder disposal
        // and the entire suite. The child's deadline never terminates the user's app.
        foreach (var mode in new[] { "off", "on" })
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add("--wake-idle-fixture");
            start.ArgumentList.Add(mode);
            using var child = Process.Start(start) ?? throw new Exception("Could not start wake idle fixture.");
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException)
            {
                child.Kill(); await child.WaitForExitAsync();
                throw new Exception("Wake idle fixture hung with wake " + mode + ": " + await output + await error);
            }
            if (child.ExitCode != 0)
                throw new Exception("Wake idle fixture failed with wake " + mode + ": " + await output + await error);
        }
    }

    public static async Task RunFixture(bool enabled)
    {
        // Reintroducing wakeGate -> recorder.gate in WakeUpdateIdle must fail this
        // test. Both the idle getter and wake callback below are production code;
        // only device I/O and the interleaving are controlled by the fixture.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var app = (SharpVoiceApp)RuntimeHelpers.GetUninitializedObject(typeof(SharpVoiceApp));
        using var drivers = new MicrophoneActivationChecks.Drivers();
        using var recorder = drivers.Recorder(false);
        typeof(SharpVoiceApp).GetField("wakeGate", flags)!.SetValue(app, new object());
        typeof(SharpVoiceApp).GetField("recorder", flags)!.SetValue(app, recorder);
        typeof(SharpVoiceApp).GetField("wakeWork", flags)!.SetValue(app, Task.CompletedTask);
        var callback = typeof(SharpVoiceApp).GetMethod("OnWakeAudio", flags)!.CreateDelegate<WakeAudioHandler>(app);
        var idle = typeof(SharpVoiceApp).GetProperty("WakeUpdateIdle", flags)!.GetMethod!.CreateDelegate<Func<bool>>(app);
        using var callbackEntered = new ManualResetEventSlim();
        using var continueCallback = new ManualResetEventSlim();
        using var idleEntered = new ManualResetEventSlim();
        using var idleReturned = new ManualResetEventSlim();
        Exception? idleFailure = null;
        bool? idleResult = null;
        await recorder.StartAsync();
        recorder.SetPreview(true); // Keep capture identical for the wake-off control.
        recorder.SetWakeListening(enabled, (samples, first, epoch) =>
        {
            callbackEntered.Set(); // Consume owns recorder.gate at this point.
            if (!continueCallback.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Fixture did not release the microphone callback.");
            callback(samples, first, epoch);
        });
        var capture = await drivers.Next();
        await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var emitted = capture.Emit(new float[1600]);
        if (enabled && !callbackEntered.Wait(TimeSpan.FromSeconds(3)))
            throw new Exception("Wake callback did not begin.");
        var ui = new Thread(() =>
        {
            idleEntered.Set();
            try { idleResult = idle(); }
            catch (Exception ex) { idleFailure = ex; }
            finally { idleReturned.Set(); }
        }) { IsBackground = true, Name = "Wake idle fixture UI" };
        ui.Start();
        if (!idleEntered.Wait(TimeSpan.FromSeconds(3))) throw new Exception("Idle check did not begin.");
        if (enabled && !SpinWait.SpinUntil(() => (ui.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(3)))
            throw new Exception("Idle check did not wait for the in-progress microphone callback.");
        continueCallback.Set();
        if (!idleReturned.Wait(TimeSpan.FromSeconds(2)))
        {
            Console.Error.WriteLine("Deadlock: idle check and microphone callback cannot complete with wake " + enabled + ".");
            Console.Error.Flush();
            Environment.Exit(1);
        }
        await emitted.WaitAsync(TimeSpan.FromSeconds(3));
        ui.Join();
        if (idleFailure is not null) throw new Exception("Idle check failed.", idleFailure);
        // With listening enabled and no activity measurement, restart must remain
        // blocked. With listening disabled, a prepared service may be idle.
        if (idleResult != !enabled) throw new Exception("Wake activity eligibility changed.");
        Console.WriteLine("PASS wake " + (enabled ? "on" : "off") + ": idle check and microphone callback completed.");
    }
}
