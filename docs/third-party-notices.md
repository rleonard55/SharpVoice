# Third-party inference software

The managed Whisper.net and LLamaSharp bindings are built from [pinned, locally patched upstream sources](../vendor/README.md). Their native runtime versions are unchanged.

SharpVoice embeds the Windows CPU runtimes from Whisper.net.Runtime 1.9.1 and LLamaSharp.Backend.Cpu 0.30.0 for one-file distribution. The executable contains the license notices below as resources; keep these notices when redistributing source or binaries.

- [Whisper.net license](licenses/Whisper.net-LICENSE)
- [whisper.cpp / ggml license](licenses/whisper.cpp-LICENSE)
- [LLamaSharp license](licenses/LLamaSharp-LICENSE)
- [llama.cpp / ggml license](licenses/llama.cpp-LICENSE)
- [Silero VAD license](licenses/silero-vad-LICENSE): the separately downloaded speech-end model used by wake recording. Download and usage follow the [sherpa-onnx Silero documentation](https://k2-fsa.github.io/sherpa/onnx/vad/silero-vad.html); SharpVoice verifies the model size and SHA-256 before installation.

Whisper and Qwen model weights are downloaded separately. See the upstream [Whisper model repository](https://huggingface.co/ggerganov/whisper.cpp) and [Qwen model card/license](https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF). Qwen's model card specifies Apache-2.0.

The portable AOT build also embeds the designated x64 Visual C++ runtime/OpenMP redistributable binaries from the installed Visual Studio toolchain's Redist directory. See the embedded [Microsoft component notice](licenses/Microsoft-CPP-LICENSE). These are Microsoft components under the applicable Visual Studio license terms, separate from the MIT inference libraries. See [Microsoft redistribution guidance](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files?view=msvc-170) and the [Visual Studio license directory](https://visualstudio.microsoft.com/license-terms/). Builds require the C++ developer environment to locate the redistributable files.
