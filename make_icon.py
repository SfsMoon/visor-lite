"""Draws icon.png (256x256) for VisorLite without external libraries (pure-Python PNG writer, 4x supersampling)."""
import math
import struct
import sys
import zlib

W = H = 256
SS = 4  # supersampling per axis


def clamp(x, a=0.0, b=1.0):
    return a if x < a else b if x > b else x


def mix(c1, c2, t):
    return tuple(c1[i] + (c2[i] - c1[i]) * t for i in range(4))


def over(dst, src):
    """Alpha-composite src over dst (both RGBA floats 0..1, straight alpha)."""
    sa = src[3]
    da = dst[3] * (1 - sa)
    a = sa + da
    if a <= 0:
        return (0.0, 0.0, 0.0, 0.0)
    return tuple((src[i] * sa + dst[i] * da) / a for i in range(3)) + (a,)


def rounded_rect_sd(x, y, cx, cy, hw, hh, r):
    qx = abs(x - cx) - hw + r
    qy = abs(y - cy) - hh + r
    return math.hypot(max(qx, 0), max(qy, 0)) + min(max(qx, qy), 0) - r


def ellipse_inside(x, y, cx, cy, rx, ry):
    return ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2


def sample(x, y):
    c = (0.0, 0.0, 0.0, 0.0)
    # background: dark rounded square with vertical gradient
    if rounded_rect_sd(x, y, 128, 128, 124, 124, 36) <= 0:
        t = y / 256
        c = over(c, mix((0.09, 0.10, 0.14, 1), (0.03, 0.03, 0.05, 1), t))

    # helmet shell: orange ring (LC HUD orange)
    d_out = ellipse_inside(x, y, 128, 124, 96, 92)
    d_in = ellipse_inside(x, y, 128, 124, 80, 76)
    if d_out <= 1 and d_in > 1:
        c = over(c, (0.91, 0.42, 0.16, 1))

    # visor: translucent blue-white glass, darker towards the bottom
    v = ellipse_inside(x, y, 128, 128, 66, 46)
    if v <= 1:
        t = clamp((y - 82) / 92)
        c = over(c, mix((0.78, 0.88, 1.0, 0.55), (0.35, 0.45, 0.60, 0.45), t))
        # diagonal highlight streak
        s = (x - 128) + (y - 128) * 0.9
        if -58 < s < -40:
            c = over(c, (1, 1, 1, 0.55))
        if -34 < s < -28:
            c = over(c, (1, 1, 1, 0.35))
        # visor rim
        if v > 0.86:
            c = over(c, (0.95, 0.97, 1.0, 0.8))

    # green "FPS up" chevrons, bottom right
    for i, oy in enumerate((214, 190)):
        cx = 196
        dx = abs(x - cx)
        top = oy - 18 + dx * 0.9  # chevron upper edge
        if dx < 30 and top <= y <= top + 11:
            c = over(c, (0.35, 0.95, 0.45, 1 if i == 1 else 0.8))
    return c


def render():
    rows = []
    for py in range(H):
        row = bytearray([0])  # filter type 0
        for px in range(W):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(SS):
                for sx in range(SS):
                    r, g, b, a = sample(px + (sx + 0.5) / SS, py + (sy + 0.5) / SS)
                    acc[0] += r * a
                    acc[1] += g * a
                    acc[2] += b * a
                    acc[3] += a
            n = SS * SS
            a = acc[3] / n
            if a > 0:
                rgb = [acc[i] / acc[3] for i in range(3)]
            else:
                rgb = [0, 0, 0]
            row += bytes(int(round(clamp(v) * 255)) for v in rgb) + bytes([int(round(a * 255))])
        rows.append(bytes(row))
    return b"".join(rows)


def png(path, raw):
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", ihdr))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


if __name__ == "__main__":
    png(sys.argv[1], render())
