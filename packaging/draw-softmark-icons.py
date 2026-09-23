"""Draw the Softmark app icon and write the per-platform icon files.

The icon is drawn here instead of being resized from a single master, because
each platform wants its own geometry (macOS keeps wide margins and a squircle,
Windows and Linux fill the square almost completely), and because the small
sizes need a flat, simplified drawing: the volumetric highlight and shadow turn
into mud below 32px.

Usage: python3 packaging/draw-softmark-icons.py
"""

from __future__ import annotations

import struct
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "packaging" / "assets"
WINDOW_ICON = ROOT / "src" / "MarkMello.Presentation" / "Assets" / "Icons" / "softmark.ico"
INSTALLER_ICON = ROOT / "packaging" / "windows" / "softmark-installer.ico"
LINUX_ICON = ROOT / "packaging" / "linux" / "softmark.png"
MAC_ICONSET = ROOT / "packaging" / "macos" / "AppIcon.iconset"

# Colours of the "зефирный объём" artboard.
TILE_TOP = (218, 122, 80)
TILE_BOTTOM = (180, 83, 46)
TILE_FLAT = (200, 100, 59)
BODY_LIGHT = (255, 255, 255)
BODY_MID = (251, 241, 230)
BODY_DARK = (231, 207, 184)
BODY_FLAT = (251, 244, 236)
UNDER = (217, 183, 155)
SHADOW = (126, 54, 24)
WARM_EDGE = (207, 162, 131)

# The S, in a 0..100 box: three cubic segments and the stroke width.
S_SEGMENTS = [
    ((63, 31), (58, 22), (37, 22), (37, 36)),
    ((37, 36), (37, 50), (63, 47), (63, 63)),
    ((63, 63), (63, 78), (40, 79), (35, 69)),
]
S_WIDTH = 15.0
# The two accents of the artboard: a rim light inside the upper bend of the S,
# and a warm bounce inside the lower one.
RIM_ARC = ((58, 27), (53, 21), (43, 22), (40, 29))
WARM_ARC = ((41, 71), (46, 77), (56, 76), (60, 70))

# Platform geometry, in fractions of the icon canvas: how much of the canvas the
# tile fills, how round its corners are, and whether the artwork carries a shadow.
PLATFORMS = {
    "macos": {"inset": 0.098, "radius": 0.225, "shadow": True},
    "windows": {"inset": 0.023, "radius": 0.160, "shadow": False},
    "linux": {"inset": 0.039, "radius": 0.195, "shadow": False},
}

def supersample(size):
    """Small icons need more supersampling; big ones would only waste memory."""
    return 4 if size <= 256 else 2


def cubic(p0, p1, p2, p3, steps):
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        x = u * u * u * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t * t * t * p3[0]
        y = u * u * u * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t * t * t * p3[1]
        yield x, y


def stroke_mask(size, segments, width, scale, offset, dx=0.0, dy=0.0):
    """A round-capped stroke of the given path, drawn by stamping circles."""
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    r = width / 2 * scale
    for seg in segments:
        for x, y in cubic(*seg, steps=900):
            cx = offset + (x + dx) * scale
            cy = offset + (y + dy) * scale
            draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)
    return mask


def clip(mask, into):
    """Keep only the part of `mask` that falls inside `into`."""
    return ImageChops.darker(mask, into)


def linear_gradient(size, top, bottom):
    grad = Image.new("RGB", (1, size))
    for y in range(size):
        t = y / max(size - 1, 1)
        grad.putpixel((0, y), tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return grad.resize((size, size), Image.Resampling.BILINEAR)


def body_gradient(size, body_mask, steps=192):
    """Cream gradient of the letter: white top-left, warm bottom-right.

    It spans the letter itself, not the whole tile — the same as the SVG
    gradient, which is measured in the path's own bounding box.
    """
    left, top, right, bottom = body_mask.getbbox()
    grad = Image.new("RGB", (steps, steps))
    px = grad.load()
    for y in range(steps):
        for x in range(steps):
            t = (x / steps * 0.45 + y / steps * 0.55)
            if t < 0.55:
                k = t / 0.55
                c = tuple(round(BODY_LIGHT[i] + (BODY_MID[i] - BODY_LIGHT[i]) * k) for i in range(3))
            else:
                k = (t - 0.55) / 0.45
                c = tuple(round(BODY_MID[i] + (BODY_DARK[i] - BODY_MID[i]) * k) for i in range(3))
            px[x, y] = c
    fill = Image.new("RGB", (size, size), BODY_MID)
    fill.paste(grad.resize((right - left, bottom - top), Image.Resampling.BILINEAR), (left, top))
    return fill


def rounded_mask(size, box, radius):
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius=radius, fill=255)
    return mask


