"""Exports the website's images from the promo renderer (everything is drawn in code).

  python site_assets.py   -> ../site/media/  (dock icons, feature stills, favicon, video poster)
"""
import os
from PIL import Image
import render as R

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "site", "media")
os.makedirs(os.path.join(OUT, "icons"), exist_ok=True)
A = R.Assets()

# dock icons, 192 px (shown at up to ~110 px CSS, sharp on high-DPI screens)
for name, img in A.icons.items():
    img.resize((192, 192), Image.LANCZOS).save(os.path.join(OUT, "icons", name.lower() + ".png"), optimize=True)

# feature stills: (time in the reel, crop box) - crops stay below the captions
STILLS = {
    "genie": (10.7, (0, 640, 1080, 1700)),
    "previews": (14.5, (0, 1120, 1080, 1700)),
    "stacks": (18.2, (300, 1020, 1080, 1700)),
    "settings": (29.0, (40, 440, 1040, 1700)),
}
for name, (t, box) in STILLS.items():
    R.render(t, A).crop(box).save(os.path.join(OUT, name + ".webp"), quality=84, method=6)

R.render(5.0, A).save(os.path.join(OUT, "reel-poster.webp"), quality=80, method=6)

# favicon from WinDock's own icon
ico = os.path.join(os.path.dirname(OUT), "..", "obj", "windock.ico")
if os.path.exists(ico):
    im = Image.open(ico); im.size = (256, 256) if (256, 256) in im.info.get("sizes", []) else im.size
    im.convert("RGBA").resize((64, 64), Image.LANCZOS).save(os.path.join(OUT, "favicon.png"))
    im.convert("RGBA").resize((180, 180), Image.LANCZOS).save(os.path.join(OUT, "apple-touch-icon.png"))
print("done:", OUT)
