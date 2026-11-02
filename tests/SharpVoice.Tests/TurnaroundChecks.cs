using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Speech;
using System.Diagnostics;
using System.Text;
internal static class TurnaroundChecks
{
    public static Task Preferences()
    {
        var path=Path.Combine(Path.GetTempPath(),"SharpVoice-Turnaround-"+Guid.NewGuid()+".json");
        try
        {
            ConfigManager.Save(new AppConfig(),path);var json=File.ReadAllText(path);
            using var original=JsonDocument.Parse(json);
            string Fixture(bool? fast)=>ConnectedJson.Build(w=>{w.WriteStartObject();foreach(var field in original.RootElement.EnumerateObject())if(field.Name!="fast_transcription"){w.WritePropertyName(field.Name);field.Value.WriteTo(w);}if(fast is bool enabled)w.WriteBoolean("fast_transcription",enabled);w.WriteEndObject();});
            File.WriteAllText(path,Fixture(null));if(ConfigManager.Load(path).FastTranscription)throw new Exception("Legacy settings silently enabled fast transcription.");
            File.WriteAllText(path,Fixture(true));
            var loaded=ConfigManager.Load(path);ConfigManager.Save(loaded,path);
            using var result=JsonDocument.Parse(File.ReadAllText(path));
            if(!result.RootElement.TryGetProperty("fast_transcription",out var value)||!value.GetBoolean())throw new Exception("The fast-transcription preference was not retained.");
        }
        finally{File.Delete(path);}
        return Task.CompletedTask;
    }

    public static Task DecodeBounds()
    {
        if(new AppConfig().FastTranscription||new AppConfig()==new AppConfig{FastTranscription=true})throw new Exception("Fast mode was enabled by default or ignored in settings equality.");
        foreach(var count in new[]{0,16000,128000,15*16000,15*16000+1,120*16000})
        {
            if(WhisperSpeedPlan.AudioContext(count,false)!=0)throw new Exception("Standard context changed.");
            var context=WhisperSpeedPlan.AudioContext(count,true);
            if(count==0||count>15*16000){if(context!=0)throw new Exception("Long recordings were given a truncated context.");continue;}
            if(context<768||context>1500||context%64!=0||context*320<count+128*320)throw new Exception("Fast encoder context loses audio, padding, or alignment.");
        }
        return Task.CompletedTask;
    }
    public static async Task Benchmark(string model,string wav)
    {
        if(!await ModelManager.VerifyAsync(model,ModelManager.HashFor(Path.GetFileNameWithoutExtension(model).Replace("ggml-","")),default))throw new Exception("Benchmark model checksum mismatch.");
        using var file=File.OpenRead(wav);using var reader=new BinaryReader(file);file.Position=12;float[] samples=[];
        while(file.Position+8<=file.Length){var kind=Encoding.ASCII.GetString(reader.ReadBytes(4));var count=reader.ReadUInt32();var end=file.Position+count+(count&1);if(kind=="data"){samples=new float[count/2];for(var i=0;i<samples.Length;i++)samples[i]=reader.ReadInt16()/32768f;}file.Position=end;}
        if(samples.Length<11*16000)throw new Exception("Use the public11-second16kHz PCM16 mono JFK fixture.");
        static string Words(string text)=>string.Join(" ",System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(),"[a-z0-9]+"));
        using var engine=new WhisperEngine();await engine.PreloadAsync(model,default);
        foreach(var seconds in new[]{3,6,11})
        {
            var audio=samples.AsMemory(0,seconds*16000);var normal=new List<double>();var quick=new List<double>();var originals=new List<string>();var faster=new List<string>();
            for(var round=0;round<3;round++)foreach(var fast in round%2==0?new[]{false,true}:new[]{true,false})
            {
                var clock=Stopwatch.StartNew();var result=await engine.TranscribeAsync(model,audio,"en",_=>{},default,fast:fast);
                (fast?quick:normal).Add(clock.Elapsed.TotalMilliseconds);(fast?faster:originals).Add(result);
            }
            var same=faster.All(x=>Words(x)==Words(originals[0]));
            Console.WriteLine($"AUDIO_SECONDS={seconds} STANDARD_MS={string.Join(",",normal.Select(x=>x.ToString("F0")))} FAST_MS={string.Join(",",quick.Select(x=>x.ToString("F0")))} SAME_WORDS={same}");
            if(!same)Console.WriteLine("QUALITY_NOTE: Standard and Fast words differ on this clip. Fast mode remains opt-in; timings are not an accuracy guarantee.");
            if(faster.Any(string.IsNullOrWhiteSpace))throw new Exception("Fast transcription returned no fixture text.");
        }
        using var cancel=new CancellationTokenSource(100);var stopped=Stopwatch.StartNew();var canceled=false;
        try{await engine.TranscribeAsync(model,samples.AsMemory(0,8*16000),"en",_=>{},cancel.Token,fast:true);}catch(OperationCanceledException){canceled=true;}
        Console.WriteLine($"FAST_DRAFT_CANCEL_DRAIN_MS={stopped.Elapsed.TotalMilliseconds:F0} CANCELED={canceled}");
        var final=await engine.TranscribeAsync(model,samples,"en",_=>{},default,fast:true);
        if(!final.Contains("country",StringComparison.OrdinalIgnoreCase)||!canceled)throw new Exception("Canceled fast decoding damaged later final transcription.");
        Console.WriteLine("PASS actual Whisper engine standard/fast timings, public word comparison and cancellation drain.");
    }
}