def gloss_mask(size, scale, origin):
    """The light band across the top of the tile, bulging down in the middle."""
    mask = Image.new("L", (size, size), 0)
    points = [(origin + 8 * scale, origin + 8 * scale), (origin + 92 * scale, origin + 8 * scale),
              (origin + 92 * scale, origin + 30 * scale)]
    for i in range(61):
        t = i / 60
        u = 1 - t
        x = u * u * 92 + 2 * u * t * 50 + t * t * 8
        y = u * u * 30 + 2 * u * t * 46 + t * t * 30
        points.append((origin + x * scale, origin + y * scale))
    ImageDraw.Draw(mask).polygon(points, fill=255)
    return mask


def draw_icon(size, platform, flat=False):
    """Render one icon at `size` pixels. `flat` drops the volume for tiny sizes.

    Coordinates are the artboard's 0..100 box, where the tile itself is the
    rectangle 4..96: keeping that box is what makes the letter sit in the tile
    exactly as it does on the canvas.
    """
    spec = PLATFORMS[platform]
    big = size * supersample(size)
    inset = round(big * spec["inset"])
    side = big - inset * 2
    scale = side / 92  # the artboard tile spans 92 of its 100 units
    origin = inset - 4 * scale
    radius = side * spec["radius"]
    box = (inset, inset, inset + side - 1, inset + side - 1)

    canvas = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    tile_mask = rounded_mask(big, box, radius)

    if spec["shadow"] and not flat:
        shadow = Image.new("RGBA", (big, big), (0, 0, 0, 0))
        shadow.paste((70, 30, 14, 90), mask=tile_mask.transform(
            (big, big), Image.AFFINE, (1, 0, 0, 0, 1, -big * 0.012), resample=Image.BILINEAR))
        canvas = Image.alpha_composite(canvas, shadow.filter(ImageFilter.GaussianBlur(big * 0.012)))

    tile = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    if flat:
        tile.paste(TILE_FLAT + (255,), mask=tile_mask)
    else:
        tile.paste(linear_gradient(big, TILE_TOP, TILE_BOTTOM), mask=tile_mask)
        gloss = Image.new("RGBA", (big, big), (0, 0, 0, 0))
        gloss.paste((255, 255, 255, 31), mask=clip(gloss_mask(big, scale, origin), tile_mask))
        tile = Image.alpha_composite(tile, gloss)
    canvas = Image.alpha_composite(canvas, tile)

    width = S_WIDTH + (2.0 if flat else 0.0)
    if not flat:
        drop = Image.new("RGBA", (big, big), (0, 0, 0, 0))
        drop.paste(SHADOW + (115,), mask=stroke_mask(big, S_SEGMENTS, width + 1, scale, origin, dy=6))
        drop = drop.filter(ImageFilter.GaussianBlur(scale * 3))
        drop.putalpha(clip(drop.getchannel("A"), tile_mask))
        canvas = Image.alpha_composite(canvas, drop)

        under = Image.new("RGBA", (big, big), (0, 0, 0, 0))
        under.paste(UNDER + (255,), mask=stroke_mask(big, S_SEGMENTS, width + 1, scale, origin, dy=2))
        canvas = Image.alpha_composite(canvas, under)

    body = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    body_mask = stroke_mask(big, S_SEGMENTS, width, scale, origin)
    fill = Image.new("RGB", (big, big), BODY_FLAT) if flat else body_gradient(big, body_mask)
    body.paste(fill, mask=body_mask)
    canvas = Image.alpha_composite(canvas, body)

    if not flat:
        # The accents of the artboard: a white rim light on the upper bend, a
        # warm bounce on the lower one, and a single specular dot.
        warm = Image.new("RGBA", (big, big), (0, 0, 0, 0))
        warm.paste(WARM_EDGE + (140,), mask=stroke_mask(big, [WARM_ARC], 3, scale, origin))
        canvas = Image.alpha_composite(canvas, warm)

        rim = Image.new("RGBA", (big, big), (0, 0, 0, 0))
        rim.paste((255, 255, 255, 215), mask=stroke_mask(big, [RIM_ARC], 3.5, scale, origin))
        canvas = Image.alpha_composite(canvas, rim)

        dot = Image.new("RGBA", (big, big), (0, 0, 0, 0))
        cx, cy = origin + 45 * scale, origin + 33 * scale
        rx, ry = 3.2 * scale, 2.0 * scale
        ImageDraw.Draw(dot).ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=(255, 255, 255, 230))
        canvas = Image.alpha_composite(canvas, dot)

    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def render(size, platform):
    """Full artwork above 32px, flat drawing at 32px and below."""
    return draw_icon(size, platform, flat=size <= 32)


