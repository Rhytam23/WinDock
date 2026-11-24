"""Renders WinDock's promo video entirely from code, in two formats with the same timeline:
a 1080x1920 (9:16) Instagram Reel, and with --wide a 1920x1080 (16:9) Windows desktop version.

Usage:
  python render.py [--wide] --stills 5 10.7 14 17 18 19.2 21 23.5 27.5 29 32   # PNG frames to promo/stills
  python render.py [--wide]                                                     # full MP4 (needs imageio-ffmpeg)

Scenes (seconds; 128 BPM, 1 bar = 1.875 s, 18 bars = 33.75 s):
  0.0  title, dock slides up          3.8  magnification
  7.1  click -> bounce, window opens  9.5  click again -> genie minimise
 12.5  window previews                15.5 folder stacks: fan, grid, list
 20.0  drag an icon off -> poof       22.7 auto-hide
 25.7  Settings: size + appearance    30.0 end card
All artwork (wallpaper, icons, windows, cursor) is drawn here; nothing is captured from the screen.
"""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

WIDE = "--wide" in sys.argv          # 1920x1080 desktop version (the default is the 1080x1920 Reel)
W, H, FPS = (1920, 1080, 30) if WIDE else (1080, 1920, 30)
DUR = 18 * 1.875
HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = r"C:\Windows\Fonts"


def font(name, size):
    for n in (name, "segoeui.ttf", "arial.ttf"):
        p = os.path.join(FONTS, n)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def vfont(style, size, fallback="seguisb.ttf"):
    """Segoe UI Variable (Windows 11's own typeface) at a named weight, e.g. "Bold Display"."""
    try:
        f = ImageFont.truetype(os.path.join(FONTS, "SegUIVar.ttf"), size)
        f.set_variation_by_name(style)
        return f
    except Exception:
        return font(fallback, size)


F_TITLE = vfont("Bold Display", 150, "segoeuib.ttf")
F_SUB = vfont("Semibold Display", 50)
F_CAP = vfont("Bold Display", 60, "segoeuib.ttf")
F_SEG = vfont("Semibold Text", 34)
F_LABEL = vfont("Semibold Text", 30)
F_FAN = vfont("Semibold Text", 30)
F_SMALL = vfont("Semibold Text", 24)
F_CHIP = vfont("Semibold Text", 40)
F_SET = vfont("Regular", 30, "segoeui.ttf")
F_SETB = vfont("Bold Display", 44, "segoeuib.ttf")
F_WIN = vfont("Semibold Text", 26)
F_TB = vfont("Regular", 22, "segoeui.ttf")

# where things sit in each format
if WIDE:
    DOCK_BOTTOM, CAP_Y, SEG_Y, TITLE_Y, SUB_Y, FAN_STEP = 1040, 104, 168, 450, 575, 100
    END_Y = (370, 495, 610, 92)
else:
    DOCK_BOTTOM, CAP_Y, SEG_Y, TITLE_Y, SUB_Y, FAN_STEP = 1640, 330, 400, 760, 890, 118
    END_Y = (640, 770, 900, 110)

# ---------------------------------------------------------------- easing / helpers

def clamp(x, a=0.0, b=1.0):
    return a if x < a else b if x > b else x

def prog(t, t0, t1):
    return clamp((t - t0) / (t1 - t0))

def ease_out(x):
    return 1 - (1 - x) ** 3

def ease_in_out(x):
    return 4 * x ** 3 if x < 0.5 else 1 - (-2 * x + 2) ** 3 / 2

def ease_out_back(x, s=1.4):
    x -= 1
    return 1 + (s + 1) * x ** 3 + s * x ** 2

def lerp(a, b, x):
    return a + (b - a) * x

def lerpc(c1, c2, x):
    return tuple(int(round(lerp(a, b, x))) for a, b in zip(c1, c2))

def window(t, t0, t1, fade=0.35):
    return min(ease_out(prog(t, t0, t0 + fade)), 1 - ease_in_out(prog(t, t1 - fade, t1)))


def paste(frame, layer, x, y, alpha=1.0):
    x, y = int(round(x)), int(round(y))
    if alpha <= 0.002:
        return
    if alpha < 0.999:
        layer = layer.copy()
        layer.putalpha(layer.getchannel("A").point(lambda v: int(v * alpha)))
    sx, sy = max(0, -x), max(0, -y)
    ex, ey = min(layer.width, W - x), min(layer.height, H - y)
    if ex <= sx or ey <= sy:
        return
    if sx or sy or ex < layer.width or ey < layer.height:
        layer = layer.crop((sx, sy, ex, ey))
    frame.alpha_composite(layer, dest=(x + sx, y + sy))


def rounded_mask(w, h, r, ss=3):
    m = Image.new("L", (w * ss, h * ss), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, w * ss - 1, h * ss - 1), r * ss, fill=255)
    return m.resize((w, h), Image.LANCZOS)


def text_layer(text, fnt, fill=(255, 255, 255), shadow=True, pad=30):
    l, t, r, b = fnt.getbbox(text)
    w, h = r - l + pad * 2, b - t + pad * 2
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    if shadow:
        sh = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        ImageDraw.Draw(sh).text((pad - l, pad - t + 4), text, font=fnt, fill=(20, 10, 40, 150))
        img = Image.alpha_composite(img, sh.filter(ImageFilter.GaussianBlur(10)))
    ImageDraw.Draw(img).text((pad - l, pad - t), text, font=fnt, fill=fill)
    return img

_TC = {}
def center_text(frame, text, fnt, cy, alpha=1.0, dy=0.0, cx=W / 2):
    key = (text, id(fnt))
    if key not in _TC:
        _TC[key] = text_layer(text, fnt)
    lay = _TC[key]
    paste(frame, lay, cx - lay.width / 2, cy - lay.height / 2 + dy, alpha)


def pill(text, fnt, bg, fg=(255, 255, 255), padx=16, pady=6, radius=None):
    lab = text_layer(text, fnt, fill=fg, shadow=False, pad=padx)
    p = Image.new("RGBA", lab.size, (0, 0, 0, 0))
    ImageDraw.Draw(p).rounded_rectangle((0, pady, lab.width - 1, lab.height - pady), radius or 18, fill=bg)
    p.alpha_composite(lab)
    return p


