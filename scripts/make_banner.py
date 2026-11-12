#!/usr/bin/env python3
"""Баннер и постер SkyScope для README (docs/)."""
import math
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOCS = os.path.join(ROOT, "docs")
FONT_DIR = r"C:\Windows\Fonts"
ACCENT = (255, 125, 31)


def font(name, size):
    return ImageFont.truetype(os.path.join(FONT_DIR, name), size)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def gradient(size, top, bottom):
    w, h = size
    img = Image.new("RGB", size)
    d = ImageDraw.Draw(img)
    for y in range(h):
        d.line([(0, y), (w, y)], fill=lerp(top, bottom, y / h))
    return img


def draw_reticle(img, cx, cy, r, color, lw, alpha=255):
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    c = color + (70,)
    gd.ellipse([cx - r - lw * 2, cy - r - lw * 2, cx + r + lw * 2, cy + r + lw * 2], outline=c, width=lw * 2)
    glow = glow.filter(ImageFilter.GaussianBlur(lw * 2))
    img.paste(Image.alpha_composite(img.convert("RGBA"), glow).convert("RGB"), (0, 0))

    d = ImageDraw.Draw(img)
    c = color + (alpha,)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=c, width=lw)
    for ang in (0, 90, 180, 270):
        a = math.radians(ang)
        x1, y1 = cx + (r + lw * 1.6) * math.cos(a), cy + (r + lw * 1.6) * math.sin(a)
        x2, y2 = cx + (r + lw * 4.4) * math.cos(a), cy + (r + lw * 4.4) * math.sin(a)
        d.line([(x1, y1), (x2, y2)], fill=c, width=lw)
        for (px, py) in ((x1, y1), (x2, y2)):
            d.ellipse([px - lw // 2, py - lw // 2, px + lw // 2, py + lw // 2], fill=c)
    dot = max(3, lw)
    d.ellipse([cx - dot, cy - dot, cx + dot, cy + dot], fill=c)


def draw_bust(img, cx, head_cy, head_rx, head_ry, color):
    d = ImageDraw.Draw(img)
    c = color + (255,)
    d.rounded_rectangle(
        [cx - head_rx * 0.31, head_cy + head_ry * 0.67,
         cx + head_rx * 0.31, head_cy + head_ry * 2.2],
        radius=head_rx * 0.17, fill=c)
    d.rounded_rectangle(
        [cx - head_rx * 1.84, head_cy + head_ry * 0.95,
         cx + head_rx * 1.84, head_cy + head_ry * 3.45],
        radius=head_rx * 1.31, fill=c)
    d.ellipse(
        [cx - head_rx, head_cy - head_ry, cx + head_rx, head_cy + head_ry],
        fill=c)


def dot_grid(img, step, color, alpha):
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(overlay)
    w, h = img.size
    for x in range(step // 2, w, step):
        for y in range(step // 2, h, step):
            d.ellipse([x - 1, y - 1, x + 1, y + 1], fill=color + (alpha,))
    img.paste(Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB"), (0, 0))


def banner():
    w, h = 1600, 460
    img = gradient((w, h), (18, 19, 24), (10, 10, 13))
    dot_grid(img, 42, (255, 255, 255), 10)

    glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([w - 720, -260, w + 220, 680], fill=(255, 125, 31, 34))
    glow = glow.filter(ImageFilter.GaussianBlur(120))
    img.paste(Image.alpha_composite(img.convert("RGBA"), glow).convert("RGB"), (0, 0))

    draw_bust(img, w - 330, h // 2, 91, 100, (68, 69, 77))
    draw_reticle(img, w - 330, h // 2, 128, ACCENT, 14)

    d = ImageDraw.Draw(img)
    d.text((120, 138), "SkyScope", font=font("arialbd.ttf", 108), fill=(238, 238, 240))
    tw = d.textlength("SkyScope", font=font("arialbd.ttf", 108))
    d.rectangle([120 + tw + 26, 168, 120 + tw + 40, 268], fill=ACCENT)
    d.text((124, 292), "Экранная лупа-прицел для Windows", font=font("segoeui.ttf", 40), fill=(155, 155, 164))
    d.text((124, 352), "Open source альтернатива Scope X  ·  Apache 2.0", font=font("segoeui.ttf", 28), fill=(110, 110, 120))

    img.save(os.path.join(DOCS, "banner.png"), optimize=True)
    print("banner.png")


def in_action(overlay_png):
    src = Image.open(overlay_png).convert("RGBA")
    bbox = src.getbbox()
    scope = src.crop(bbox) if bbox else src

    w, h = 1600, 900
    img = gradient((w, h), (24, 26, 34), (8, 9, 12)).convert("RGBA")
    dot_grid(img, 36, (255, 255, 255), 8)

    horizon = int(h * 0.62)
    d = ImageDraw.Draw(img)
    d.rectangle([0, horizon, w, h], fill=(12, 13, 17, 255))
    d.line([(0, horizon), (w, horizon)], fill=(255, 125, 31, 90), width=2)

    glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([w // 2 - 500, horizon - 420, w // 2 + 500, horizon + 380], fill=(255, 125, 31, 26))
    glow = glow.filter(ImageFilter.GaussianBlur(140))
    img = Image.alpha_composite(img, glow)

    px, py = int(w * 0.6 - scope.width / 2), int(h * 0.42 - scope.height / 2)
    img.alpha_composite(scope, (px, py))

    d = ImageDraw.Draw(img)
    d.text((60, h - 92), "Прицел виден только вам — оверлей исключён из захвата экрана",
           font=font("segoeui.ttf", 30), fill=(170, 170, 178))

    img.convert("RGB").save(os.path.join(DOCS, "in-action.png"), optimize=True)
    print("in-action.png")


if __name__ == "__main__":
    os.makedirs(DOCS, exist_ok=True)
    banner()
    overlay_png = os.path.join(DOCS, "screenshots", "overlay.png")
    if os.path.exists(overlay_png):
        in_action(overlay_png)
    else:
        print("overlay.png ещё нет — пропускаю in-action")