def dib_payload(image):
    """A bottom-up BGRA DIB with an empty AND mask, the classic ICO payload.

    Small sizes are stored this way instead of as PNG, because that is what every
    ICO reader understands; PNG payloads are only safe from 128px up.
    """
    w, h = image.size
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, w * h * 4, 0, 0, 0, 0)
    rows = []
    px = image.load()
    for y in range(h - 1, -1, -1):
        row = bytearray()
        for x in range(w):
            r, g, b, a = px[x, y]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    mask_stride = ((w + 31) // 32) * 4
    return header + b"".join(rows) + b"\x00" * (mask_stride * h)


def write_ico(path: Path, sizes, platform):
    """ICO holding its own drawing per size: DIB up to 64px, PNG above."""
    images = []
    for edge in sizes:
        image = render(edge, platform).convert("RGBA")
        if edge <= 64:
            images.append((edge, dib_payload(image)))
        else:
            buf = BytesIO()
            image.save(buf, format="PNG")
            images.append((edge, buf.getvalue()))

    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, payloads = b"", b""
    for edge, data in images:
        entries += struct.pack("<BBBBHHII", edge if edge < 256 else 0, edge if edge < 256 else 0,
                               0, 0, 1, 32, len(data), offset)
        offset += len(data)
        payloads += data
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(header + entries + payloads)


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    for platform in PLATFORMS:
        master = render(1024, platform)
        master.save(ASSETS / f"softmark-master-{platform}.png", format="PNG")

    ico_sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    write_ico(WINDOW_ICON, ico_sizes, "windows")
    write_ico(INSTALLER_ICON, ico_sizes, "windows")

    LINUX_ICON.parent.mkdir(parents=True, exist_ok=True)
    render(512, "linux").save(LINUX_ICON, format="PNG")

    MAC_ICONSET.mkdir(parents=True, exist_ok=True)
    for name, edge in [("icon_16x16.png", 16), ("icon_16x16@2x.png", 32), ("icon_32x32.png", 32),
                       ("icon_32x32@2x.png", 64), ("icon_128x128.png", 128), ("icon_128x128@2x.png", 256),
                       ("icon_256x256.png", 256), ("icon_256x256@2x.png", 512), ("icon_512x512.png", 512),
                       ("icon_512x512@2x.png", 1024)]:
        render(edge, "macos").save(MAC_ICONSET / name, format="PNG")

    print("Drew Softmark icons:")
    print(f"- {WINDOW_ICON}")
    print(f"- {INSTALLER_ICON}")
    print(f"- {LINUX_ICON}")
    print(f"- {MAC_ICONSET}")
    print(f"- {ASSETS}/softmark-master-*.png")
    print("Run `iconutil -c icns -o packaging/macos/Softmark.icns packaging/macos/AppIcon.iconset` for the bundle icon.")


if __name__ == "__main__":
    main()
