"""Turn the Thorvayne world map picture into Unity terrain data.

Usage: python3 -I build.py <map.jpg> <out_dir>
Outputs (all rows ordered south -> north, columns west -> east, i.e. Unity order):
  thorvayne_height.r16    uint16 LE, HM x HM
  thorvayne_splat.bytes   uint8, AM x AM x LAYERS (layer fastest)
  thorvayne_trees.bytes   uint8, TM x TM x 3 (conifer, broadleaf, snowpine density)
  thorvayne_world.json    sizes, layer list, markers
  Textures/*.png          tileable layer textures
  previews
"""
import json, os, sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

SRC, OUT = sys.argv[1], sys.argv[2]
os.makedirs(os.path.join(OUT, 'Textures'), exist_ok=True)
rng = np.random.default_rng(7)

WORLD_W, WORLD_L = 2048.0, 1536.0     # metres; 1 map pixel = 1 m
HEIGHT_SCALE = 320.0                  # terrain max height (m)
SEA = 30.0                            # sea level (m)
HM, AM, TM = 1025, 1024, 512
W, H = 1024, 768                      # working grid (2 m per cell)

full = Image.open(SRC).convert('RGB')
a = np.asarray(full.resize((W, H), Image.LANCZOS)).astype(float)
r, g, b = a[..., 0], a[..., 1], a[..., 2]

# ---------------- water ----------------
water = ((g - r) >= 14) & (b >= r - 2)
water = ndi.binary_opening(water, iterations=1)
water = ndi.binary_closing(water, iterations=2)
water[:3, :] = water[-3:, :] = True
water[:, :3] = water[:, -3:] = True

# ---------------- biome colour classes ----------------
blur = np.stack([ndi.gaussian_filter(a[..., c], 6) for c in range(3)], -1)
CENT = {'grass': (150, 167, 75), 'meadow': (128, 116, 60), 'darkmeadow': (95, 84, 32),
        'barren': (73, 58, 32), 'dry': (145, 128, 88), 'sand': (192, 174, 112),
        'scorch': (205, 125, 60), 'snow': (205, 210, 212), 'snow2': (160, 166, 166),
        'rock': (58, 66, 68), 'rock2': (80, 88, 90), 'forest': (86, 86, 43)}
names = list(CENT)
C = np.array([CENT[k] for k in names], float)
cls = ((blur[..., None, :] - C[None, None]) ** 2).sum(-1).argmin(-1)
def M(*ks): return np.isin(cls, [names.index(k) for k in ks])

lab0, _ = ndi.label(water)
open_sea = np.isin(lab0, list(set(np.unique(np.r_[lab0[0], lab0[-1], lab0[:, 0], lab0[:, -1]])) - {0}))
open_sea = ndi.binary_opening(open_sea, iterations=3)
snow_region = ndi.binary_opening(M('snow', 'snow2') & ~open_sea, iterations=3)
snow_region = ndi.binary_closing(snow_region, iterations=6)
lab, n = ndi.label(snow_region)
sizes = ndi.sum(snow_region, lab, range(1, n + 1))
snow_region = lab == (1 + int(np.argmax(sizes)))          # the NW snowfield only
snow_region = ndi.binary_fill_holes(snow_region) & ~open_sea
dark_region = ndi.binary_closing(ndi.binary_opening(M('rock', 'rock2'), iterations=3), iterations=4)
lab, n = ndi.label(dark_region)
sizes = ndi.sum(dark_region, lab, range(1, n + 1))
dark_region = ndi.binary_fill_holes(lab == (1 + int(np.argmax(sizes))))

water &= ~snow_region   # icons / frozen lake inside the snowfield are not open water
water &= ~dark_region | ndi.binary_erosion(water, iterations=3)

lab, n = ndi.label(water)
border = set(np.unique(np.r_[lab[0], lab[-1], lab[:, 0], lab[:, -1]])) - {0}
connected = np.isin(lab, list(border))
ocean = ndi.binary_opening(connected, iterations=5) & connected
ocean = ndi.binary_dilation(ocean, iterations=2) & connected       # keep the wide sea, drop river mouths
lakes_core = ndi.binary_opening(water & ~ocean, iterations=4)
lab, n = ndi.label(lakes_core)
lakes = np.zeros_like(water)
for i in range(1, n + 1):
    m = lab == i
    if m.sum() > 150:
        lakes |= m
