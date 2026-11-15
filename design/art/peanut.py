"""Draws the WaNut peanut, pixel by pixel.

Kept from the old 16x16 peanut: the tilted two-lobe shell, its four browns and the dark outline.
New: a 24x24 grid (shown at 3x), a face, feet and a set of expressions.

Run: python design/art/peanut.py   ->  design/art/out/
"""
from pathlib import Path

from PIL import Image

SIZE = 24
OUT = Path(__file__).parent / "out"

# The old peanut's palette, plus a highlight and the face colors.
OUTLINE = "#6E4522"
LIGHT = "#C9955E"
MID = "#B07A45"
DARK = "#8E5E32"
HIGHLIGHT = "#E0B27C"
FACE = "#3A2614"
BLUSH = "#D9866A"
SWEAT = "#8CC8E8"
MUTED = "#8C8172"
STEAM = "#E6DFD2"

# (center x, center y, radius x, radius y): small upper lobe leaning right, big lower lobe.
LOBES = [(13.5, 6.5, 5.3, 5.3), (10.0, 14.5, 6.6, 6.2)]

# The expressions below are placed on the old 20x20 layout; this moves them onto the lower lobe.
SHIFT = (1, 2)


def lobe_at(x, y):
    """The lobe a pixel belongs to and how far out it sits (0 center, 1 edge), or None outside."""
    best = None
    for cx, cy, rx, ry in LOBES:
        d = ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2
        if d <= 1 and (best is None or d < best[1]):
            best = ((cx, cy, rx, ry), d)
    return best


def body():
    """Shell pixels: outline, three shades lit from the top left, and the old dimple texture."""
    inside = {(x, y) for x in range(SIZE) for y in range(SIZE) if lobe_at(x, y)}
    pixels = {}
    for x, y in inside:
        if any(n not in inside for n in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1))):
            pixels[(x, y)] = OUTLINE
            continue
        (cx, cy, rx, ry), _ = lobe_at(x, y)
        lit = -((x + 0.5 - cx) / rx + (y + 0.5 - cy) / ry) / 1.41
        color = LIGHT if lit > 0.3 else DARK if lit < -0.45 else MID
        if color == MID and (x + 2 * y) % 5 == 0:
            color = DARK
        if color == LIGHT and lit > 0.62:
            color = HIGHLIGHT
        pixels[(x, y)] = color
    # Clear the face area so the dimples do not read as freckles.
    for x in range(5, 15):
        for y in range(13, 18):
            if pixels.get((x, y)) == DARK and (x + 2 * y) % 5 == 0:
                pixels[(x, y)] = MID
    # Feet: one cell wide, two cells long.
    for x in (8, 12):
        pixels[(x, 21)] = OUTLINE
        pixels[(x, 22)] = OUTLINE
    return pixels


def put(pixels, color, *points):
    for x, y in points:
        pixels[(x + SHIFT[0], y + SHIFT[1])] = color


def idle(p):
    put(p, FACE, (6, 12), (6, 13), (11, 12), (11, 13), (8, 15), (9, 15))


def blink(p):
    put(p, FACE, (5, 13), (6, 13), (11, 13), (12, 13), (8, 15), (9, 15))


def happy(p):
    put(p, FACE, (4, 13), (5, 12), (6, 13), (11, 13), (12, 12), (13, 13), (7, 15), (8, 16), (9, 16), (10, 15))
    put(p, BLUSH, (4, 15), (13, 15))


def nervous(p):
    put(p, "#FFFDF9", (5, 12), (6, 12), (5, 13), (6, 13), (11, 12), (12, 12), (11, 13), (12, 13))
    put(p, FACE, (6, 13), (11, 13), (8, 15), (9, 15), (8, 16), (9, 16))
    put(p, SWEAT, (17, 9), (17, 10), (16, 10))


def sleepy(p):
    put(p, FACE, (5, 13), (6, 13), (11, 13), (12, 13), (8, 15))
    put(p, MUTED, (19, -2), (20, -2), (21, -2), (22, -2), (21, -1), (20, 0), (19, 1), (20, 1), (21, 1), (22, 1))


def hot(p):
    for point, color in list(p.items()):
        if color in (MID, LIGHT):
            p[point] = {MID: "#B9703F", LIGHT: "#D08A56"}[color]
    put(p, FACE, (5, 12), (6, 13), (12, 12), (11, 13), (8, 15), (9, 15), (8, 16), (9, 16))
    put(p, BLUSH, (4, 15), (5, 15), (12, 15), (13, 15))
    put(p, SWEAT, (2, 9), (2, 10), (18, 10), (18, 11))
    put(p, STEAM, (5, 0), (6, 1), (5, 2), (19, -2), (20, -1), (19, 0))


EXPRESSIONS = {"idle": idle, "blink": blink, "happy": happy, "nervous": nervous, "sleepy": sleepy, "hot": hot}


def render(name):
    pixels = body()
    EXPRESSIONS[name](pixels)
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    for (x, y), color in pixels.items():
        if 0 <= x < SIZE and 0 <= y < SIZE:
            image.putpixel((x, y), tuple(int(color[i:i + 2], 16) for i in (1, 3, 5)) + (255,))
    return image


def sheet(sprites, old):
    """The idle peanut again, then every expression, on the light and the dark window colors. One cell = 6px here."""
    scale, pad = 6, 12
    cell = SIZE * scale + pad
    width = cell * (len(sprites) + 1) + pad
    image = Image.new("RGBA", (width, cell * 2 + pad), "#F7F4EE")
    image.paste(Image.new("RGBA", (width, cell + pad // 2), "#1F1B16"), (0, cell + pad // 2))
    for row in range(2):
        for column, sprite in enumerate([old] + sprites):
            big = sprite.resize((sprite.width * scale, sprite.height * scale), Image.NEAREST)
            x = pad + column * cell + (SIZE * scale - big.width) // 2
            y = pad + row * cell + (SIZE * scale - big.height)
            image.paste(big, (x, y), big)
    return image


def main():
    OUT.mkdir(exist_ok=True)
    sprites = []
    for name in EXPRESSIONS:
        sprite = render(name)
        sprite.save(OUT / f"peanut-{name}-{SIZE}.png")
        sprites.append(sprite)
    # The app uses the idle sprite: copy out/peanut-idle-24.png to src/WaNut.App/Assets/peanut-24.png.
    old = sprites[0]
    sheet(sprites, old).save(OUT / "peanut-sheet.png")


if __name__ == "__main__":
    main()
