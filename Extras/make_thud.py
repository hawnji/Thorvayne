# Makes the wood-chop hit sounds (Resources/Audio/WoodThud1..3.wav): a sharp "tock" as the blade bites, the knock of
# the trunk (a few short wooden resonances) and a soft low thump. Three slightly different takes, so repeated
# swings don't sound identical. Plain Python + numpy.
# Usage: python make_thud.py out_dir
import sys, wave, numpy as np
out = sys.argv[1]
SR = 44100
for take in range(3):
    rng = np.random.default_rng(10 + take)
    t = np.arange(int(SR * 0.42)) / SR
    tock = rng.normal(size=t.size) * np.exp(-t / 0.004)                              # blade bite: short noise burst
    tock = np.convolve(tock, [1, -0.9], 'same')                                       # brighter (high-passed)
    body = sum(a * np.sin(2 * np.pi * f * (1 + rng.uniform(-0.04, 0.04)) * t + rng.uniform(0, 6)) * np.exp(-t / d)
               for f, a, d in ((165, 1.0, 0.07), (310, 0.7, 0.05), (590, 0.45, 0.035), (1040, 0.25, 0.02), (1720, 0.12, 0.012)))
    thump = np.sin(2 * np.pi * 78 * t) * np.exp(-t / 0.06)
    s = 0.55 * tock + body + 0.6 * thump
    s *= np.minimum(1, t / 0.0008)                                                    # no click at the start
    s = s / np.abs(s).max() * 0.9
    with wave.open(f'{out}/WoodThud{take + 1}.wav', 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((s * 32767).astype('<i2').tobytes())
    print('WoodThud%d.wav' % (take + 1), round(t[-1], 2), 's')
