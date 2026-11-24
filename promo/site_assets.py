"""Exports the website's icons from the promo renderer (everything is drawn in code).

  python site_assets.py   -> ../site/media/  (dock icons, favicon)
  python site_clips.py    -> ../site/media/  (videos, feature clips and posters)
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

# feature clips, their posters and the videos come from site_clips.py

# favicon from WinDock's own icon
ico = os.path.join(os.path.dirname(OUT), "..", "obj", "windock.ico")
if os.path.exists(ico):
    im = Image.open(ico); im.size = (256, 256) if (256, 256) in im.info.get("sizes", []) else im.size
    im.convert("RGBA").resize((64, 64), Image.LANCZOS).save(os.path.join(OUT, "favicon.png"))
    im.convert("RGBA").resize((180, 180), Image.LANCZOS).save(os.path.join(OUT, "apple-touch-icon.png"))
print("done:", OUT)
