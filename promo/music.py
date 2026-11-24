"""Synthesizes an original soundtrack for the WinDock Reel and adds it to the video.

Everything is generated here (no samples, no downloaded music), so it is free to use on Instagram.
128 BPM -> one bar = 1.875 s; 18 bars = 33.75 s, the exact length of the video.
Sound effects line up with the scenes in render.py.

Usage:  python music.py      -> writes music.wav and WinDock-reel-music.mp4
"""
import os, subprocess, wave
import numpy as np

SR, BPM = 44100, 128
BEAT = 60 / BPM
BAR = BEAT * 4
BARS = 18
DUR = BAR * BARS
N = int(SR * DUR)
HERE = os.path.dirname(os.path.abspath(__file__))
rng = np.random.default_rng(11)

L = np.zeros(N); R = np.zeros(N)


def hz(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def add(sig, t0, gain=1.0, pan=0.0):
    i = int(t0 * SR)
    if i >= N:
        return
    sig = sig[: N - i] * gain
    L[i:i + len(sig)] += sig * np.sqrt(0.5 * (1 - pan))
    R[i:i + len(sig)] += sig * np.sqrt(0.5 * (1 + pan))


def tone(freq, dur, harmonics=((1, 1.0),), attack=0.005, decay=None, release=0.05, detune=0.0, glide=None):
    n = int(dur * SR); t = np.arange(n) / SR
    f = np.full(n, freq) if glide is None else freq * (glide ** (t / dur))
    ph = 2 * np.pi * np.cumsum(f) / SR * (1 + detune)
    s = sum(a * np.sin(ph * k) for k, a in harmonics)
    env = np.minimum(1, t / max(attack, 1e-4))
    if decay:
        env *= np.exp(-t / decay)
    rel = int(release * SR)
    if rel and rel < n:
        env[-rel:] *= np.linspace(1, 0, rel)
    return s * env


def band(x, lo, hi):
    a = np.convolve(x, np.ones(lo) / lo, mode="same")
    b = np.convolve(x, np.ones(hi) / hi, mode="same")
    return (a - b) * 2.5

# ---------------------------------------------------------------- harmony
F, G, Em, Am, G7, Dm, C = ((53, 57, 60, 64), 41), ((55, 59, 62, 64), 43), ((52, 55, 59, 62), 40), \
    ((57, 60, 64, 67), 45), ((55, 59, 62, 65), 43), ((50, 53, 57, 60), 38), ((48, 55, 60, 64, 74), 36)
CHORDS = [F, G, Em, Am, F, G, Em, Am, F, G, Em, Am, Dm, Em, F, G7, C, C]
WARM = ((1, 1.0), (2, 0.35), (3, 0.18), (4, 0.08), (5, 0.05))

for b, (notes, _) in enumerate(CHORDS):                       # pad
    if b == BARS - 1:
        continue
    dur = BAR * (2 if b == BARS - 2 else 1) + 0.6
    for m in notes:
        for det, pan in ((-0.0025, -0.6), (0.0025, 0.6)):
            add(tone(hz(m), dur, WARM, attack=0.35, release=0.6, detune=det), b * BAR, 0.032, pan)

for b, (_, root) in enumerate(CHORDS):                        # bass from bar 2
    if b == 0:
        continue
    last = b >= BARS - 2
    if last and b == BARS - 1:
        continue
    for beat in ([0] if last else [0, 2, 3.5] if b % 2 else [0, 2]):
        d = BAR * 2 if last else BEAT * (0.45 if beat == 3.5 else 1.8)
        add(tone(hz(root + (12 if beat == 3.5 else 0)), d, ((1, 1.0), (2, 0.25)), attack=0.01,
                 decay=2.0 if last else 0.9, release=0.1), b * BAR + beat * BEAT, 0.26 if beat != 3.5 else 0.14)

PATTERN = [0, 1, 2, 3, 2, 1, 2, 3]                            # arpeggio pluck, bars 1-16
for b, (notes, _) in enumerate(CHORDS[:16]):
    for k in range(8):
        m = notes[PATTERN[k] % len(notes)] + 12
        g = 0.09 if b else 0.09 * (0.4 + 0.6 * k / 8)
        add(tone(hz(m), 0.5, ((1, 1.0), (2, 0.5), (3, 0.25), (4, 0.1)), attack=0.002, decay=0.16),
            b * BAR + k * BEAT / 2, g, -0.35 if k % 2 else 0.35)

# lead melody, bars 9-16 (beats, midi, length in beats)
MEL = [(0, 76, 1.5), (1.5, 74, 0.5), (2, 72, 1), (3, 74, 1),
       (4, 74, 1.5), (5.5, 72, 0.5), (6, 71, 1), (7, 67, 1),
       (8, 71, 1.5), (9.5, 72, 0.5), (10, 74, 1), (11, 76, 1),
       (12, 72, 3), (15, 71, 1),
       (16, 69, 1.5), (17.5, 72, 0.5), (18, 74, 1), (19, 77, 1),
       (20, 76, 1.5), (21.5, 74, 0.5), (22, 71, 1), (23, 67, 1),
       (24, 69, 1), (25, 72, 1), (26, 76, 1), (27, 79, 1),
       (28, 77, 2), (30, 74, 2)]
for beat, m, ln in MEL:
    t0 = 8 * BAR + beat * BEAT
    add(tone(hz(m), ln * BEAT + 0.25, ((1, 1.0), (2, 0.12), (3, 0.06)), attack=0.02, decay=0.9, release=0.2), t0, 0.075, 0.1)
    add(tone(hz(m + 12), ln * BEAT + 0.25, ((1, 1.0),), attack=0.02, decay=0.5, release=0.2), t0, 0.02, -0.2)

# drums, bars 2-16; a last kick on the end card
def kick():
    n = int(0.45 * SR); t = np.arange(n) / SR
    f = 45 + 95 * np.exp(-t / 0.045)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.16)

