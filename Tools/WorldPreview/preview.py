import json, sys, math
from PIL import Image, ImageDraw, ImageFont
d = sys.argv[1]; out = sys.argv[2]; seed = sys.argv[3]
img = Image.open(f"{d}/map.ppm").convert("RGB")
S = img.size[0]; w = json.load(open(f"{d}/world.json", encoding="utf-8-sig")); ws = w["worldSize"]
dr = ImageDraw.Draw(img, "RGBA")
P = lambda x, z: (x / ws * S, (1 - z / ws) * S)
tc = {0: (34, 92, 30), 1: (20, 60, 40), 2: (225, 235, 235), 3: (60, 120, 30), 4: (110, 100, 40), 5: (40, 60, 30)}
for x, z, k in w["trees"]:
    px, py = P(x, z); c = tc[k]
    dr.point((px, py), fill=c + (200,))
fb = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"; fr = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
f_city = ImageFont.truetype(fb, 22); f_town = ImageFont.truetype(fb, 16); f_vil = ImageFont.truetype(fr, 13); f_cave = ImageFont.truetype(fr, 12)
def label(xy, text, font, fill):
    x, y = xy
    dr.text((x, y), text, font=font, fill=fill, anchor="mm", stroke_width=3, stroke_fill=(20, 20, 20, 230))
for b in w["bridges"]:
    px, py = P(b["x"], b["z"]); dr.rectangle([px - 3, py - 3, px + 3, py + 3], fill=(140, 90, 40, 255), outline=(30, 20, 10, 255))
for c in w["caves"]:
    px, py = P(c["x"], c["z"]); r = 7
    dr.polygon([(px, py - r), (px + r, py + r * 0.7), (px - r, py + r * 0.7)], fill=(30, 30, 35, 255), outline=(255, 210, 90, 255))
    a = math.radians(c["yaw"]); dr.line([(px, py), (px + math.sin(a) * 14, py - math.cos(a) * 14)], fill=(255, 210, 90, 255), width=2)
    label((px, py + 16), c["name"], f_cave, (255, 225, 150, 255))
for s in w["settlements"]:
    px, py = P(s["x"], s["z"]); t = s["type"]
    r = {"city": 13, "town": 9, "village": 6}[t]
    col = (255, 255, 255, 255) if not s["starter"] else (255, 230, 60, 255)
    dr.ellipse([px - r, py - r, px + r, py + r], fill={"city": (200, 50, 40, 255), "town": (220, 120, 40, 255), "village": (240, 200, 90, 255)}[t], outline=(20, 20, 20, 255), width=2)
    name = s["name"] + (" (start)" if s["starter"] else "")
    label((px, py - r - 12), name, {"city": f_city, "town": f_town, "village": f_vil}[t], col)
# legend + scale
ft = ImageFont.truetype(fb, 20); fl = ImageFont.truetype(fr, 15)
dr.rectangle([14, 14, 330, 200], fill=(15, 20, 25, 200))
dr.text((26, 22), f"Thorvayne random world  seed {seed}", font=ImageFont.truetype(fb, 15), fill="white")
items = [((200, 50, 40), "City"), ((220, 120, 40), "Town"), ((240, 200, 90), "Village"), ((30, 30, 35), "Cave entrance (line = facing)"), ((140, 90, 40), "Bridge")]
for i, (c, t) in enumerate(items):
    y = 52 + i * 26
    if i == 3: dr.polygon([(36, y), (44, y + 14), (28, y + 14)], fill=c + (255,), outline=(255, 210, 90))
    elif i == 4: dr.rectangle([30, y + 3, 40, y + 13], fill=c + (255,), outline=(0, 0, 0))
    else: dr.ellipse([28, y, 44, y + 16], fill=c + (255,), outline=(0, 0, 0), width=2)
    dr.text((54, y), t, font=fl, fill="white")
y = 52 + 5 * 26
km = S / (ws / 1000); dr.line([(28, y + 10), (28 + km, y + 10)], fill="white", width=4); dr.text((36 + km, y), "1 km", font=fl, fill="white")
dr.text((S - 40, 20), "N", font=ft, fill="white", stroke_width=3, stroke_fill="black"); dr.line([(S - 33, 48), (S - 33, 80)], fill="white", width=3)
img.save(out)
print(len(w["settlements"]), "settlements", len(w["caves"]), "caves", len(w["bridges"]), "bridges", len(w["trees"]), "trees")
