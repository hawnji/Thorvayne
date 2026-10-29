import numpy as np, sys, math
from PIL import Image
d = sys.argv[1]; out = sys.argv[2]
n = 1281; ws = 5000.0
H = np.fromfile(f"{d}/height_{n}.f32", dtype=np.float32).reshape(n, n)   # row 0 = south
C = np.asarray(Image.open(f"{d}/map.ppm").convert("RGB"))[::-1].astype(np.float32)  # flip to row 0 = south
sea = 40.0
Hw = np.maximum(H, sea)  # water surface flat at sea level
W, Ht = 1920, 1080
img = np.zeros((Ht, W, 3), np.float32)
# sky gradient
for y in range(Ht):
    t = y / Ht
    img[y] = np.array([0.55, 0.72, 0.9]) * (1 - t) + np.array([0.85, 0.9, 0.95]) * t
img *= 255
ybuf = np.full(W, Ht, np.float32)
cam = np.array([2500.0 * n / ws, -650.0 * n / ws])  # grid units, south of the map
camh = 1300.0; horizon = 60.0; scale = 260.0
ang = math.radians(0)  # looking north
sx = np.linspace(-1, 1, W) * 0.75
dz = 1.0; z = 20.0
rows = np.arange(Ht)[:, None]
while z < 2.2 * n:
    # points along the slice
    px = cam[0] + sx * z
    py = cam[1] + z
    xi = np.clip(px.astype(int), 0, n - 1); yi = np.clip(py.astype(int), 0, n - 1)
    inside = (px >= 0) & (px < n) & (py >= 0) & (py < n)
    h = np.where(inside, Hw[yi, xi], sea)
    col = np.where(inside[:, None], C[yi, xi], np.array([40, 95, 140], np.float32))
    ys = (camh - h * 1.15) / z * scale + horizon
    ys = np.floor(np.clip(ys, 0, Ht))
    lo = int(ys.min()); hi = int(ybuf.max())
    if hi > lo:
        r = rows[lo:hi]
        m = (r >= ys[None, :]) & (r < ybuf[None, :])
        fog = min(1.0, (z / (2.2 * n)) ** 1.6)
        c = col * (1 - fog * 0.65) + np.array([200, 215, 230]) * fog * 0.65
        seg = img[lo:hi]
        seg[m] = np.broadcast_to(c[None, :, :], (hi - lo, W, 3))[m]
    ybuf = np.minimum(ybuf, ys)
    z += dz; dz *= 1.002
Image.fromarray(img.clip(0, 255).astype(np.uint8)).save(out)