def hat(length=0.06):
    n = int(length * SR)
    s = np.diff(rng.normal(0, 1, n + 1))
    return band(s * np.exp(-np.arange(n) / SR / (length / 4)), 2, 6)

def clap():
    n = int(0.22 * SR); t = np.arange(n) / SR
    env = np.exp(-t / 0.05) * (1 + 0.6 * (np.sin(2 * np.pi * 90 * t) > 0) * (t < 0.03))
    return band(rng.normal(0, 1, n), 4, 24) * env

for b in range(1, 16):
    for beat in range(4):
        t0 = b * BAR + beat * BEAT
        add(kick(), t0, 0.55)
        add(hat(), t0 + BEAT / 2, 0.035, 0.3)
        if b >= 8:
            add(hat(0.03), t0 + BEAT / 4 * 3, 0.018, -0.3)
        if beat in (1, 3):
            add(clap(), t0, 0.07, -0.1)
add(kick(), 16 * BAR, 0.55)

# ---------------------------------------------------------------- sound effects synced to the picture
def noise_swell(dur, k=60):
    n = int(dur * SR); t = np.arange(n) / SR
    s = np.convolve(rng.normal(0, 1, n), np.ones(k) / k, mode="same") * 6
    return s * np.sin(np.pi * t / dur) ** 2

def click():
    return tone(2400, 0.05, ((1, 1.0), (2, 0.3)), attack=0.0005, decay=0.008, release=0.005)

def blip(m, g=0.06, pan=0.0, t0=0.0, d=0.06):
    add(tone(hz(m), 0.3, ((1, 1.0), (2, 0.2)), attack=0.002, decay=d), t0, g, pan)

add(noise_swell(1.1), 1.3, 0.10)                                         # dock slides up
for t0, pan in ((7.25, -0.2), (10.2, -0.2), (16.05, 0.4), (20.5, 0.1), (28.5, 0.1)):
    add(click(), t0, 0.22, pan)                                          # clicks
