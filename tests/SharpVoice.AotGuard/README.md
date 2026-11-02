# Negative AOT gate fixture

This project is intentionally excluded from the solution. **Its Native AOT publish must fail** with IL2026/IL3050 from the vendored `ChatHistory.ToJson` implementation. It proves the IL-only binding project cannot hide reachable unsupported code from entrypoint ILC analysis.

Run from an x64 Visual Studio Developer Command Prompt:

```powershell
dotnet publish tests/SharpVoice.AotGuard/SharpVoice.AotGuard.csproj -c Release -r win-x64 -p:IlcUseEnvironmentalTools=true
```

Expected: nonzero exit and ILC errors IL2026/IL3050 naming `LLama.Common.ChatHistory.ToJson`. A generic toolchain/restore failure does not count as a pass. The normal app and test runner must publish successfully under the same strict warning settings.
