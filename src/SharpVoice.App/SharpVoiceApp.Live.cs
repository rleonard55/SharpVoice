using SharpVoice.Core;
using SharpVoice.Speech;

namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    private readonly object liveGate = new();
    private LiveTranscription? liveTranscription;
    private Task liveStopping = Task.CompletedTask;
    private long liveRevision;
    private void BeginLiveTranscription()
    {
        var previous = StopLiveTranscription(); desktop.SetLiveDraft(null);
        if (!recordingConfig.LiveTranscription) return;
        var settings = recordingConfig; var ready = preload; var activeRecorder = recorder; var id = generation;
        var editorId = editorSession.Busy ? editorSession.Id : id.ToString(); var client = workspaceClient;
        lock (liveGate)
        {
            var revision = ++liveRevision;
            liveTranscription = new(() => activeRecorder.Snapshot(8), async (audio, ct) =>
            {
                await previous.WaitAsync(ct).ConfigureAwait(false); await ready.WaitAsync(ct).ConfigureAwait(false);
                var path = await models.EnsureAsync(settings.SpeechModel, _ => { }, ct);
                return await speech.TranscribeAsync(path, audio, settings.Language, _ => { }, ct, wakeSession is not null ? WakePhrases.VocabularyPrompt(settings) : "", fast: settings.FastTranscription && wakeSession is null);
            }, (text, sequence) => desktop.Post(() =>
            {
                if (revision != liveRevision || generation != id || phase != Phase.Recording || lifetime.IsCancellationRequested) return;
                desktop.SetLiveDraft(wakeSession?.Resolve(text).Text ?? text);
                if (answerReply is not null) desktop.SetAnswerStatus("Live Draft · " + TranscriptDraft.Tail(text, 110));
                if (recordingMode == VoiceMode.AskAi && settings.ConnectedAsk && !editorSession.Busy && !workspaceAsking) desktop.SetConnectedStatus("Live Draft · " + TranscriptDraft.Tail(text, 150));
                if ((workspaceAsking || editorSession.Busy) && client.Length > 0 && client == workspaceClient && editorBridge is not null)
                {
                    var sent = editorBridge.SendAsync(new("draft", editorId, Message: text, Sequence: sequence, ConversationId: workspaceConversation, ClientId: client));
                    _ = sent.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                }
            }), operation!.Token, unavailable: () => desktop.Post(() =>
            {
                if (revision == liveRevision && generation == id && phase == Phase.Recording) desktop.SetLiveDraft(null);
            }));
        }
    }
    private Task StopLiveTranscription()
    {
        lock (liveGate)
        {
            var revision = ++liveRevision;
            desktop.Post(() => { if (revision == liveRevision) desktop.SetLiveDraft(null); });
            if (liveTranscription is { } worker)
            {
                liveTranscription = null; liveStopping = Task.WhenAll(liveStopping, worker.DisposeAsync().AsTask());
            }
            return liveStopping;
        }
    }
}