lakes = ndi.binary_dilation(lakes, iterations=4) & water & ~ocean
rivers = water & ~ocean & ~lakes
lab, n = ndi.label(rivers)
sizes = ndi.sum(rivers, lab, range(1, n + 1))
rivers = np.isin(lab, [i + 1 for i, v in enumerate(sizes) if v >= 60])
land = ~ocean & ~lakes & ~rivers

# ---------------- texture (forests) ----------------
lum = a.mean(2)
mu = ndi.uniform_filter(lum, 7)
std = np.sqrt(np.maximum(ndi.uniform_filter(lum ** 2, 7) - mu ** 2, 0))
std = ndi.gaussian_filter(std, 3)
coast_d = ndi.distance_transform_edt(land)
forest = (std > 11) & land & (coast_d > 6) & ~dark_region
forest = ndi.binary_opening(forest, iterations=4)
forest &= ~M('sand', 'scorch')
lab, n = ndi.label(forest)
sizes = ndi.sum(forest, lab, range(1, n + 1))
forest = np.isin(lab, [i + 1 for i, v in enumerate(sizes) if v >= 120])
ys = np.arange(H)[:, None] / H
mountains = snow_region & (ys < 0.17) & (std > 9)     # mountain icons in the upper snowfield
mountains = ndi.binary_closing(mountains, iterations=6)
snowpine = forest & snow_region & ~mountains | (snow_region & (std > 9) & (ys >= 0.15) & (ys < 0.26))
snowpine &= ~mountains
forest &= ~snow_region
broadleaf = forest & ((blur[..., 0] - blur[..., 1]) > 4)   # brown/autumn woods
conifer = forest & ~broadleaf

# ---------------- roads ----------------
fa = np.asarray(full).astype(int)
mn, mx = fa.min(2), fa.max(2)
white = (mn > 195) & (mx - mn < 40)
lab, n = ndi.label(white)
sizes = ndi.sum(white, lab, range(1, n + 1))
dots = np.isin(lab, [i + 1 for i, v in enumerate(sizes) if 3 <= v <= 60])
dots = ndi.binary_dilation(dots, iterations=5)
roads = np.asarray(Image.fromarray((dots * 255).astype(np.uint8)).resize((W, H), Image.BILINEAR)) > 150
roads &= ~ndi.binary_dilation(snow_region, iterations=2) | (ys > 0.2)
img = Image.fromarray((roads * 255).astype(np.uint8))
dr = ImageDraw.Draw(img)
s = W / 2000.0  # display coords of the elven road through the snow
dr.line([(555 * s, 175 * s), (585 * s, 230 * s), (650 * s, 300 * s), (660 * s, 380 * s), (632 * s, 410 * s)], fill=255, width=3)
roads = (np.asarray(img) > 0) & land
roads = ndi.binary_closing(roads, iterations=2) & land

