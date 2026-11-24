"""Exports artwork for the animated desktop at the top of the website (everything is drawn in code).

  python stage_assets.py   -> ../site/media/stage/  (wallpaper, music window, file icons, window thumbnails)
"""
import os, sys
sys.argv.append("--wide")
from PIL import Image
import render as R

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "site", "media", "stage")
os.makedirs(OUT, exist_ok=True)
A = R.Assets()
A.bg.convert("RGB").resize((1600, 900), Image.LANCZOS).save(os.path.join(OUT, "wallpaper.webp"), quality=86, method=6)
A.music.save(os.path.join(OUT, "music-window.png"), optimize=True)
for i, d in enumerate(A.docs):
    d.resize((128, 128), Image.LANCZOS).save(os.path.join(OUT, f"doc{i}.png"), optimize=True)
for i, t in enumerate(A.thumbs):
    t.save(os.path.join(OUT, f"thumb{i}.png"), optimize=True)
for f in sorted(os.listdir(OUT)):
    print(f"{f:20s} {os.path.getsize(os.path.join(OUT, f)) / 1024:6.0f} KB")