for t0 in (8.15, 8.85):
    blip(84, 0.08, -0.2, t0)                                             # bounce landings
add(tone(hz(64), 0.5, ((1, 1.0), (2, 0.3)), attack=0.01, decay=0.25), 8.9, 0.05, 0.0)   # window opens
add(tone(hz(88), 0.8, ((1, 1.0), (2, 0.2)), attack=0.01, decay=0.3, glide=0.35), 10.3, 0.05, -0.2)  # genie
add(noise_swell(0.8, 30), 10.3, 0.05, -0.2)
blip(81, 0.05, -0.5, 13.4); blip(88, 0.04, -0.5, 13.48)                 # previews pop
for k, m in enumerate((76, 79, 83, 86, 88, 91)):                        # fan sparkle
    blip(m, 0.05, 0.5, 16.2 + k * 0.08, 0.18)
blip(83, 0.05, 0.5, 17.55); blip(86, 0.05, 0.5, 18.65)                  # grid, list
blip(79, 0.05, 0.1, 20.6, 0.04)                                          # pick up icon
add(noise_swell(0.45, 12), 21.5, 0.16, 0.0)                              # poof
add(noise_swell(0.6), 23.15, 0.09, 0.0)                                  # dock hides
add(noise_swell(0.6), 24.75, 0.09, 0.0)                                  # dock returns
add(tone(hz(84), 0.9, ((1, 1.0), (3, 0.1)), attack=0.005, decay=0.35), 25.8, 0.04, 0.3)  # settings opens
add(tone(hz(91), 0.9, ((1, 1.0),), attack=0.005, decay=0.35), 25.88, 0.03, 0.3)
for k in range(10):                                                      # slider ticks
    blip(72 + k, 0.025, 0.2, 26.8 + k * 0.1, 0.02)
add(tone(hz(55), 0.6, ((1, 1.0), (2, 0.3)), attack=0.005, decay=0.2), 28.5, 0.05, 0.0)  # dark mode
for m in (72, 76, 79, 86):                                               # end card shimmer
    add(tone(hz(m + 12), 2.5, ((1, 1.0), (2, 0.15)), attack=0.02, decay=1.0), 30.2, 0.03, 0.0)

# ---------------------------------------------------------------- space, fades, level
def reverb(x):
    out = x.copy()
    for d, g in ((0.031, 0.32), (0.047, 0.28), (0.073, 0.22), (0.113, 0.18), (0.167, 0.14), (0.241, 0.10), (0.353, 0.07)):
        k = int(d * SR)
        out[k:] += x[:-k] * g
    return out

L, R = reverb(L), reverb(np.roll(R, int(0.011 * SR)))
t = np.arange(N) / SR
fade = np.minimum(1, t / 0.6) * np.minimum(1, (DUR - t) / 1.2)
L *= fade; R *= fade
peak = max(np.abs(L).max(), np.abs(R).max())
stereo = np.clip(np.stack([L, R], 1) / peak * 0.89, -1, 1)

wav = os.path.join(HERE, "music.wav")
with wave.open(wav, "wb") as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((stereo * 32767).astype("<i2").tobytes())
print(f"music.wav  {DUR:.2f}s  peak {20*np.log10(np.abs(stereo).max()):.1f} dBFS")

import imageio_ffmpeg
ff = imageio_ffmpeg.get_ffmpeg_exe()
import sys
name = "WinDock-wide" if "--wide" in sys.argv else "WinDock-reel"
src, out = os.path.join(HERE, name + ".mp4"), os.path.join(HERE, name + "-music.mp4")
subprocess.run([ff, "-hide_banner", "-loglevel", "error", "-y", "-i", src, "-i", wav,
                "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-ar", "44100",
                "-shortest", "-movflags", "+faststart", out], check=True,
               stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
print(out)
