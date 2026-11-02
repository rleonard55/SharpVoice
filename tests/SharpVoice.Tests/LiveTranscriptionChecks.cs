using SharpVoice.Core;
using SharpVoice.Audio;
using SharpVoice.Speech;

internal static class LiveTranscriptionChecks
{
    public static async Task Worker()
    {
        var pool = new TrackingFloatPool();var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks=0;var snapshots=0;
        var worker=new LiveTranscription(()=>++snapshots==1?new AudioRecording(pool.Rent(16000),16000,true,pool):null,
            async (audio,_)=>{if(audio.Length!=16000)throw new Exception("Draft audio changed.");entered.TrySetResult();await release.Task;return "Late draft";},(_,_)=>callbacks++,default,TimeSpan.FromMilliseconds(5));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));var stopping=worker.DisposeAsync().AsTask();
        if(stopping.IsCompleted||pool.Returns!=0)throw new Exception("Canceled preview returned audio while inference still owned it.");
        release.SetResult();await stopping.WaitAsync(TimeSpan.FromSeconds(3));
        if(callbacks!=0||pool.Returns!=1||!pool.Cleared)throw new Exception("Stale preview was published or owned audio was not cleared.");
        var heard=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var active=0;var max=0;
        await using(var live=new LiveTranscription(()=>new AudioRecording(pool.Rent(16000),16000,true,pool),async(_,ct)=>{var n=Interlocked.Increment(ref active);max=Math.Max(max,n);await Task.Delay(10,ct);Interlocked.Decrement(ref active);return "Heard words";},(text,_)=>{if(text=="Heard words")heard.TrySetResult();},default,TimeSpan.FromMilliseconds(5)))
        {await heard.Task.WaitAsync(TimeSpan.FromSeconds(3));}
        if(max!=1)throw new Exception("Preview inference overlapped.");
    }
    public static async Task Benchmark(string model, string wav)
    {
        if (!await ModelManager.VerifyAsync(model, ModelManager.HashFor(Path.GetFileNameWithoutExtension(model).Replace("ggml-", "")), default)) throw new Exception("Benchmark model failed its pinned checksum.");
        using var file = File.OpenRead(wav); using var reader = new BinaryReader(file);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new Exception("Expected WAV."); reader.ReadUInt32(); if (new string(reader.ReadChars(4)) != "WAVE") throw new Exception("Expected WAVE.");
        float[]? samples = null;
        while(file.Position + 8 <= file.Length)
        {
            var kind = new string(reader.ReadChars(4)); var count = reader.ReadUInt32(); var next = file.Position + count + (count & 1);
            if(kind == "data") { samples = new float[count / 2]; for(var i=0;i<samples.Length;i++) samples[i] = reader.ReadInt16()/32768f; }
            file.Position = next;
        }
        if(samples is null)throw new Exception("Missing fixture audio.");
        using var engine = new WhisperEngine(); await engine.PreloadAsync(model, default);
        var countDrafts=0; var snapshots=0; var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var intervals=new List<double>(); var watch=System.Diagnostics.Stopwatch.StartNew(); var last=0d;
        await using(var live=new LiveTranscription(()=>
        {
            var count=Math.Min(samples.Length, 16000*Math.Min(8,2+2*Interlocked.Increment(ref snapshots)));var data=System.Buffers.ArrayPool<float>.Shared.Rent(count);samples.AsSpan(0,count).CopyTo(data);return new AudioRecording(data,count,true);
        },(audio,ct)=>engine.TranscribeAsync(model,audio,"en",_=>{},ct),(text,_)=>{var time=watch.Elapsed.TotalMilliseconds;intervals.Add(time-last);last=time;if(Interlocked.Increment(ref countDrafts)>=3)done.TrySetResult();},default))
        {await done.Task.WaitAsync(TimeSpan.FromSeconds(45));}
        Console.WriteLine("LIVE_DRAFT_INTERVALS_MS="+string.Join(",",intervals.Select(x=>x.ToString("F0",System.Globalization.CultureInfo.InvariantCulture))));
        var longAudio=Enumerable.Range(0,8).SelectMany(_=>samples).ToArray();using var cancel=new CancellationTokenSource(100);watch.Restart();var canceled=false;
        try{await engine.TranscribeAsync(model,longAudio,"en",_=>{},cancel.Token);}catch(OperationCanceledException){canceled=true;}
        Console.WriteLine("CANCEL_DRAIN_MS="+watch.Elapsed.TotalMilliseconds.ToString("F0",System.Globalization.CultureInfo.InvariantCulture)+" CANCELED="+canceled);
        watch.Restart();var final=await engine.TranscribeAsync(model,samples,"en",_=>{},default);
        if(!final.Contains("country",StringComparison.OrdinalIgnoreCase))throw new Exception("Final transcription changed after preview cancellation.");
        Console.WriteLine("FINAL_INFERENCE_MS="+watch.Elapsed.TotalMilliseconds.ToString("F0",System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine("PASS local live draft cadence, native cancellation drain and final full-recording transcription.");
    }
    public static Task Configuration()
    {
        var path=Path.Combine(Path.GetTempPath(),"sharpvoice-live-"+Guid.NewGuid()+".json");
        try
        {
            ConfigManager.Save(new AppConfig(), path);
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"live_transcription\": true,", "")); if(!ConfigManager.Load(path).LiveTranscription)throw new Exception("Legacy installs did not enable live local drafts.");
            ConfigManager.Save(new AppConfig{LiveTranscription=false},path);if(ConfigManager.Load(path).LiveTranscription)throw new Exception("Explicit live-off preference was lost.");
            var tail=TranscriptDraft.Tail(new string('x',600)+"😀 latest words",40);if(!tail.EndsWith("latest words")||tail.Contains(new string('x',40))||char.IsLowSurrogate(tail[0]))throw new Exception("Live text shows its old beginning or splits Unicode.");
        }
        finally{if(File.Exists(path))File.Delete(path);}
        return Task.CompletedTask;
    }
}
