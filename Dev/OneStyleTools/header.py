# The header banner of One Style's Nexus page (1300x372): a tagline on the left, a row of Unique monster portraits on the right.
# python header.py (needs Pillow); writes Dev\shots\nexus-onestyle\00-header.png
import json, os, sys
from PIL import Image, ImageDraw, ImageFont
SRC = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))   # the repository
M = os.path.join(SRC, 'Mods', 'OneStyle')
OUT = os.path.join(SRC, 'Dev', 'shots', 'nexus-onestyle', '00-header.png')
F = r'C:\Windows\Fonts'
def font(name, size): return ImageFont.truetype(os.path.join(F, name), size)
BG, ORANGE, TEXT, DIM, BLUE, YELLOW = (22, 22, 28), (234, 133, 56), (232, 226, 214), (160, 154, 142), (110, 150, 255), (240, 205, 80)
W, H = 1300, 372
im = Image.new('RGB', (W, H), BG)
d = ImageDraw.Draw(im)
for x in range(W):   # warm glow from the right
    a = max(0, (x - 520) // 13)
    d.line([(x, 0), (x, H)], fill=(min(255, 22 + a // 2), min(255, 22 + a // 5), 28))
elites = {e['Id']: e for e in json.load(open(os.path.join(M, 'data', 'elites.json'), encoding='utf-8'))}
picks = ['Zelith', 'Yzren', 'Teyr', 'Skyrra', 'Auvrel', 'Velkith', 'Keldrun']
chosen = []
for want in picks:
    for e in elites.values():
        if e['UniqueName'].split(',')[0].strip() == want:
            chosen.append(e); break
# portraits: overlapping row, the middle one biggest
xs = [585, 685, 785, 890, 995, 1095, 1185]
sizes = [180, 200, 225, 255, 225, 200, 180]
order = sorted(range(len(chosen)), key=lambda i: sizes[i])
for i in order:
    e = chosen[i]; s = sizes[i]
    art = Image.open(os.path.join(M, 'art', 'unique', e['File'] + '.webp')).convert('RGBA').resize((s, s), Image.LANCZOS)
    im.paste(art, (xs[i] - s // 2 + 40, H - s - 62), art)
d = ImageDraw.Draw(im)
# Nexus draws the breadcrumbs, the mod name and the counters over the lower left of the header, and its buttons over
# the lower right: all text stays in the top half.
d.text((46, 34), 'Pick one combat style', font=font('segoeuib.ttf', 36), fill=TEXT)
d.text((46, 82), 'and play the whole game with it.', font=font('segoeuib.ttf', 36), fill=ORANGE)
f = font('segoeuib.ttf', 24)
x = 48
for word, col in (('Magic', BLUE), (', ', DIM), ('Rare', YELLOW), (' and ', DIM), ('Unique', ORANGE), (' gear', DIM)):
    d.text((x, 142), word, font=f, fill=col); x += d.textlength(word, font=f)
d.text((48, 180), '179 elite monsters and 58 Unique items', font=font('segoeui.ttf', 20), fill=DIM)
im.save(OUT); print('ok', OUT, [e['UniqueName'] for e in chosen])
