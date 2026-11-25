# Two Nexus gallery pictures made from One Style's own art: 24 of the 179 Unique monsters, and all 58 Unique items.
# python collage.py (needs Pillow); writes Dev\shots\nexus-onestyle\20-unique-monsters.png and 21-unique-items.png
import json, os
from PIL import Image, ImageDraw, ImageFont, ImageFilter
SRC = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))   # the repository
M = os.path.join(SRC, 'Mods', 'OneStyle')
OUT = os.path.join(SRC, 'Dev', 'shots', 'nexus-onestyle')
os.makedirs(OUT, exist_ok=True)
F = r'C:\Windows\Fonts'
def font(name, size): return ImageFont.truetype(os.path.join(F, name), size)
BG, PANEL, EDGE = (22, 22, 28), (34, 34, 42), (234, 133, 56)
ORANGE, TEXT, DIM = (234, 133, 56), (232, 226, 214), (160, 154, 142)
W, H = 1600, 900

def backdrop():
    im = Image.new('RGB', (W, H), BG)
    d = ImageDraw.Draw(im)
    for y in range(H):   # a faint warm glow from the top
        a = max(0, 40 - y // 12)
        d.line([(0, y), (W, y)], fill=(22 + a // 3, 22 + a // 6, 28))
    return im

def centered(d, y, text, f, fill):
    w = d.textlength(text, font=f); d.text(((W - w) / 2, y), text, font=f, fill=fill)

def fit(d, text, f, width):
    while d.textlength(text, font=f) > width and len(text) > 3: text = text[:-2]
    return text

def shrink(d, text, name, size, width):
    while size > 10 and d.textlength(text, font=font(name, size)) > width: size -= 1
    return font(name, size)

uniques = json.load(open(os.path.join(M, 'data', 'uniques.json'), encoding='utf-8'))
elites = {e['Id']: e for e in json.load(open(os.path.join(M, 'data', 'elites.json'), encoding='utf-8'))}

# 1. Unique monsters: the ones whose Unique has its own item, spread over the levels
src = [u['Source'] for u in uniques]
picks = [s for i, s in enumerate(src) if i % 58 * 24 // 58 == i * 24 // 58][:24]
seen = []; [seen.append(s) for s in src if s not in seen]
step = len(seen) / 24
picks = [seen[int(i * step)] for i in range(24)]
im = backdrop(); d = ImageDraw.Draw(im)
centered(d, 26, '179 UNIQUE MONSTERS', font('georgiab.ttf', 46), ORANGE)
centered(d, 86, 'Each has a rare Unique with its own name, story and picture, two affixes and, for most, a signature attack', font('segoeui.ttf', 22), DIM)
cols, rows, cw, ch = 8, 3, 190, 248
x0, y0 = (W - cols * cw) // 2, 138
for i, sid in enumerate(picks):
    e = elites[sid]
    cx, cy = x0 + (i % cols) * cw, y0 + (i // cols) * ch
    d.rounded_rectangle([cx + 6, cy + 4, cx + cw - 6, cy + ch - 10], 10, fill=PANEL, outline=EDGE, width=2)
    art = Image.open(os.path.join(M, 'art', 'unique', e['File'] + '.webp')).convert('RGBA').resize((168, 168), Image.LANCZOS)
    im.paste(art, (cx + (cw - 168) // 2, cy + 10), art)
    name, title = (e['UniqueName'].split(', ', 1) + [''])[:2]
    f1, f2 = font('georgiab.ttf', 19), font('segoeui.ttf', 14)
    n = fit(d, name, f1, cw - 20); d.text((cx + (cw - d.textlength(n, font=f1)) / 2, cy + 184), n, font=f1, fill=ORANGE)
    t = fit(d, title, f2, cw - 20); d.text((cx + (cw - d.textlength(t, font=f2)) / 2, cy + 210), t, font=f2, fill=TEXT)
im.save(os.path.join(OUT, '20-unique-monsters.png')); print('20 ok', len(picks))

# 2. All 58 Unique items, by level
items = sorted(uniques, key=lambda u: (u['Level'], u['Name']))
im = backdrop(); d = ImageDraw.Draw(im)
centered(d, 22, '58 UNIQUE ITEMS', font('georgiab.ttf', 46), ORANGE)
centered(d, 82, "Each drops from one monster's Unique, with fixed stats and a special power of its own", font('segoeui.ttf', 22), DIM)
cols, cw, ch = 10, 156, 124
x0, y0 = (W - cols * cw) // 2, 128
for i, u in enumerate(items):
    cx, cy = x0 + (i % cols) * cw, y0 + (i // cols) * ch
    d.rounded_rectangle([cx + 40, cy, cx + cw - 40, cy + 78], 8, fill=PANEL, outline=EDGE, width=2)
    art = Image.open(os.path.join(M, 'art', 'items', u['Id'][3:] + '.webp')).convert('RGBA').resize((68, 68), Image.LANCZOS)
    im.paste(art, (cx + (cw - 68) // 2, cy + 5), art)
    n = u['Name']; f1 = shrink(d, n, 'segoeuib.ttf', 14, cw - 6); d.text((cx + (cw - d.textlength(n, font=f1)) / 2, cy + 84), n, font=f1, fill=TEXT)
    f2 = font('segoeui.ttf', 12); s = f"{u['Slot'].lower().replace('torso', 'body')} · level {u['Level']}"
    d.text((cx + (cw - d.textlength(s, font=f2)) / 2, cy + 102), s, font=f2, fill=DIM)
im.save(os.path.join(OUT, '21-unique-items.png')); print('21 ok', len(items))
