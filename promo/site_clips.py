"""Cuts the website's media from the rendered videos (run after render.py --wide and music.py):

  site/media/windock-wide.mp4   1920x1080 silent loop (the "Watch" window)
  site/media/wide-poster.webp   poster frame for the wide video
  site/media/clip-*.mp4         4:3 silent loops for the feature tiles, with matching *.webp posters

  python site_clips.py
"""
import os, shutil, subprocess, sys
sys.argv.append("--wide")             # use the widescreen layout numbers from render.py
import render as R
import imageio_ffmpeg

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "site", "media")
FF = imageio_ffmpeg.get_ffmpeg_exe()
WIDE = os.path.join(HERE, "WinDock-wide.mp4")          # silent render, sharpest source for the loops


def ff(*args):
    subprocess.run([FF, "-hide_banner", "-loglevel", "error", "-y", *args], check=True,
                   stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL)


def box43(cx, top, bottom, w=R.W):
    """A 4:3 box spanning top..bottom, centred on cx and kept inside the frame."""
    h = bottom - top
    bw = int(h * 4 / 3)
    x = int(min(max(cx - bw / 2, 0), w - bw))
    return x, int(top), bw, int(h)


bx = R.BC[R.NAMES.index("Browser")]
dl = R.BC[R.NAMES.index("Downloads")]
bottom = R.DOCK_BOTTOM + 12
CLIPS = {  # name: (start, end, crop box)
    "genie": (9.4, 11.7, box43(R.W / 2, R.MY - 6, bottom)),
    "previews": (12.7, 15.3, box43(bx, bottom - 480, bottom)),
    "stacks": (15.9, 19.85, box43(dl - 60, 150, bottom)),
    "settings": (25.7, 29.9, box43(R.W / 2, R.SY - 30, bottom)),
}

for name, (t0, t1, (x, y, w, h)) in CLIPS.items():
    vf = f"crop={w}:{h}:{x}:{y},scale=960:720:flags=lanczos"
    ff("-ss", str(t0), "-to", str(t1), "-i", WIDE, "-vf", vf, "-an", "-c:v", "libx264", "-crf", "26",
       "-preset", "slow", "-pix_fmt", "yuv420p", "-movflags", "+faststart", os.path.join(OUT, f"clip-{name}.mp4"))
    ff("-ss", str((t0 + t1) / 2 if name != "genie" else 10.75), "-i", WIDE, "-frames:v", "1", "-vf", vf,
       "-quality", "82", os.path.join(OUT, f"{name}.webp"))
    print(name, (x, y, w, h))

ff("-ss", "5.0", "-i", WIDE, "-frames:v", "1", "-vf", "scale=1280:-2:flags=lanczos", "-quality", "80",
   os.path.join(OUT, "wide-poster.webp"))
# the Watch window plays it as a silent loop: no audio track, moov atom first so it starts while downloading
ff("-i", WIDE, "-an", "-c:v", "copy", "-movflags", "+faststart", os.path.join(OUT, "windock-wide.mp4"))
for f in sorted(os.listdir(OUT)):
    p = os.path.join(OUT, f)
    if os.path.isfile(p):
        print(f"{f:24s} {os.path.getsize(p) / 1024:8.0f} KB")
