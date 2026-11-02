"""Build crisp, flat microphone tray icons without third-party dependencies.

Geometry is drawn independently at each size with 8x coverage antialiasing.
The detailed branded app/executable assets are deliberately separate.
"""
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / 'src/SharpVoice.UI/Assets'
COLORS = {'neutral': (131, 145, 160), 'recording': (239, 68, 68), 'error': (217, 149, 8)}
SIZES = (16, 20, 24, 32, 40, 48)


def inside(x, y):
    # Filled capsule, a U-shaped cradle, then stem and base. Coordinates: 16x16.
    capsule = (5 <= x <= 11 and 4 <= y <= 7) or (x - 8) ** 2 + (y - 4) ** 2 <= 9 or (x - 8) ** 2 + (y - 7) ** 2 <= 9
    radius = (x - 8) ** 2 + (y - 8) ** 2
    cradle = (y >= 8 and 3.5 ** 2 <= radius <= 5.5 ** 2) or (6.5 <= y <= 8 and (2.5 <= x <= 4.5 or 11.5 <= x <= 13.5))
    stem = 7 <= x <= 9 and 12.5 <= y <= 15
    base = 5 <= x <= 11 and 14 <= y <= 15.5
    return capsule or cradle or stem or base


def raster(size, rgb):
    rgba = bytearray()
    for y in range(size):
        for x in range(size):
            coverage = sum(inside((x + (sx + .5) / 8) * 16 / size, (y + (sy + .5) / 8) * 16 / size) for sy in range(8) for sx in range(8))
            rgba.extend((*rgb, round(255 * coverage / 64)))
    return bytes(rgba)


def png(width, height, rgba):
    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data))
    rows = b''.join(b'\0' + rgba[y * width * 4:(y + 1) * width * 4] for y in range(height))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b'')


def dib(size, rgba):
    pixels = bytearray()
    masks = bytearray()
    stride = ((size + 31) // 32) * 4
    for y in reversed(range(size)):
        mask = bytearray(stride)
        for x in range(size):
            r, g, b, a = rgba[(y * size + x) * 4:(y * size + x + 1) * 4]
            pixels.extend((b, g, r, a))
            if a == 0:
                mask[x // 8] |= 128 >> (x % 8)
        masks.extend(mask)
    return struct.pack('<IiiHHIIiiii', 40, size, size * 2, 1, 32, 0, len(pixels), 0, 0, 0, 0) + pixels + masks


def build():
    previews = {}
    for state, rgb in COLORS.items():
        frames = []
        for size in SIZES:
            rgba = raster(size, rgb)
            assert rgba[3] == rgba[-1] == 0, 'Corners must be transparent'
            assert sum(a >= 128 for a in rgba[3::4]) >= size * size * .3, 'Silhouette too thin'
            frames.append(dib(size, rgba))
            previews[state, size] = rgba
        offset = 6 + 16 * len(SIZES)
        directory = bytearray(struct.pack('<HHH', 0, 1, len(SIZES)))
        for size, frame in zip(SIZES, frames):
            directory.extend(struct.pack('<BBBBHHII', size, size, 0, 0, 1, 32, len(frame), offset))
            offset += len(frame)
        (ASSETS / f'tray-microphone-{state}.ico').write_bytes(directory + b''.join(frames))
    # Actual-size icons on light/dark backgrounds, plus a 4x nearest-neighbor detail.
    width, height = 420, 160
    canvas = bytearray()
    for y in range(height):
        canvas.extend(bytes((245, 246, 248, 255) if y < 80 else (24, 27, 32, 255)) * width)
    for row in range(2):
        for column, state in enumerate(COLORS):
            for size, scale, dx in ((16, 1, 0), (24, 1, 28), (16, 4, 62)):
                rgba = previews[state, size]
                for y in range(size * scale):
                    for x in range(size * scale):
                        source = ((y // scale) * size + x // scale) * 4
                        target = ((row * 80 + 8 + y) * width + column * 140 + 8 + dx + x) * 4
                        a = rgba[source + 3] / 255
                        for c in range(3):
                            canvas[target + c] = round(rgba[source + c] * a + canvas[target + c] * (1 - a))
    preview = ROOT / '.build-cache/tray-icons-preview.png'
    preview.parent.mkdir(exist_ok=True)
    preview.write_bytes(png(width, height, canvas))
    print('Built three transparent tray icons at ' + ', '.join(map(str, SIZES)) + ' px.')


if __name__ == '__main__':
    build()
