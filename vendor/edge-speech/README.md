# Private Edge speech runtime

`bundle.lock.json` pins the official Python Windows x64 embeddable runtime and PyPI wheels by version, download URL and SHA-256. `python scripts/bundle-edge-speech.py` rebuilds `src/SharpVoice.Platform.Windows/Assets/edge-speech.zip` deterministically, verifying every upstream archive. No package manager or system Python installation is used at runtime.

The runtime is extracted only for spoken answers into SharpVoice's private versioned cache. The interpreter runs with `-I`, the private `python313._pth` and vendored packages. Answer text stays on stdin; no source text is put in command arguments. Windows speech remains the fallback.

Upstream: https://github.com/rany2/edge-tts and https://www.python.org/ . This is an unofficial Edge speech client; answer text is sent to Microsoft's online speech service. No Microsoft service availability guarantee is implied.

Licenses are preserved inside the embedded archive: Python `LICENSE.txt` and each package's `.dist-info` LICENSE/licenses files (including edge-tts LGPLv3, certifi MPL2 and aiohttp/other dependency licenses). Original source/wheels remain available at the exact URLs in the lock file. The Python source release matching the runtime is https://www.python.org/ftp/python/3.13.12/Python-3.13.12.tar.xz . Vendored client code is unmodified.