# ---------------- noise helpers ----------------
def vnoise(cell, octaves=4, seed=0):
    rr = np.random.default_rng(seed)
    out = np.zeros((H, W)); amp, tot = 1.0, 0.0
    for o in range(octaves):
        c = max(2, int(cell / 2 ** o))
        grid = rr.random((H // c + 3, W // c + 3))
        z = ndi.zoom(grid, c, order=3)[:H, :W]
        out += amp * z; tot += amp; amp *= 0.5
    return out / tot  # ~0..1

def soft(mask, sigma): return ndi.gaussian_filter(mask.astype(float), sigma)
def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)

# ---------------- heights (metres) ----------------
water_any = ~land
d_water = ndi.distance_transform_edt(~water_any)          # cells from any water
d_ocean = ndi.distance_transform_edt(~ocean)
d_landfromsea = ndi.distance_transform_edt(ocean)
valley = smoothstep(0, 45, ndi.distance_transform_edt(~(rivers | lakes)))

n1 = vnoise(96, 4, 1); n2 = vnoise(48, 4, 2); n3 = vnoise(160, 3, 3)
ridged = 1 - np.abs(vnoise(40, 4, 4) * 2 - 1)

h = SEA + 1.2 + np.minimum(d_ocean * 0.45, 9.0)          # beaches -> lowlands
h += (n1 - 0.5) * 10 * smoothstep(5, 40, d_ocean)          # gentle rolling everywhere

interior = lambda m, k: smoothstep(0, k, ndi.distance_transform_edt(m))
# snowfield: plateau + mountain range in the north
sf = soft(snow_region, 10)
h += sf * 35
mt = soft(mountains, 14)
h += mt * (60 + 150 * ridged * smoothstep(0.1, 0.8, n2 + 0.3))
h += sf * (1 - mt) * 15 * ridged
# dark wastes: high broken plateau with a cliff wall on its edge
dk = soft(dark_region, 2.5) * interior(dark_region, 25)
h += soft(dark_region, 3) * 40 + dk * (35 + 55 * ridged * n3)
# NE barrens (brown): high hills
barren = ndi.binary_opening(M('barren'), iterations=4) & land
h += soft(barren, 18) * (30 + 40 * n2) * valley
# west brown wood / dark meadows: rolling hills
west = ndi.binary_opening(M('darkmeadow') | broadleaf, iterations=3) & land
h += soft(west, 20) * (12 + 18 * n2) * valley
# forests sit on low hills
h += soft(conifer, 12) * (6 + 10 * n2) * valley
# desert dunes
desert = ndi.binary_opening(M('sand', 'scorch'), iterations=3) & land & ~ndi.binary_dilation(ocean, iterations=8)
dsoft = soft(desert, 10)
yy, xx = np.mgrid[0:H, 0:W]
dunes = (np.sin((xx * 0.8 + yy * 0.6) / 9.0 + n1 * 6) * 0.5 + 0.5) ** 2
h += dsoft * (4 + 7 * dunes)
# central starter grassland: keep it soft and inviting (pull toward a gentle level)
grass = ndi.binary_opening(M('grass'), iterations=3) & land
gs = soft(grass, 25)
h = h * (1 - 0.5 * gs) + gs * 0.5 * (SEA + 8 + 5 * n1)
# islands get hills
isl_lab, nl = ndi.label(land | rivers | lakes)
isl_sizes = ndi.sum(np.ones_like(land), isl_lab, range(1, nl + 1))
mainland_id = 1 + int(np.argmax(isl_sizes))
islands = (isl_lab > 0) & (isl_lab != mainland_id)
h += soft(islands, 6) * interior(islands, 40) * (10 + 25 * n2)

h = ndi.gaussian_filter(h, 1.2)
# roads: flatten a little toward a smoothed version
hs = ndi.gaussian_filter(h, 4)
rw = np.clip(soft(roads, 1) * 1.6, 0, 1)
h = h * (1 - 0.7 * rw) + hs * 0.7 * rw

# ---------------- markers ----------------
def P(x, y): return [round(x / 2000, 4), round(1 - y / 1500, 4)]   # display px -> (u east, v north)
markers = [
    ('Starter Village', 'village', 1120, 720, 1, True),
    ('North Crossroads Town', 'town', 625, 420, 2, False),
    ('North Road Town', 'town', 955, 415, 2, False),
    ('West Crossroads Hamlet', 'village', 840, 630, 1, False),
    ('East Meadow Hamlet', 'village', 1375, 545, 1, False),
    ('East Town', 'town', 1610, 750, 2, False),
    ('Southeast Crossroads Hamlet', 'village', 1295, 945, 2, False),
    ('South Central Town', 'town', 945, 970, 2, False),
    ('Southeast City', 'city', 1560, 1170, 3, False),
    ('Southeast Coast Town', 'town', 1820, 1110, 3, False),
    ('South Town', 'town', 1275, 1295, 3, False),
    ('Southeast Fishing Hamlet', 'village', 1810, 1365, 3, False),
    ('West Peninsula Town', 'town', 595, 880, 3, False),
    ('Far West Town', 'town', 115, 940, 3, False),
    ('Northwest Town', 'town', 245, 375, 3, False),
    ('Northeast Town', 'town', 1520, 235, 3, False),
    ('Far Northeast Town', 'town', 1750, 140, 4, False),
    ('West Castle', 'castle', 215, 720, 3, False),
    ('Northeast Forest Castle', 'castle', 1810, 445, 4, False),
    ('Dark Wastes Castle', 'castle', 950, 115, 5, False),
    ('Elven Spire', 'spire', 555, 160, 4, False),
    ('Dark Wastes War Camp', 'warcamp', 1240, 325, 5, False),
    ('Scorched Sands War Camp', 'warcamp', 740, 1225, 5, False),
    ('East Isle Fortress', 'fortress', 1860, 765, 4, False),
    ('Southwest Isle Fortress', 'fortress', 200, 1330, 4, False),
    ('Frozen Coast Tower', 'tower', 535, 30, 4, False),
    ('Northeast Cape Tower', 'tower', 1520, 30, 4, False),
    ('Dark Wastes Tower', 'tower', 1070, 210, 5, False),
    ('West Coast Tower', 'tower', 60, 515, 3, False),
    ('Brown Wood Tower', 'tower', 500, 600, 3, False),
    ('Southwest Isle Tower', 'tower', 325, 1160, 4, False),
    ('Desert Coast Tower', 'tower', 1068, 1375, 4, False),
]
# settlements sit on flattened pads
FLAT_R = {'village': 18, 'town': 22, 'city': 34, 'castle': 22, 'fortress': 20, 'warcamp': 20, 'spire': 12, 'tower': 7}
for name_, t, x, y, d_, s_ in markers:
    cx, cy = x / 2000 * W, y / 1500 * H
    R = FLAT_R[t]
    dist = np.hypot(xx - cx, yy - cy)
    sel = dist < R * 0.6
    if not sel.any(): continue
    target = max(np.median(h[sel]), SEA + 2.5)
    wgt = 1 - smoothstep(R * 0.6, R * 1.3, dist)
    h = h * (1 - wgt) + target * wgt
    rw = np.maximum(rw, (1 - smoothstep(R * 0.35, R * 0.7, dist)) * 0.6)
# carve water: sea floor, lake beds, river channels
sea_bed = SEA - np.clip(2 + d_landfromsea * 0.35, 2, 22)
h = np.where(ocean, sea_bed, h)
lake_d = ndi.distance_transform_edt(lakes)
lake_bed = SEA - np.clip(1.5 + lake_d * 0.4, 1.5, 8)
river_bed = np.full_like(h, SEA - 1.8)
bed = np.where(lakes, lake_bed, river_bed)
inland_w = lakes | rivers
bank = smoothstep(0, 6, d_water)
h = np.where(ocean, h, np.where(inland_w, bed, (SEA + 0.6) * (1 - bank) + h * bank))
# shorelines: guarantee land is just above sea near any water
near = (~water_any) & (d_water < 3)
h = np.where(near, np.maximum(np.minimum(h, SEA + 1.5 + d_water * 0.6), SEA + 0.4), h)
h = ndi.gaussian_filter(h, 0.8)
h = np.clip(h, 0.5, HEIGHT_SCALE - 1)

# ---------------- splat layers ----------------
LAYERS = ['grass', 'meadow', 'dryland', 'sand', 'snow', 'cliff', 'darkrock', 'dirt', 'path', 'scorched']
L = {k: i for i, k in enumerate(LAYERS)}
w = np.zeros((H, W, len(LAYERS)))
def add(k, m, s=3): w[..., L[k]] += soft(m, s)
add('grass', M('grass')); add('meadow', M('meadow', 'forest')); add('meadow', M('darkmeadow'))
add('dirt', M('darkmeadow') * 0.6); add('dryland', M('dry')); add('dirt', M('barren'))
add('sand', M('sand')); add('scorched', M('scorch'))
add('snow', snow_region, 4); add('darkrock', dark_region, 2)
add('dirt', (conifer | broadleaf) * 0.7, 3)
keep = 1 - np.maximum(soft(snow_region, 3), soft(dark_region, 2))
w *= keep[..., None]
w[..., L['snow']] += soft(snow_region, 3) * 2
w[..., L['darkrock']] += soft(dark_region, 2) * 2
# beaches + water beds
beach = (d_water < 6) & ~snow_region & ~dark_region
w[..., L['sand']] += soft(beach, 2) * 1.5
w[..., L['sand']] += soft(ocean | lakes, 1) * 3
w[..., L['dirt']] += soft(rivers, 1) * 3
# slopes -> cliff rock
gy, gx = np.gradient(h, 2.0)
slope = np.degrees(np.arctan(np.hypot(gx, gy)))
cliff = smoothstep(28, 42, slope)
w *= (1 - cliff)[..., None]
w[..., L['cliff']] += cliff * 2
# roads on top
w *= (1 - rw * 0.9)[..., None]
w[..., L['path']] += rw * 2
w = np.maximum(w, 0) + 1e-4
w /= w.sum(-1, keepdims=True)

# ---------------- resample to Unity grids (flip rows: Unity row 0 = south) ----------------
def resample(arr, n):
    yi = np.linspace(0, arr.shape[0] - 1, n); xi = np.linspace(0, arr.shape[1] - 1, n)
    Y, X = np.meshgrid(yi, xi, indexing='ij')
    return ndi.map_coordinates(arr, [Y, X], order=1)

hm = resample(h, HM)[::-1]
hm16 = np.clip(hm / HEIGHT_SCALE * 65535, 0, 65535).astype('<u2')
hm16.tofile(os.path.join(OUT, 'thorvayne_height.r16'))
am = np.stack([resample(w[..., i], AM) for i in range(len(LAYERS))], -1)[::-1]
am /= am.sum(-1, keepdims=True)
(np.round(am * 255)).astype(np.uint8).tofile(os.path.join(OUT, 'thorvayne_splat.bytes'))
tr = np.stack([resample(soft(m, 1.5), TM) for m in (conifer, broadleaf, snowpine)], -1)[::-1]
block = resample(np.maximum(rw, soft(~land, 1)), TM)[::-1]
tr = np.where(block[..., None] > 0.2, 0, tr)
(np.clip(tr, 0, 1) * 255).astype(np.uint8).tofile(os.path.join(OUT, 'thorvayne_trees.bytes'))

world = {
    'worldWidth': WORLD_W, 'worldLength': WORLD_L, 'heightScale': HEIGHT_SCALE, 'seaLevel': SEA,
    'heightmapResolution': HM, 'alphamapResolution': AM, 'treeMapResolution': TM,
    'layers': LAYERS,
    'markers': [{'name': n_, 'type': t, 'u': P(x, y)[0], 'v': P(x, y)[1], 'dangerTier': d, 'isStarter': s_}
                for n_, t, x, y, d, s_ in markers],
}
json.dump(world, open(os.path.join(OUT, 'thorvayne_world.json'), 'w'), indent=2)

# ---------------- layer textures (tileable, 256px) ----------------
TEX = {'grass': (112, 170, 62), 'meadow': (128, 128, 58), 'dryland': (156, 138, 92), 'sand': (214, 194, 136),
       'snow': (232, 238, 244), 'cliff': (112, 108, 100), 'darkrock': (52, 54, 60), 'dirt': (104, 82, 52),
       'path': (176, 150, 108), 'scorched': (150, 72, 36)}
def tile_noise(n, cell, seed):
    rr = np.random.default_rng(seed); out = np.zeros((n, n)); amp = 1; tot = 0
    for o in range(4):
        c = max(1, cell >> o); k = n // c
        grid = rr.random((k, k))
        z = ndi.zoom(np.tile(grid, (3, 3)), c, order=3)[n:2 * n, n:2 * n]
        out += amp * z; tot += amp; amp *= 0.5
    return out / tot
for i, (k, col) in enumerate(TEX.items()):
    nz = tile_noise(256, 32, 100 + i) - 0.5
    amp = 22 if k not in ('snow', 'sand') else 12
    img = np.clip(np.array(col)[None, None, :] + nz[..., None] * amp * 2, 0, 255).astype(np.uint8)
    Image.fromarray(img).save(os.path.join(OUT, 'Textures', f'tv_{k}.png'))

# ---------------- previews ----------------
cols = np.array([TEX[k] for k in LAYERS], float)
rgb = (w[..., :, None] * cols[None, None]).sum(2)
wc = np.array([60, 120, 160.0])
depth = np.clip((SEA - h) / 20, 0, 1)[..., None]
rgb = np.where(water_any[..., None], wc * (1 - 0.5 * depth) + 40 * (1 - depth), rgb)
gy, gx = np.gradient(h, 2.0)
shade = np.clip(0.75 + (-gx * 0.7 + -gy * -0.7) * 0.06, 0.35, 1.25)  # light from NW
rgb = np.where(water_any[..., None], rgb, rgb * shade[..., None])
tre = (conifer | broadleaf | snowpine)
spk = tre & (rng.random((H, W)) < 0.18)
rgb[spk & conifer] = (30, 70, 30); rgb[spk & broadleaf] = (110, 70, 25); rgb[spk & snowpine] = (40, 70, 60)
prev = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
d = ImageDraw.Draw(prev)
for m in world['markers']:
    x, y = m['u'] * W, (1 - m['v']) * H
    c = (255, 230, 60) if m['isStarter'] else {1: (255, 255, 255), 2: (230, 230, 230), 3: (255, 200, 120), 4: (255, 130, 80), 5: (220, 40, 40)}[m['dangerTier']]
    rad = 7 if m['isStarter'] else 4
    d.ellipse([x - rad, y - rad, x + rad, y + rad], fill=c, outline=(0, 0, 0))
prev.save(os.path.join(OUT, 'preview_terrain.png'))
hp = (np.clip((h - h.min()) / (h.max() - h.min()), 0, 1) * 255).astype(np.uint8)
Image.fromarray(hp).save(os.path.join(OUT, 'preview_heightmap.png'))
print('height range', h.min(), h.max(), 'land %', land.mean(), 'trees cells', tre.sum())
