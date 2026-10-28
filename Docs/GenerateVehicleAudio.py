"""Rebuild the original driving and regional impact sounds (Python stdlib only).

Run from any directory. All audio is synthesized, with no external samples.
Loop filters are warmed through complete periods to keep their boundary continuous.
"""

import array
import math
from pathlib import Path
import random
import uuid
import wave


RATE = 44100
TAU = math.tau
DEST = Path(__file__).resolve().parents[1] / "Assets/Resources/Audio/BumperCars"
RANDOM = random.Random(27102026)


def lowpass(samples, cutoff, periodic=False):
    coefficient = 1 - math.exp(-TAU * cutoff / RATE)
    state = 0.0
    if periodic:
        for value in samples:
            state += coefficient * (value - state)
    result = []
    for value in samples:
        state += coefficient * (value - state)
        result.append(state)
    return result


def noise(length, low, high, periodic=False):
    raw = [RANDOM.uniform(-1, 1) for _ in range(length)]
    top = lowpass(raw, high, periodic)
    bottom = lowpass(raw, low, periodic)
    return [a - b for a, b in zip(top, bottom)]


def save(name, samples, peak=0.82, rms=None):
    maximum = max(abs(value) for value in samples)
    gain = peak / max(0.00001, maximum)
    if rms is not None:
        measured = math.sqrt(sum(value * value for value in samples) / len(samples))
        gain = min(gain, rms / max(0.00001, measured))
    samples = [value * gain for value in samples]
    integers = array.array("h", (round(value * 32767) for value in samples))
    path = DEST / (name + ".wav")
    with wave.open(str(path), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(integers.tobytes())
    meta = path.with_suffix(".wav.meta")
    if not meta.exists():
        template = (DEST / "Impact.wav.meta").read_text(encoding="utf-8")
        old_guid = next(line for line in template.splitlines() if line.startswith("guid:"))
        template = template.replace(old_guid, "guid: " + uuid.uuid5(uuid.NAMESPACE_URL,
            "bumper-cars-original-audio/" + name).hex)
        template = template.replace("Original synthesized audio; see AUDIO_NOTES.txt",
            "Original synthesized vehicle audio; see Docs/GenerateVehicleAudio.py")
        meta.write_text(template, encoding="utf-8")
    measured_rms = math.sqrt(sum(value * value for value in samples) / len(samples))
    boundary = abs(samples[0] - samples[-1])
    print(f"{name}: {len(samples) / RATE:.2f}s, RMS {measured_rms:.4f}, "
          f"peak {max(abs(v) for v in samples):.4f}, boundary {boundary:.6f}")


def motor():
    # All oscillations complete an integer number of cycles within four seconds.
    # The phase modulation produces a warm electric motor with a mechanical purr.
    length = RATE * 4
    air = noise(length, 220, 1600, periodic=True)
    result = []
    for i in range(length):
        t = i / RATE
        phase = TAU * 64 * t + 0.13 * math.sin(TAU * 3 * t)
        body = (0.40 * math.sin(phase) + 0.30 * math.sin(phase * 2 + 0.3)
                + 0.15 * math.sin(phase * 3 + 0.8)
                + 0.08 * math.sin(phase * 5 + 0.4)
                + 0.025 * math.sin(phase * 9))
        purr = 0.94 + 0.04 * math.sin(TAU * 8 * t) + 0.02 * math.sin(TAU * 1.25 * t)
        result.append(body * purr + 0.045 * air[i])
    save("EngineLoop", result, peak=0.73, rms=0.24)


def rolling():
    length = RATE * 4
    road = noise(length, 65, 1550, periodic=True)
    grit = noise(length, 900, 2600, periodic=True)
    result = []
    for i in range(length):
        t = i / RATE
        tread = 0.76 + 0.12 * math.sin(TAU * 6 * t) + 0.06 * math.sin(TAU * 11.25 * t)
        rubber = 0.035 * math.sin(TAU * 88 * t + 0.05 * math.sin(TAU * 2 * t))
        result.append(road[i] * tread + 0.12 * grit[i] + rubber)
    # Choose a quiet, gently changing point in the circular noise for the splice.
    # This also avoids an abrupt transient when the loop is first started.
    splice = min(range(length), key=lambda i:
                 abs(result[i]) + 2 * abs(result[i] - result[i - 1]))
    result = result[splice:] + result[:splice]
    save("TireRollLoop", result, peak=0.70, rms=0.17)


def impact(name, duration, kind):
    length = round(RATE * duration)
    thud = noise(length, 60, 1700)
    snap = noise(length, 1000, 8000)
    clatter = noise(length, 350, 3600)
    result = []
    for i in range(length):
        t = i / RATE
        attack = 1 - math.exp(-t / 0.0008)
        end = min(1.0, (duration - t) / 0.055)
        if kind == "front":
            # Firm rubber bumper slap, followed by a short steel chassis clonk.
            body = (0.64 * math.sin(TAU * (126 * t + 1.6 * (1 - math.exp(-26 * t)))) * math.exp(-17 * t)
                    + 0.25 * math.sin(TAU * 284 * t + 0.3) * math.exp(-23 * t)
                    + 0.13 * math.sin(TAU * 618 * t) * math.exp(-35 * t))
            signal = body + 1.05 * thud[i] * math.exp(-40 * t) + 0.22 * snap[i] * math.exp(-105 * t)
        elif kind == "side":
            # Thin side panel with a sharper metallic knock and little loose rattle.
            body = (0.34 * math.sin(TAU * 238 * t) * math.exp(-20 * t)
                    + 0.37 * math.sin(TAU * 487 * t + 0.5) * math.exp(-16 * t)
                    + 0.23 * math.sin(TAU * 863 * t) * math.exp(-25 * t)
                    + 0.08 * math.sin(TAU * 1437 * t) * math.exp(-32 * t))
            rattle = (0.35 + 0.65 * math.sin(TAU * 41 * t) ** 4)
            signal = (body + 0.60 * thud[i] * math.exp(-33 * t)
                      + 0.28 * snap[i] * math.exp(-82 * t)
                      + 0.45 * clatter[i] * math.exp(-13 * t) * rattle)
        elif kind == "rear":
            # Hollow rear casing: lower and longer than the front, with a dull ring.
            body = (0.63 * math.sin(TAU * (91 * t + 0.5 * (1 - math.exp(-30 * t)))) * math.exp(-11 * t)
                    + 0.31 * math.sin(TAU * 176 * t + 0.6) * math.exp(-14 * t)
                    + 0.16 * math.sin(TAU * 347 * t) * math.exp(-13 * t)
                    + 0.06 * math.sin(TAU * 594 * t) * math.exp(-24 * t))
            signal = body + 0.90 * thud[i] * math.exp(-32 * t) + 0.16 * snap[i] * math.exp(-90 * t)
        else:
            # Shared damage layer: deep body compression and weight, no explosion.
            body = (0.77 * math.sin(TAU * (49 * t + 0.95 * (1 - math.exp(-23 * t)))) * math.exp(-9 * t)
                    + 0.42 * math.sin(TAU * 83 * t + 0.3) * math.exp(-12 * t)
                    + 0.20 * math.sin(TAU * 159 * t) * math.exp(-17 * t))
            signal = (body + 0.70 * thud[i] * math.exp(-29 * t)
                      + 0.12 * snap[i] * math.exp(-86 * t)
                      + 0.12 * clatter[i] * math.exp(-12 * t))
        result.append(math.tanh(signal * 1.7) * attack * end)
    save(name, result, peak=0.84)


if __name__ == "__main__":
    motor()
    rolling()
    impact("ImpactFront", 0.44, "front")
    impact("ImpactSide", 0.46, "side")
    impact("ImpactRear", 0.60, "rear")
    impact("ImpactHeavy", 0.70, "heavy")
