#!/usr/bin/env python3
"""Генерация иконок SkyScope: PNG (512/32) и ICO (16..256).

Рисует ретикл-прицел: оранжевое кольцо, засечки по сторонам, точка в центре
на тёмной скруглённой плашке. Суперсэмплинг x4 для гладких краёв.
"""
import math
import os
import shutil
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ACCENT = (255, 125, 31, 255)
ACCENT_SOFT = (255, 125, 31, 70)
BG_TOP = (24, 25, 30, 255)
BG_BOTTOM = (14, 14, 17, 255)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(4))


def make_icon(size: int) -> Image.Image:
    s = size * 4  # суперсэмплинг
    img = make_base_plate(s)

    cx = cy = s / 2

    ring_r = s * 0.285
    lw = max(2, int(s * 0.032))

    # мягкое свечение за кольцом
    glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    gdraw = ImageDraw.Draw(glow)
    gdraw.ellipse(
        [cx - ring_r - lw * 3, cy - ring_r - lw * 3, cx + ring_r + lw * 3, cy + ring_r + lw * 3],
        outline=ACCENT_SOFT,
        width=lw * 3,
    )
    glow = glow.filter(ImageFilter.GaussianBlur(lw * 2))
    img.alpha_composite(glow)
    d = ImageDraw.Draw(img)

    # кольцо
    d.ellipse(
        [cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r],
        outline=ACCENT,
        width=lw,
    )

    # засечки по сторонам
    tick_in = ring_r + s * 0.045
    tick_out = ring_r + s * 0.135
    cap = lw // 2
    for ang in (0, 90, 180, 270):
        a = math.radians(ang)
        x1, y1 = cx + tick_in * math.cos(a), cy + tick_in * math.sin(a)
        x2, y2 = cx + tick_out * math.cos(a), cy + tick_out * math.sin(a)
        d.line([(x1, y1), (x2, y2)], fill=ACCENT, width=lw)
        # округлые концы засечек
        for (px, py) in ((x1, y1), (x2, y2)):
            d.ellipse([px - cap, py - cap, px + cap, py + cap], fill=ACCENT)

    # точка в центре
    dot_r = s * 0.033
    d.ellipse([cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r], fill=ACCENT)

    return img.resize((size, size), Image.LANCZOS)


def make_settings_icon(size: int) -> Image.Image:
    # прицел на силуэте головы — как иконка раздела «Настройка»
    s = size * 4
    k = s / 512
    img = make_base_plate(s)
    d = ImageDraw.Draw(img)
    head = (216, 217, 222, 255)

    head_c, head_r = 256 * k, 78 * k
    d.ellipse(
        [head_c - head_r, 198 * k - head_r, head_c + head_r, 198 * k + head_r],
        fill=head,
    )
    d.polygon(_shoulders(k), fill=head)

    cx, cy = 256 * k, 198 * k
    ring_r, lw = 96 * k, 13 * k
    d.ellipse(
        [cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r],
        outline=ACCENT,
        width=int(lw),
    )
    tick_in, tick_out = 112 * k, 150 * k
    cap = int(lw / 2)
    for ang in (0, 90, 180, 270):
        a = math.radians(ang)
        x1, y1 = cx + tick_in * math.cos(a), cy + tick_in * math.sin(a)
        x2, y2 = cx + tick_out * math.cos(a), cy + tick_out * math.sin(a)
        d.line([(x1, y1), (x2, y2)], fill=ACCENT, width=int(lw))
        for (px, py) in ((x1, y1), (x2, y2)):
            d.ellipse([px - cap, py - cap, px + cap, py + cap], fill=ACCENT)

    dot_r = 15 * k
    d.ellipse([cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r], fill=ACCENT)
    return img.resize((size, size), Image.LANCZOS)


def _shoulders(k: float):
    # M 124 436 C 124 342 182 308 256 308 C 330 308 388 342 388 436 Z
    def cubic(p0, p1, p2, p3, n=48):
        pts = []
        for i in range(n + 1):
            t = i / n
            mt = 1 - t
            pts.append((
                mt ** 3 * p0[0] + 3 * mt * mt * t * p1[0] + 3 * mt * t * t * p2[0] + t ** 3 * p3[0],
                mt ** 3 * p0[1] + 3 * mt * mt * t * p1[1] + 3 * mt * t * t * p2[1] + t ** 3 * p3[1],
            ))
        return pts

    left = cubic((124, 436), (124, 342), (182, 308), (256, 308))
    right = cubic((256, 308), (330, 308), (388, 342), (388, 436))
    return [(x * k, y * k) for x, y in left + right]


def make_base_plate(s: int) -> Image.Image:
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    radius = int(s * 0.22)
    grad = Image.new("RGBA", (s, s))
    gd = ImageDraw.Draw(grad)
    for y in range(s):
        gd.line([(0, y), (s, y)], fill=lerp(BG_TOP, BG_BOTTOM, y / s))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=radius, fill=255)
    img.paste(grad, (0, 0), mask)
    return img


def main():
    assets = os.path.join(ROOT, "assets")
    build = os.path.join(ROOT, "build")
    app_assets = os.path.join(ROOT, "src", "SkyScope", "Assets")
    os.makedirs(assets, exist_ok=True)
    os.makedirs(build, exist_ok=True)
    os.makedirs(app_assets, exist_ok=True)

    icon512 = make_icon(512)
    icon512.save(os.path.join(assets, "icon.png"))

    tray = make_icon(32)
    tray.save(os.path.join(assets, "tray.png"))

    make_settings_icon(512).save(os.path.join(assets, "icon-settings.png"))

    # ICO с набором размеров
    sizes = [16, 24, 32, 48, 64, 128, 256]
    icon512.save(
        os.path.join(build, "icon.ico"),
        sizes=[(s, s) for s in sizes],
    )
    icon512.save(os.path.join(build, "icon.png"))

    # встроенные ресурсы приложения
    shutil.copyfile(os.path.join(build, "icon.ico"), os.path.join(app_assets, "icon.ico"))
    shutil.copyfile(os.path.join(assets, "icon.png"), os.path.join(app_assets, "icon.png"))
    print("icons written:", os.listdir(assets), os.listdir(build))


if __name__ == "__main__":
    main()