def soft_shadow(w, h, r, blur=22, alpha=120, off=14):
    sh = Image.new("RGBA", (w + blur * 4, h + blur * 4), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((blur * 2, blur * 2 + off, blur * 2 + w, blur * 2 + h + off), r, fill=(10, 0, 30, alpha))
    return sh.filter(ImageFilter.GaussianBlur(blur)), blur * 2

# ---------------------------------------------------------------- wallpaper

def make_wallpaper(seed=7):
    """An original Windows 11-style bloom: glossy, curved petals over deep blue."""
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    m = float(min(W, H))
    cx, cy = W * 0.5, H * 0.56

    # background: deep navy at the corners, brighter blue behind the flower
    r = np.sqrt(((xx - cx) / (W * 0.75)) ** 2 + ((yy - cy) / (H * 0.95)) ** 2)
    g = np.clip(r, 0, 1)[..., None] ** 0.9
    img = np.array([22, 84, 214], np.float32) * (1 - g) + np.array([3, 12, 52], np.float32) * g
    glow = np.exp(-((xx - cx) ** 2 + (yy - cy) ** 2) / (2 * (m * 0.30) ** 2))[..., None]
    img = img + np.array([40, 110, 230], np.float32) * glow * 0.55

    dark = np.array([8, 40, 140], np.float32)
    mid = np.array([30, 110, 240], np.float32)
    lite = np.array([150, 210, 255], np.float32)
    tip = np.array([225, 242, 255], np.float32)

    L = m * 0.56            # petal length
    Wd = m * 0.15           # petal half-width at its widest
    n = 7
    for k in range(n):
        a = np.deg2rad(-90 + k * 360 / n + 8)
        dx, dy = xx - cx, yy - cy
        u = dx * np.cos(a) + dy * np.sin(a)
        v = -dx * np.sin(a) + dy * np.cos(a)
        t = u / L
        tc = np.clip(t, 0, 1)
        bend = Wd * 0.9 * np.sin(np.pi * tc * 0.9) * tc          # petals sweep sideways: a gentle swirl
        hw = Wd * np.sin(np.pi * tc) ** 0.75 * (0.55 + 0.45 * np.sin(np.pi * tc * 0.8))
        hw = np.maximum(hw, 1e-3)
        s = (v - bend) / hw                                        # -1 .. 1 across the petal
        inside = (t > 0) & (t < 1)
        edge = hw - np.abs(v - bend)
        a_mask = np.clip(edge / 1.6, 0, 1) * inside
        a_mask *= np.clip(t * 6, 0, 1)                             # fade into the centre

        # shading: one edge catches light, the other folds into shadow
        f = np.clip((s + 1) / 2, 0, 1)
        col = (dark[None, None] * (1 - f[..., None]) ** 1.6
               + mid[None, None] * (1 - np.abs(f[..., None] - 0.55) * 1.6).clip(0, 1)
               + lite[None, None] * f[..., None] ** 2.2)
        rim = np.exp(-((1 - np.clip(s, -1, 1)) * hw / (m * 0.006)) ** 2)            # thin bright rim on the lit edge
        col = col + (tip - col) * (rim[..., None] * 0.85)
        col = col + (tip - col) * (np.clip(tc - 0.55, 0, 1)[..., None] * 0.5 * f[..., None])
        alpha = a_mask * (0.72 + 0.14 * f)

        # soft shadow cast onto what's beneath, then the petal itself
        sh = Image.fromarray((a_mask * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(m * 0.02))
        sh = np.array(sh, np.float32)[..., None] / 255
        img = img * (1 - sh * 0.35)
        img = img * (1 - alpha[..., None]) + col * alpha[..., None]

    core = np.exp(-((xx - cx) ** 2 + (yy - cy) ** 2) / (2 * (m * 0.05) ** 2))[..., None]
    img = img + (np.array([235, 246, 255], np.float32) - img) * core * 0.6
    vig = np.clip(1 - 0.35 * (((xx - W / 2) / W) ** 2 + ((yy - H / 2) / H) ** 2) * 2, 0, 1)[..., None]
    img = img * vig
    img += np.random.default_rng(seed).normal(0, 1.4, (H, W, 1))
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


def draw_taskbar(fr, A, t):
    """A Windows 11-style taskbar (centred icons, search box, clock) that slides away as WinDock arrives."""
    off = ease_in_out(prog(t, 1.0, 1.45))
    if off >= 1:
        return
    th = 56 if WIDE else 66
    y0 = int(H - th + off * (th + 12))
    if y0 >= H - 1:
        return
    g = A.bg_blur.crop((0, y0, W, min(H, y0 + th)))
    g = Image.blend(g, Image.new("RGBA", g.size, (232, 238, 250, 255)), 0.78)
    fr.alpha_composite(g, (0, y0))
    d = ImageDraw.Draw(fr)
    d.line((0, y0, W, y0), fill=(255, 255, 255, 200), width=1)
    s = int(th * 0.62)
    names = ["Files", "Browser", "Mail", "Music", "Settings"]
    search_w = int(th * 3.4)
    total = search_w + 14 + len(names) * (s + 14)
    x = W / 2 - total / 2
    cy = y0 + th / 2
    d.rounded_rectangle((x, cy - s / 2, x + search_w, cy + s / 2), s / 2, fill=(255, 255, 255, 235), outline=(200, 205, 220, 255))
    r = s * 0.18
    d.ellipse((x + s * 0.35 - r, cy - r - 2, x + s * 0.35 + r, cy + r - 2), outline=(60, 60, 70, 255), width=2)
    d.line((x + s * 0.35 + r * 0.7, cy + r * 0.7 - 2, x + s * 0.35 + r * 1.5, cy + r * 1.5 - 2), fill=(60, 60, 70, 255), width=2)
    d.text((x + s * 0.95, cy), "Search", font=F_TB, fill=(90, 90, 100, 255), anchor="lm")
    x += search_w + 14
    for n in names:
        fr.alpha_composite(A.icon(n, s), (int(x), int(cy - s / 2)))
        if n in ("Files", "Browser"):
            d.rounded_rectangle((x + s / 2 - 6, cy + s / 2 + 3, x + s / 2 + 6, cy + s / 2 + 6), 2, fill=(70, 110, 230, 255))
        x += s + 14
    d.text((W - 24, cy - 11), "4:20 PM", font=F_TB, fill=(30, 30, 40, 255), anchor="rm")
    d.text((W - 24, cy + 13), "11/24/2026", font=F_TB, fill=(30, 30, 40, 255), anchor="rm")

# ---------------------------------------------------------------- icons (all original, drawn here)

S = 1024


def vgrad(w, h, c1, c2):
    ar = np.linspace(0, 1, h)[:, None, None]
    col = (np.array(c1) * (1 - ar) + np.array(c2) * ar).astype(np.uint8)
    return Image.fromarray(np.dstack([np.repeat(col, w, 1), np.full((h, w, 1), 255, np.uint8)]), "RGBA")


def squircle(c1, c2):
    base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    m = Image.new("L", (S, S), 0)
    ImageDraw.Draw(m).rounded_rectangle((40, 40, S - 40, S - 40), 230, fill=255)
    base.paste(vgrad(S, S, c1, c2), (0, 0), m)
    hl = Image.new("L", (S, S), 0)
    ImageDraw.Draw(hl).rounded_rectangle((40, 40, S - 40, S // 2), 230, fill=38)
    base.paste(Image.new("RGBA", (S, S), (255, 255, 255, 255)), (0, 0), Image.composite(hl, Image.new("L", (S, S), 0), m))
    return base


def finish(img):
    return img.resize((512, 512), Image.LANCZOS)


def ic_files():
    im = squircle((92, 178, 255), (24, 104, 222)); d = ImageDraw.Draw(im)
    d.rounded_rectangle((210, 300, 520, 420), 40, fill=(214, 236, 255))
    d.rounded_rectangle((210, 360, 814, 740), 50, fill=(255, 255, 255))
    d.rectangle((210, 430, 814, 446), fill=(200, 226, 255))
    return finish(im)


def ic_browser():
    im = squircle((70, 214, 222), (30, 120, 230)); d = ImageDraw.Draw(im)
    c, r, w = 512, 290, 34
    d.ellipse((c - r, c - r, c + r, c + r), outline="white", width=w)
    d.ellipse((c - 130, c - r, c + 130, c + r), outline="white", width=w)
    d.line((c, c - r, c, c + r), fill="white", width=w)
    d.line((c - r, c, c + r, c), fill="white", width=w)
    d.arc((c - r, c - r - 210, c + r, c - 40), 35, 145, fill="white", width=w - 6)
    d.arc((c - r, c + 40, c + r, c + r + 210), 215, 325, fill="white", width=w - 6)
    return finish(im)


def ic_mail():
    im = squircle((100, 200, 255), (10, 110, 245)); d = ImageDraw.Draw(im)
    d.rounded_rectangle((210, 310, 814, 714), 50, fill="white")
    d.line((230, 340, 512, 540, 794, 340), fill=(60, 150, 250), width=40, joint="curve")
    return finish(im)


def ic_music():
    im = squircle((255, 110, 120), (225, 30, 90)); d = ImageDraw.Draw(im)
    d.polygon([(420, 300), (760, 240), (760, 320), (420, 380)], fill="white")
    d.rectangle((420, 330, 460, 660), fill="white")
    d.rectangle((720, 270, 760, 610), fill="white")
    d.ellipse((290, 600, 460, 730), fill="white")
    d.ellipse((590, 550, 760, 680), fill="white")
    return finish(im)


def ic_photos():
    im = squircle((255, 255, 255), (238, 238, 243))
    for k, col in enumerate([(255, 179, 0), (255, 99, 71), (220, 60, 160), (120, 90, 255), (40, 170, 255), (60, 200, 120)]):
        pet = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        ImageDraw.Draw(pet).ellipse((412, 190, 612, 520), fill=col + (205,))
        im = Image.alpha_composite(im, pet.rotate(k * 60, center=(512, 512), resample=Image.BICUBIC))
    return finish(im)


def ic_notes():
    im = squircle((255, 252, 240), (246, 240, 222))
    m = Image.new("L", (S, S), 0); ImageDraw.Draw(m).rounded_rectangle((40, 40, S - 40, S - 40), 230, fill=255)
    band = Image.new("RGBA", (S, S), (0, 0, 0, 0)); ImageDraw.Draw(band).rectangle((0, 0, S, 300), fill=(255, 206, 60, 255))
    im.paste(band, (0, 0), Image.composite(band.getchannel("A"), Image.new("L", (S, S), 0), m))
    d = ImageDraw.Draw(im)
    for k in range(4):
        d.line((200, 420 + k * 110, 824, 420 + k * 110), fill=(200, 196, 186), width=14)
    return finish(im)


def ic_terminal():
    im = squircle((70, 72, 78), (24, 25, 28)); d = ImageDraw.Draw(im)
    d.line((250, 360, 410, 480, 250, 600), fill=(80, 230, 120), width=56, joint="curve")
    d.rounded_rectangle((470, 600, 720, 650), 20, fill=(235, 235, 235))
    return finish(im)


def ic_calendar():
    im = squircle((255, 255, 255), (242, 242, 246)); d = ImageDraw.Draw(im)
    d.text((512, 230), "NOV", font=font("segoeuib.ttf", 120), fill=(240, 60, 60), anchor="mm")
    d.text((512, 600), "24", font=font("segoeui.ttf", 400), fill=(30, 30, 34), anchor="mm")
    return finish(im)


def ic_settings():
    im = squircle((170, 174, 182), (100, 104, 112))
    gear = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    for k in range(10):
        t = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        ImageDraw.Draw(t).rounded_rectangle((462, 170, 562, 300), 24, fill=(245, 246, 248, 255))
        gear = Image.alpha_composite(gear, t.rotate(k * 36, center=(512, 512), resample=Image.BICUBIC))
    d = ImageDraw.Draw(gear)
    d.ellipse((250, 250, 774, 774), fill=(245, 246, 248, 255))
    d.ellipse((400, 400, 624, 624), fill=(120, 124, 132, 255))
    return finish(Image.alpha_composite(im, gear))


def ic_downloads():
    im = squircle((150, 210, 255), (70, 150, 240)); d = ImageDraw.Draw(im)
    d.rounded_rectangle((210, 300, 520, 420), 40, fill=(225, 242, 255))
    d.rounded_rectangle((210, 360, 814, 740), 50, fill=(255, 255, 255))
    d.rectangle((482, 450, 542, 600), fill=(70, 150, 240))
    d.polygon([(412, 580), (612, 580), (512, 680)], fill=(70, 150, 240))
    return finish(im)


def ic_trash():
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle((300, 200, 724, 250), 20, fill=(235, 238, 245, 235))
    d.polygon([(310, 270), (714, 270), (670, 860), (354, 860)], fill=(225, 230, 240, 210))
    for x in (410, 512, 614):
        d.line((x, 320, x + (x - 512) * 0.12, 800), fill=(160, 168, 184, 230), width=22)
    return finish(im)


def doc_icon(band, label, photo=False):
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.polygon([(230, 90), (640, 90), (800, 250), (800, 934), (230, 934)], fill=(252, 252, 255, 255))
    d.polygon([(640, 90), (640, 250), (800, 250)], fill=(215, 220, 232, 255))
    if photo:
        im.paste(vgrad(500, 380, (255, 150, 80), (120, 60, 200)), (265, 330))
        d.ellipse((560, 380, 660, 480), fill=(255, 230, 150, 255))
        d.polygon([(265, 710), (420, 520), (560, 710)], fill=(60, 40, 110, 255))
    else:
        for k in range(5):
            d.line((300, 330 + k * 80, 730, 330 + k * 80), fill=(205, 210, 222, 255), width=20)
    d.rounded_rectangle((260, 760, 770, 900), 26, fill=band + (255,))
    d.text((515, 830), label, font=font("segoeuib.ttf", 110), fill="white", anchor="mm")
    return im.resize((512, 512), Image.LANCZOS)


def cursor_img():
    s = 4; im = Image.new("RGBA", (40 * s, 60 * s), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.polygon([(x * s, y * s) for x, y in [(2, 2), (2, 44), (12, 34), (19, 50), (26, 47), (19, 32), (33, 32)]], fill=(15, 15, 20, 255))
    d.polygon([(x * s, y * s) for x, y in [(5, 9), (5, 37), (12.5, 29.5), (19.5, 45), (22.5, 43.5), (15.5, 28.5), (26, 28.5)]], fill=(255, 255, 255, 255))
    im = im.resize((40, 60), Image.LANCZOS)
    sh = Image.new("RGBA", (60, 80), (0, 0, 0, 0)); sh.paste((0, 0, 0, 90), (6, 8), im.getchannel("A"))
    out = sh.filter(ImageFilter.GaussianBlur(3)); out.alpha_composite(im, (4, 4))
    return out

# ---------------------------------------------------------------- windows (original, generic look)

MW, MH = 760, 500
MX, MY = (W - MW) // 2, (230 if WIDE else 720)


def win_controls(d, x_right, y, col):
    d.line((x_right - 150, y, x_right - 130, y), fill=col, width=3)
    d.rectangle((x_right - 92, y - 10, x_right - 72, y + 10), outline=col, width=3)
    d.line((x_right - 34, y - 10, x_right - 14, y + 10), fill=col, width=3)
    d.line((x_right - 34, y + 10, x_right - 14, y - 10), fill=col, width=3)


def make_music_window():
    im = Image.new("RGBA", (MW, MH), (0, 0, 0, 0))
    body = vgrad(MW, MH, (40, 26, 70), (18, 14, 34))
    d = ImageDraw.Draw(body)
    d.rectangle((0, 0, MW, 56), fill=(52, 38, 86))
    d.text((24, 28), "Music", font=F_WIN, fill=(235, 230, 250), anchor="lm")
    win_controls(d, MW - 10, 28, (220, 215, 235))
    art = vgrad(260, 260, (255, 120, 120), (120, 60, 220))
    am = rounded_mask(260, 260, 22)
    body.paste(art, (36, 96), am)
    ImageDraw.Draw(body).ellipse((36 + 90, 96 + 90, 36 + 170, 96 + 170), outline=(255, 255, 255), width=8)
    d = ImageDraw.Draw(body)
    d.text((330, 112), "NOW PLAYING", font=F_SMALL, fill=(170, 160, 200))
    d.text((330, 148), "Midnight Drive", font=font("segoeuib.ttf", 46), fill="white")
    d.text((330, 212), "Neon Avenue", font=F_SET, fill=(190, 182, 220))
    d.rounded_rectangle((330, 290, 720, 298), 4, fill=(80, 70, 110))
    d.rounded_rectangle((330, 290, 520, 298), 4, fill=(255, 110, 140))
    d.ellipse((510, 284, 530, 304), fill="white")
    for k, x in enumerate((400, 500, 600)):
        r = 30 if k == 1 else 22
        d.ellipse((x - r, 330, x + r, 330 + 2 * r), fill=(255, 255, 255) if k == 1 else (90, 80, 120))
    d.polygon([(492, 344), (492, 376), (516, 360)], fill=(40, 26, 70))
    for k, name in enumerate(["1  Midnight Drive", "2  City Lights", "3  Afterglow"]):
        d.text((40, 384 + k * 36), name, font=F_SMALL, fill=(200, 194, 225) if k else (255, 140, 165))
    im.paste(body, (0, 0), rounded_mask(MW, MH, 18))
    return im


def make_thumb(accent, title):
    w, h = 250, 160
    im = Image.new("RGBA", (w, h), (250, 250, 252, 255)); d = ImageDraw.Draw(im)
    d.rectangle((0, 0, w, 22), fill=(232, 233, 238))
    d.rounded_rectangle((40, 6, 200, 16), 5, fill=(255, 255, 255))
    d.rectangle((0, 22, w, 62), fill=accent)
    d.rounded_rectangle((14, 34, 120, 50), 6, fill=(255, 255, 255))
    for k in range(3):
        d.rounded_rectangle((14 + k * 78, 74, 84 + k * 78, 120), 8, fill=(225, 228, 236))
    d.rounded_rectangle((14, 130, 236, 138), 4, fill=(225, 228, 236))
    d.rounded_rectangle((14, 144, 170, 152), 4, fill=(225, 228, 236))
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0)); out.paste(im, (0, 0), rounded_mask(w, h, 12))
    return out

# ---------------------------------------------------------------- Settings window

SW, SH = 900, 700
SX, SY = (W - SW) // 2, (200 if WIDE else 470)
TRACK = (560, 850, 205)          # x0, x1, y  (relative)
SEG = (300, 850, 420, 474)        # segmented control box
BMIN, BMAX = 48, 90
B0, B1 = 66, 76


def knob_x(v):
    return SX + TRACK[0] + v * (TRACK[1] - TRACK[0])

V0, V1 = (B0 - BMIN) / (BMAX - BMIN), (B1 - BMIN) / (BMAX - BMIN)


def draw_settings(dark, v, seg_sel):
    bg = lerpc((246, 246, 248), (36, 36, 42), dark)
    side = lerpc((234, 234, 238), (28, 28, 33), dark)
    fg = lerpc((28, 28, 32), (240, 240, 245), dark)
    sub = lerpc((110, 110, 120), (160, 160, 170), dark)
    card = lerpc((255, 255, 255), (48, 48, 56), dark)
    line = lerpc((225, 225, 230), (64, 64, 72), dark)
    acc = (10, 122, 255)
    im = Image.new("RGBA", (SW, SH), bg + (255,)); d = ImageDraw.Draw(im)
    d.rectangle((0, 0, 250, SH), fill=side)
    d.ellipse((22, 22, 40, 40), fill=(255, 95, 87))
    d.rounded_rectangle((18, 60, 232, 100), 10, fill=lerpc((222, 222, 228), (44, 44, 52), dark))
    d.text((44, 80), "Search", font=F_SMALL, fill=sub, anchor="lm")
    items = [("General", (142, 142, 150)), ("Dock", (10, 122, 255)), ("Appearance", (88, 86, 214)),
             ("Desktop", (52, 199, 89)), ("About", (255, 149, 0))]
    for k, (name, col) in enumerate(items):
        y = 130 + k * 56
        if name == "Dock":
            d.rounded_rectangle((12, y, 238, y + 46), 10, fill=acc)
        d.rounded_rectangle((24, y + 9, 52, y + 37), 7, fill=(255, 255, 255) if name == "Dock" else col)
        d.text((66, y + 23), name, font=F_SMALL, fill=(255, 255, 255) if name == "Dock" else fg, anchor="lm")
    d.text((290, 70), "Dock", font=F_SETB, fill=fg, anchor="lm")
    d.rounded_rectangle((280, 130, SW - 30, 610), 16, fill=card)
    # size slider
    d.text((300, TRACK[2]), "Size", font=F_SET, fill=fg, anchor="lm")
    d.rounded_rectangle((TRACK[0], TRACK[2] - 4, TRACK[1], TRACK[2] + 4), 4, fill=line)
    kx = TRACK[0] + v * (TRACK[1] - TRACK[0])
    d.rounded_rectangle((TRACK[0], TRACK[2] - 4, kx, TRACK[2] + 4), 4, fill=acc)
    d.ellipse((kx - 15, TRACK[2] - 15, kx + 15, TRACK[2] + 15), fill=(255, 255, 255), outline=line, width=2)
    d.line((300, 260, SW - 50, 260), fill=line, width=2)
    # magnification switch
    def switch(y, on=True):
        d.rounded_rectangle((SW - 130, y - 18, SW - 66, y + 18), 18, fill=(52, 199, 89) if on else line)
        d.ellipse((SW - 100, y - 15, SW - 70, y + 15), fill="white")
    d.text((300, 310), "Magnification", font=F_SET, fill=fg, anchor="lm"); switch(310)
    d.line((300, 360, SW - 50, 360), fill=line, width=2)
    d.text((300, 395), "Appearance", font=F_SET, fill=fg, anchor="lm")
    x0, x1, y0, y1 = SEG
    d.rounded_rectangle((x0, y0, x1, y1), 12, fill=lerpc((232, 232, 238), (30, 30, 36), dark))
    segw = (x1 - x0) / 3
    sx = x0 + 4 + seg_sel * segw
    d.rounded_rectangle((sx, y0 + 4, sx + segw - 8, y1 - 4), 10, fill=lerpc((255, 255, 255), (78, 78, 90), dark))
    for k, name in enumerate(["Light", "Dark", "Match Windows"]):
        d.text((x0 + segw * (k + 0.5), (y0 + y1) / 2), name, font=F_SMALL, fill=fg, anchor="mm")
    d.line((300, 510, SW - 50, 510), fill=line, width=2)
    d.text((300, 560), "Hide the Windows taskbar", font=F_SET, fill=fg, anchor="lm"); switch(560)
    out = Image.new("RGBA", (SW, SH), (0, 0, 0, 0)); out.paste(im, (0, 0), rounded_mask(SW, SH, 26))
    return out

SEG_DARK = (SX + SEG[0] + (SEG[1] - SEG[0]) * 0.5, SY + (SEG[2] + SEG[3]) / 2)

# ---------------------------------------------------------------- dock model

ITEMS = [("Files", ic_files, True), ("Browser", ic_browser, True), ("Mail", ic_mail, False),
         ("Music", ic_music, False), ("Photos", ic_photos, False), ("Notes", ic_notes, False),
         ("Terminal", ic_terminal, False), ("Calendar", ic_calendar, False), ("Settings", ic_settings, False),
         ("|", None, False), ("Downloads", ic_downloads, False), ("Trash", ic_trash, False)]
NAMES = [n for n, _, _ in ITEMS]
GAP, PAD, MAXMAG = 12, 14, 1.75

FAN = [("Sunset.jpg", (255, 140, 60), "JPG", True), ("Invoice.pdf", (230, 60, 60), "PDF", False),
       ("Trip.mp4", (140, 80, 230), "MP4", False), ("Project.zip", (110, 116, 130), "ZIP", False),
       ("Budget.xlsx", (40, 160, 90), "XLS", False), ("Notes.txt", (60, 120, 230), "TXT", False)]


class Assets:
    def __init__(self):
        self.bg = make_wallpaper()
        self.bg_blur = self.bg.filter(ImageFilter.GaussianBlur(28))
        self.icons = {n: fn() for n, fn, _ in ITEMS if fn}
        self.docs = [doc_icon(c, lab, ph) for _, c, lab, ph in FAN]
        self.cursor = cursor_img()
        self.music = make_music_window()
        self.music_np = np.array(self.music)
        self.music_sh = soft_shadow(MW, MH, 18)
        self.thumbs = [make_thumb((30, 120, 230), "News"), make_thumb((240, 110, 60), "Recipes")]
        self._sized = {}

    def icon(self, name, size, docs=False):
        key = (name, size, docs)
        if key not in self._sized:
            if len(self._sized) > 4000:
                self._sized.clear()
            src = self.docs[name] if docs else self.icons[name]
            self._sized[key] = src.resize((max(1, size), max(1, size)), Image.LANCZOS)
        return self._sized[key]


def slots(sizes, pres):
    """Centres for the given per-item sizes and presence (0..1)."""
    widths = [(2 if n == "|" else s) * p for n, s, p in zip(NAMES, sizes, pres)]
    total = sum(w + GAP * p for w, p in zip(widths, pres)) - GAP + 8
    x = W / 2 - total / 2; out = []
    for n, w, p in zip(NAMES, widths, pres):
        if n == "|": x += 4
        out.append(x + w / 2); x += w + GAP * p
        if n == "|": x += 4
    return out


def base_centers(base, pres=None):
    pres = pres or [1.0] * len(ITEMS)
    return slots([base] * len(ITEMS), pres)


def layout(cursor_x, hover, base, pres):
    bc = base_centers(base, pres)
    sizes = []
    for n, bx in zip(NAMES, bc):
        g = math.exp(-((bx - cursor_x) ** 2) / (2 * (base * 1.25) ** 2))
        sizes.append(base * (1 + (MAXMAG - 1) * g * hover))
    cs = slots(sizes, pres)
    items = list(zip(NAMES, cs, sizes, pres))
    first = next(i for i in items if i[3] > 0)
    left = first[1] - first[2] * first[3] / 2 - PAD
    right = items[-1][1] + items[-1][2] / 2 + PAD
    return items, left, right

# ---------------------------------------------------------------- timeline helpers

def dock_base(t):
    return lerp(B0, B1, ease_in_out(prog(t, 26.8, 27.8)))

def dock_dark(t):
    return ease_in_out(prog(t, 28.5, 28.9))

def presence(t):
    p = [1.0] * len(ITEMS)
    p[NAMES.index("Calendar")] = 1 - ease_in_out(prog(t, 21.6, 22.2))
    return p

def dock_offset(t):
    intro = (1 - ease_out_back(prog(t, 1.4, 2.4))) * 320 if t < 2.4 else 0
    hide = ease_in_out(prog(t, 23.2, 23.7)) * (1 - ease_out(prog(t, 24.8, 25.3))) * (H - DOCK_BOTTOM + 66 + 2 * PAD + 80)
    return intro + hide


BC = base_centers(B0)
Y_ICON = DOCK_BOTTOM - PAD - B0 / 2
I = NAMES.index
CAL_X = BC[I("Calendar")]
KEYS = [
    (3.4, W * 0.93, DOCK_BOTTOM - 420), (4.2, BC[0], Y_ICON), (6.4, BC[-1], Y_ICON), (7.1, BC[I("Music")], Y_ICON),
    (10.4, BC[I("Music")], Y_ICON), (12.5, BC[I("Music")], Y_ICON), (13.1, BC[I("Browser")], Y_ICON),
    (15.4, BC[I("Browser")], Y_ICON), (16.0, BC[I("Downloads")], Y_ICON), (19.9, BC[I("Downloads")], Y_ICON),
    (20.5, CAL_X, Y_ICON), (20.6, CAL_X, Y_ICON), (21.4, CAL_X - 40, Y_ICON - 300), (22.7, CAL_X - 40, Y_ICON - 300),
    (23.2, W * 0.57, DOCK_BOTTOM - 490), (24.2, W * 0.57, DOCK_BOTTOM - 490), (24.8, W * 0.52, H - 15), (25.7, W * 0.52, H - 40),
    (26.6, knob_x(V0), SY + TRACK[2]), (26.8, knob_x(V0), SY + TRACK[2]), (27.8, knob_x(V1), SY + TRACK[2]),
    (28.4, SEG_DARK[0], SEG_DARK[1]), (29.5, SEG_DARK[0], SEG_DARK[1]), (30.1, W * 0.93, DOCK_BOTTOM - 340),
]
CLICKS = [("Music", 7.25, 7.45), ("Music", 10.2, 10.35), ("Downloads", 16.05, 16.25), ("Calendar", 20.5, 20.6)]


def cursor_path(t):
    vis = min(prog(t, 3.4, 3.7), 1 - prog(t, 29.8, 30.1))
    if t <= KEYS[0][0]:
        return (KEYS[0][1], KEYS[0][2]), 0.0
    for (t0, x0, y0), (t1, x1, y1) in zip(KEYS, KEYS[1:]):
        if t <= t1:
            e = ease_in_out(prog(t, t0, t1))
            return (lerp(x0, x1, e), lerp(y0, y1, e)), vis
    return (KEYS[-1][1], KEYS[-1][2]), 0.0

# ---------------------------------------------------------------- genie

def genie(src, p, icon_cx, icon_top, icon_s):
    h, w = src.shape[:2]
    rs = np.arange(h, dtype=np.float64); v = rs / (h - 1)
    a = np.clip(p * 1.5 - (1 - v) * 0.5, 0, 1); e = a * a * (3 - 2 * a)
    b = np.clip(p * 2.0 - (1 - v) * 1.0, 0, 1); f = b * b * (3 - 2 * b)
    ys = (MY + rs) * (1 - e) + (icon_top + v * icon_s) * e
    ws = w * (1 - f) + icon_s * f
    cxs = (MX + w / 2) * (1 - f) + icon_cx * f
    y0, y1 = int(math.floor(ys.min())), int(math.ceil(ys.max()))
    canvas = np.zeros((y1 - y0 + 1, W, 4), np.uint8)
    Ys = np.arange(y0, y1 + 1)
    rr = np.interp(Ys, ys, rs); wr = np.interp(Ys, ys, ws); cr = np.interp(Ys, ys, cxs)
    for i in range(len(Ys)):
        wi = int(round(wr[i]))
        if wi < 1:
            continue
        cols = np.linspace(0, w - 1, wi).astype(int)
        x0 = int(round(cr[i] - wi / 2))
        a0, a1 = max(0, x0), min(W, x0 + wi)
        if a1 > a0:
            canvas[i, a0:a1] = src[int(round(rr[i])), cols[a0 - x0:a1 - x0]]
    if p > 0.85:
        canvas[..., 3] = (canvas[..., 3] * (1 - (p - 0.85) / 0.15)).astype(np.uint8)
    return Image.fromarray(canvas, "RGBA"), y0

# ---------------------------------------------------------------- frame

CAPTIONS = [(3.8, 7.0, "Icons grow as you glide past"), (7.1, 9.4, "Click to open: it bounces"),
            (9.5, 12.4, "Click again: genie into the Dock"), (12.5, 15.4, "Hover for live window previews"),
            (15.5, 19.9, "Folders open as stacks"), (20.0, 22.6, "Drag an icon off to remove it"),
            (22.7, 25.6, "Auto-hide: away and back"), (25.7, 29.9, "Settings change it live")]


def render(t, A):
    fr = A.bg.copy()

    a = window(t, 0.25, 3.8, 0.6)
    if a > 0:
        rise = (1 - ease_out(prog(t, 0.25, 1.2))) * 60 - ease_in_out(prog(t, 3.2, 3.8)) * 40
        center_text(fr, "WinDock", F_TITLE, TITLE_Y, a, rise)
        center_text(fr, "A Mac-style dock for Windows 11", F_SUB, SUB_Y, window(t, 0.7, 3.8, 0.6), rise)

    for t0, t1, txt in CAPTIONS:
        ca = window(t, t0, t1, 0.4)
        if ca > 0:
            center_text(fr, txt, F_CAP, CAP_Y, ca, (1 - ease_out(prog(t, t0, t0 + 0.4))) * 30)
    # stack mode indicator
    sa = window(t, 15.6, 19.9, 0.4)
    if sa > 0:
        mode = 0 if t < 17.5 else 1 if t < 18.65 else 2
        x = W / 2 - 3 * 150 / 2
        for k, name in enumerate(["Fan", "Grid", "List"]):
            p = pill(name, F_SEG, (255, 255, 255, 235) if k == mode else (255, 255, 255, 50),
                     fg=(40, 30, 70) if k == mode else (255, 255, 255), padx=26, pady=4, radius=26)
            paste(fr, p, x + k * 150 + 75 - p.width / 2, SEG_Y, sa)

    draw_taskbar(fr, A, t)
    if t < 1.4:
        return finish_frame(fr, t)

    base = dock_base(t); dark = dock_dark(t); pres = presence(t); dy = dock_offset(t)
    bar_h = base + PAD * 2
    (cx, cy), cvis = cursor_path(t)
    top_edge = DOCK_BOTTOM - bar_h + dy
    up = clamp(1 - (top_edge - 20 - cy) / 140)
    down = clamp(1 - (cy - (DOCK_BOTTOM + dy + 10)) / 40)
    dragging = 20.6 <= t < 21.5
    hover = up * down * (cvis > 0.05) * ease_out(prog(t, 3.6, 4.2)) * (0 if dragging else 1)
    items, left, right = layout(cx, hover, base, pres)
    pos = {n: (x, s) for n, x, s, _ in items}

    # music window: open (scale + fade), then genie into the icon
    mx, ms = pos["Music"]
    base_y = DOCK_BOTTOM - PAD + dy
    if 8.9 <= t < 10.3:
        oa = ease_out(prog(t, 8.9, 9.3))
        sc = lerp(0.9, 1.0, oa)
        sh, off = A.music_sh
        paste(fr, sh, MX - off, MY - off, oa * 0.9)
        m = A.music.resize((int(MW * sc), int(MH * sc)), Image.LANCZOS) if sc < 0.999 else A.music
        paste(fr, m, MX + (MW - m.width) / 2, MY + (MH - m.height) / 2, oa)
    elif 10.3 <= t < 11.1:
        img, y0 = genie(A.music_np, ease_in_out(prog(t, 10.3, 11.1)), mx, base_y - ms, ms)
        paste(fr, img, 0, y0)

    # dock bar
    x0, x1 = int(left), int(math.ceil(right))
    y0, y1 = int(top_edge), int(DOCK_BOTTOM + dy)
    bw, bh = x1 - x0, y1 - y0
    if y0 < H:
        sh = Image.new("RGBA", (bw + 80, bh + 80), (0, 0, 0, 0))
        ImageDraw.Draw(sh).rounded_rectangle((40, 50, bw + 40, bh + 40), 30, fill=(10, 0, 30, 110))
        paste(fr, sh.filter(ImageFilter.GaussianBlur(16)), x0 - 40, y0 - 40)
        cy0, cy1 = max(0, y0), min(H, y1)
        g = A.bg_blur.crop((x0, cy0, x1, cy1))
        light = Image.blend(g, Image.new("RGBA", g.size, (255, 255, 255, 255)), 0.24)
        darkg = Image.blend(g, Image.new("RGBA", g.size, (22, 20, 32, 255)), 0.5)
        glass = Image.blend(light, darkg, dark) if dark > 0 else light
        glass.putalpha(rounded_mask(bw, bh, 30).crop((0, cy0 - y0, bw, cy1 - y0)))
        paste(fr, glass, x0, cy0)
        border = Image.new("RGBA", (bw * 3, bh * 3), (0, 0, 0, 0))
        ImageDraw.Draw(border).rounded_rectangle((1, 1, bw * 3 - 2, bh * 3 - 2), 90,
                                                 outline=(255, 255, 255, int(lerp(120, 60, dark))), width=4)
        paste(fr, border.resize((bw, bh), Image.LANCZOS), x0, y0)

    running = {"Files", "Browser"} | ({"Music"} if t >= 8.6 else set())
    stack_open = 16.2 <= t < 19.75
    previews_on = 13.4 <= t < 15.4
    labels = []
    for name, icx, size, p in items:
        if name == "|":
            ImageDraw.Draw(fr).line((icx, base_y - base + 6, icx, base_y - 6), fill=(255, 255, 255, 110), width=2)
            continue
        if p <= 0.01 or (name == "Calendar" and t >= 20.6):
            continue
        s = int(round(size))
        yoff = 0.0
        if name == "Music" and 7.45 <= t <= 8.85:
            u = (t - 7.45) / 1.4
            yoff = -abs(math.sin(u * 2 * math.pi)) * 70 * (1 - 0.45 * u)
        ic = A.icon(name, s)
        if any(n == name and c0 <= t <= c1 for n, c0, c1 in CLICKS):
            dk = Image.new("RGBA", ic.size, (0, 0, 0, 0))
            dk.putalpha(ic.getchannel("A").point(lambda v: v * 90 // 255))
            ic = Image.alpha_composite(ic, dk)
        paste(fr, ic, icx - s / 2, base_y - s + yoff)
        if name in running:
            ImageDraw.Draw(fr).ellipse((icx - 4, base_y + 5, icx + 4, base_y + 13), fill=(255, 255, 255, 230))
        if hover > 0.6 and abs(icx - cx) < size / 2 + GAP / 2 and cvis > 0.5 and not stack_open and not previews_on:
            labels.append((name, icx, base_y - s + yoff))
    for name, lx, ly in labels:
        p = pill(name, F_LABEL, (30, 30, 40, 175))
        paste(fr, p, lx - p.width / 2, ly - p.height - 18)

    # window previews over Browser
    pa = ease_out(prog(t, 13.4, 13.7)) * (1 - ease_in_out(prog(t, 15.1, 15.4)))
    if pa > 0:
        bx, bs = pos["Browser"]
        pw, ph = 2 * 250 + 3 * 18, 160 + 18 * 2 + 40
        px = clamp(bx - pw / 2, 20, W - 20 - pw); py = base_y - bs - ph - 26
        panel = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
        g = A.bg_blur.crop((int(px), int(py), int(px) + pw, int(py) + ph))
        g = Image.blend(g, Image.new("RGBA", g.size, (30, 28, 44, 255)), 0.55)
        g.putalpha(rounded_mask(pw, ph, 22)); panel.alpha_composite(g)
        dp = ImageDraw.Draw(panel)
        for k, (th, title) in enumerate(zip(A.thumbs, ["News - Browser", "Recipes - Browser"])):
            tx = 18 + k * (250 + 18)
            panel.alpha_composite(th, (tx, 18))
            if k == 0 and t > 14.2:
                dp.rounded_rectangle((tx - 3, 15, tx + 253, 181), 14, outline=(10, 122, 255, 255), width=4)
            dp.text((tx + 125, 18 + 160 + 22), title, font=F_SMALL, fill=(240, 240, 248), anchor="mm")
        sc = lerp(0.92, 1.0, pa)
        if sc < 0.999:
            panel = panel.resize((int(pw * sc), int(ph * sc)), Image.LANCZOS)
        paste(fr, panel, px + (pw - panel.width) / 2, py + (ph - panel.height), pa)

    # folder stacks over Downloads
    fx, fs = pos["Downloads"]
    fy = DOCK_BOTTOM - bar_h + dy
    fo = ease_out(prog(t, 16.2, 16.9)) * (1 - ease_in_out(prog(t, 17.3, 17.5)))
    if fo > 0:
        for k in range(len(FAN)):
            kk = clamp((fo * (len(FAN) + 2) - k) / 3)
            if kk <= 0:
                continue
            e = ease_out(kk)
            tx, ty = fx - 7 * k ** 1.6, fy - 30 - FAN_STEP * (k + 1)
            x, y = lerp(fx, tx, e), lerp(fy - 30, ty, e)
            ic = A.icon(k, int(84 * lerp(0.4, 1.0, e)), docs=True).rotate(-1.6 * k * e, resample=Image.BICUBIC, expand=True)
            paste(fr, ic, x - ic.width / 2, y - ic.height / 2, e)
            p = pill(FAN[k][0], F_FAN, (25, 25, 35, 185), padx=14, pady=4)
            paste(fr, p, x - 60 - p.width, y - p.height / 2, e)
        ea = clamp(fo * 1.4 - 0.4)
        if ea > 0:
            p = pill("Open in Explorer  \u203a", F_FAN, (255, 255, 255, 60))
            paste(fr, p, fx - 7 * 6 ** 1.6 - p.width / 2 - 20, fy - 30 - FAN_STEP * 7 + 10, ea)
    for mode, (o0, o1, c0, c1) in (("grid", (17.55, 17.85, 18.45, 18.6)), ("list", (18.65, 18.95, 19.5, 19.75))):
        oa = ease_out(prog(t, o0, o1)) * (1 - ease_in_out(prog(t, c0, c1)))
        if oa <= 0:
            continue
        if mode == "grid":
            pw, ph = 3 * 170 + 30, 2 * 160 + 130
        else:
            pw, ph = 470, 6 * 62 + 120
        px = clamp(fx - pw / 2, 20, W - 20 - pw); py = fy - 26 - ph
        panel = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
        g = A.bg_blur.crop((int(px), int(py), int(px) + pw, int(py) + ph))
        g = Image.blend(g, Image.new("RGBA", g.size, (30, 28, 44, 255)), 0.55)
        g.putalpha(rounded_mask(pw, ph, 24)); panel.alpha_composite(g)
        dp = ImageDraw.Draw(panel)
        dp.text((pw / 2, 36), "Downloads", font=F_LABEL, fill="white", anchor="mm")
        if mode == "grid":
            for k in range(6):
                gx, gy = 15 + (k % 3) * 170 + 85, 70 + (k // 3) * 160
                panel.alpha_composite(A.icon(k, 96, docs=True), (int(gx - 48), gy))
                dp.text((gx, gy + 118), FAN[k][0], font=F_SMALL, fill=(235, 235, 245), anchor="mm")
        else:
            for k in range(6):
                ly = 66 + k * 62
                if k == 2:
                    dp.rounded_rectangle((12, ly, pw - 12, ly + 56), 12, fill=(10, 122, 255, 200))
                panel.alpha_composite(A.icon(k, 46, docs=True), (24, ly + 5))
                dp.text((84, ly + 28), FAN[k][0], font=F_SET, fill="white", anchor="lm")
        dp.text((pw / 2, ph - 34), "Open in Explorer  \u203a", font=F_SMALL, fill=(200, 210, 255), anchor="mm")
        sc = lerp(0.9, 1.0, oa)
        if sc < 0.999:
            panel = panel.resize((int(pw * sc), int(ph * sc)), Image.LANCZOS)
        paste(fr, panel, px + (pw - panel.width) / 2 + (fx - (px + pw / 2)) * (1 - sc), py + (ph - panel.height), oa)

    # dragged Calendar icon, "Remove" label, poof
    if dragging:
        s = int(base * 1.1)
        paste(fr, A.icon("Calendar", s), cx - s / 2, cy - s / 2 - 10, 0.92)
        ra = clamp((Y_ICON - 120 - cy) / 60)
        if ra > 0:
            p = pill("Remove", F_LABEL, (30, 30, 40, 190))
            paste(fr, p, cx - p.width / 2, cy - s / 2 - p.height - 24, ra)
    pf = prog(t, 21.5, 22.05)
    if 0 < pf < 1:
        px, py = CAL_X - 40, Y_ICON - 300 - 10
        poof = Image.new("RGBA", (300, 300), (0, 0, 0, 0)); dp = ImageDraw.Draw(poof)
        for k in range(8):
            ang = k * math.pi / 4 + 0.3
            rad = lerp(10, 70, ease_out(pf)); r = lerp(26, 8, pf)
            ox, oy = 150 + math.cos(ang) * rad, 150 + math.sin(ang) * rad
            dp.ellipse((ox - r, oy - r, ox + r, oy + r), fill=(255, 255, 255, int(230 * (1 - pf))))
        dp.ellipse((150 - 40 * (1 - pf), 150 - 40 * (1 - pf), 150 + 40 * (1 - pf), 150 + 40 * (1 - pf)), fill=(255, 255, 255, int(200 * (1 - pf))))
        paste(fr, poof.filter(ImageFilter.GaussianBlur(2)), px - 150, py - 150)

    # Settings window
    so = ease_out(prog(t, 25.8, 26.2)) * (1 - ease_in_out(prog(t, 29.4, 29.8)))
    if so > 0:
        v = lerp(V0, V1, ease_in_out(prog(t, 26.8, 27.8)))
        seg = lerp(0, 1, ease_in_out(prog(t, 28.5, 28.8)))
        win = draw_settings(dark, v, seg)
        sh, off = soft_shadow(SW, SH, 26, alpha=140)
        sc = lerp(0.94, 1.0, so)
        paste(fr, sh, SX - off, SY - off, so)
        if sc < 0.999:
            win = win.resize((int(SW * sc), int(SH * sc)), Image.LANCZOS)
        paste(fr, win, SX + (SW - win.width) / 2, SY + (SH - win.height) / 2, so)

    if cvis > 0:
        paste(fr, A.cursor, cx - 6, cy - 6, cvis)

    # end card
    ea = ease_out(prog(t, 30.2, 30.9))
    if ea > 0:
        center_text(fr, "WinDock", F_TITLE, END_Y[0], ea, (1 - ea) * 40)
        center_text(fr, "A Mac-style dock for Windows 11", F_SUB, END_Y[1], ease_out(prog(t, 30.4, 31.1)))
        for i, chip in enumerate(["No admin rights", "No telemetry", "Free & open source (MIT)"]):
            ca = ease_out(prog(t, 30.7 + i * 0.25, 31.3 + i * 0.25))
            if ca <= 0:
                continue
            lab = text_layer(chip, F_CHIP, shadow=False, pad=22)
            p = Image.new("RGBA", (lab.width + 20, lab.height), (0, 0, 0, 0))
            ImageDraw.Draw(p).rounded_rectangle((0, 0, p.width - 1, p.height - 1), p.height // 2, fill=(255, 255, 255, 52), outline=(255, 255, 255, 120), width=2)
            p.alpha_composite(lab, (10, 0))
            paste(fr, p, W / 2 - p.width / 2, END_Y[2] + i * END_Y[3] + (1 - ca) * 30, ca)

    return finish_frame(fr, t)


def finish_frame(fr, t):
    f = min(ease_out(prog(t, 0, 0.4)), 1 - prog(t, DUR - 0.5, DUR))
    out = fr.convert("RGB")
    if f < 1:
        out = Image.blend(Image.new("RGB", (W, H)), out, f)
    return out


def main():
    A = Assets()
    if "--stills" in sys.argv:
        os.makedirs(os.path.join(HERE, "stills"), exist_ok=True)
        for s in sys.argv[sys.argv.index("--stills") + 1:]:
            p = os.path.join(HERE, "stills", ("wide-" if WIDE else "") + f"t{float(s):05.2f}.png")
            render(float(s), A).save(p); print(p)
        return
    import imageio_ffmpeg
    path = os.path.join(HERE, "WinDock-wide.mp4" if WIDE else "WinDock-reel.mp4")
    n = int(round(DUR * FPS))
    wr = imageio_ffmpeg.write_frames(path, (W, H), fps=FPS, codec="libx264", pix_fmt_out="yuv420p",
                                     quality=None, bitrate=None, macro_block_size=8,
                                     output_params=["-crf", "18", "-preset", "slow", "-movflags", "+faststart",
                                                    "-profile:v", "high", "-level", "4.1"])
    wr.send(None)
    for i in range(n):
        wr.send(render(i / FPS, A).tobytes())
        if i % 60 == 0:
            print(f"frame {i}/{n}", flush=True)
    wr.close()
    print(path)


if __name__ == "__main__":
    main()
