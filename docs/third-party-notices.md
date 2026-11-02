# Third-party inference software

SharpVoice-owned source code is covered by the repository's [MIT license](../LICENSE). That grant does not replace the separate licenses of vendored code, model weights, runtimes or artwork. Keep their copyright notices and applicable license texts when redistributing them.

The embedded Edge speech helper includes Python, edge-tts (LGPLv3), certifi (MPL-2.0) and other dependencies under their own licenses. Their license files are retained inside the speech archive. See the [speech runtime inventory and source references](../vendor/edge-speech/README.md). Cloud-provider and speech-service access remains subject to the respective service terms.

The Windows installer and GitHub update integration use [Velopack 1.2.161](https://github.com/velopack/velopack), under its [MIT license](licenses/Velopack-LICENSE). Release packages include this notice and the license texts. Velopack's updater/installer components retain their upstream notices.

The managed Whisper.net and LLamaSharp bindings are built from [pinned, locally patched upstream sources](../vendor/README.md). Their native runtime versions are unchanged.

SharpVoice embeds the Windows CPU runtimes from Whisper.net.Runtime 1.9.1 and LLamaSharp.Backend.Cpu 0.30.0 for one-file distribution. The executable contains the license notices below as resources; keep these notices when redistributing source or binaries.

- [Whisper.net license](licenses/Whisper.net-LICENSE)
- [whisper.cpp / ggml license](licenses/whisper.cpp-LICENSE)
- [LLamaSharp license](licenses/LLamaSharp-LICENSE)
- [llama.cpp / ggml license](licenses/llama.cpp-LICENSE)
- [Silero VAD license](licenses/silero-vad-LICENSE): the separately downloaded speech-end model used by wake recording. Download and usage follow the [sherpa-onnx Silero documentation](https://k2-fsa.github.io/sherpa/onnx/vad/silero-vad.html); SharpVoice verifies the model size and SHA-256 before installation.

Whisper and Qwen model weights are downloaded separately. See the upstream [Whisper model repository](https://huggingface.co/ggerganov/whisper.cpp) and [Qwen model card/license](https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF). Qwen's model card specifies Apache-2.0.

The portable AOT build also embeds the designated x64 Visual C++ runtime/OpenMP redistributable binaries from the installed Visual Studio toolchain's Redist directory. See the embedded [Microsoft component notice](licenses/Microsoft-CPP-LICENSE). These are Microsoft components under the applicable Visual Studio license terms, separate from the MIT inference libraries. See [Microsoft redistribution guidance](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files?view=msvc-170) and the [Visual Studio license directory](https://visualstudio.microsoft.com/license-terms/). Builds require the C++ developer environment to locate the redistributable files.
