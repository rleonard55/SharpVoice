"""Generate short PCM cues. Package branded icons with build-brand-icons.ps1."""
from pathlib import Path
import math
import struct
import sys
import wave

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src/SharpVoice.UI/Assets"
ASSETS.mkdir(parents=True, exist_ok=True)


def insertion_success():
    """A quiet 140 ms bell, with a soft attack and decaying harmonics."""
    rate, seconds = 44100, .14
    samples = []
    for i in range(round(rate * seconds)):
        t = i / rate
        envelope = min(1, t / .004, (seconds - t) / .015)
        fundamental = math.sin(2 * math.pi * 1046.5 * t) * math.exp(-t / .045)
        overtone = .20 * math.sin(2 * math.pi * 2093 * t) * math.exp(-t / .027)
        shimmer = .08 * math.sin(2 * math.pi * 3139.5 * t) * math.exp(-t / .018)
        samples.append(round(.045 * envelope * (fundamental + overtone + shimmer) * 32767))
    with wave.open(str(ASSETS / "insertion-success.wav"), "wb") as target:
        target.setnchannels(1)
        target.setsampwidth(2)
        target.setframerate(rate)
        target.writeframes(struct.pack(f"<{len(samples)}h", *samples))


# Generate this addition without rewriting existing reviewed cues/icons.
if "--only-insertion-success" in sys.argv:
    insertion_success()
    print("Generated subdued insertion success cue.")
    raise SystemExit(0)


def chime(name, notes):
    rate = 44100
    samples = []
    for frequency, seconds in notes:
        for i in range(round(rate * seconds)):
            t = i / rate
            envelope = min(1, t / .004, (seconds - t) / .018)
            value = 0 if frequency == 0 else .12 * envelope * (math.sin(2 * math.pi * frequency * t) + .12 * math.sin(4 * math.pi * frequency * t))
            samples.append(round(value * 32767))
    with wave.open(str(ASSETS / f"{name}.wav"), "wb") as target:
        target.setnchannels(1)
        target.setsampwidth(2)
        target.setframerate(rate)
        target.writeframes(struct.pack(f"<{len(samples)}h", *samples))


# Generate the question cue alone without changing reviewed sounds.
if "--only-follow-up-question" in sys.argv:
    chime("follow-up-question", ((784, .085), (0, .065), (1174.66, .12)))
    print("Generated distinct follow-up question cue.")
    raise SystemExit(0)

chime("recording-start", ((660, .055), (0, .01), (880, .065)))
chime("recording-stop", ((660, .055), (0, .01), (440, .065)))
chime("warning", ((330, .07), (0, .03), (330, .07)))
insertion_success()
chime("follow-up-question", ((784, .085), (0, .065), (1174.66, .12)))

print("Generated recording, warning and insertion-success PCM cues; branded icons are preserved.")
