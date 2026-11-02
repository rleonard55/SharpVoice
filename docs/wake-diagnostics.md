# Wake phrase diagnostic utility

Run `Launch-Wake-Diagnostics.cmd` from the repository folder, or open `artifacts/diagnostics/SharpVoice.Diagnostics.exe`. This is a separate app: it tests SharpVoice's actual microphone capture, wake detector, speech ending and repeated rearming without making AI requests or inserting text.

1. Exit SharpVoice using its tray menu. The utility refuses to start capture while the main app is running.
2. Choose your microphone and click **Start Listening**. The app prepares the same verified local models as SharpVoice if necessary.
3. Click **Test Ask AI**, say the displayed phrase and question naturally, then stop speaking. Wait for the outcome and rearming.
4. Repeat **Test Dictation** in the same way. Try each phrase three to five times, including quiet speech and your normal speaking distance.
5. If nothing activates, click **Mark Missed**. Use **Room Noise Test** while staying quiet to check false activations. **Finish Attempt** ends a trial manually.
6. Click **Export Report ZIP**, review the ZIP, and send that file back for diagnosis. Note any audible/visible behavior that differs from the timeline.

The phrases, gate, silence duration and **selected Whisper model** initially use your saved preferences; changes here do not change SharpVoice's settings. The model name appears at the top and in the health/report data. Speech detection gates local Whisper checks; silence does not invoke transcription. The gate is the recording/transcription noise gate, not wake sensitivity. The window shows audio arrival and level, capture gaps, activity-worker backlog, Whisper check count/time, speech silence and lifecycle transitions; detailed packet positions and sample cursors are in the report. A wake hit alone does not prove speech ending worked: an automatic recording timeout is reported separately. Completed recordings also pass through final local transcription and the same mode resolution as the app; transcript contents are not logged. No confidence score is invented.

Audio saving is **off every time the utility launches**. Default reports contain technical metadata and your wake phrases, with no speech recordings, transcripts, full configuration, account credentials, device identifiers or device names. **Save Test Audio (Optional)** saves at most 20 guided-attempt clips, each at most 20 seconds. These clips can include other sounds/people in the room; listen to them before sharing. They are useful when a phrase repeatedly fails, but are not required for the first report.

Logs are flushed during the session under `%USERPROFILE%\.sharpvoice\diagnostics`. Export uses an explicit list of report files. **Open Log Folder** and **Export Report ZIP** remain available while stopping, including if native cleanup stalls. After five seconds, **Close** changes to **Force Close**; it exits only the diagnostic process. Already flushed logs remain available. Sessions stop after 20 minutes; event and clip storage are bounded within each session. Old session folders are retained until you delete them.

Stop or close the utility before restarting SharpVoice. The standalone test isolates the audio/detector lifecycle; success here does not prove that the full application's hotkey, provider, playback and insertion paths work together.

To rebuild on Windows with .NET 10 and Visual Studio x64 C++ tools:

```powershell
pwsh -File scripts/Build-WakeDiagnostics.ps1
```
