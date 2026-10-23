#!/usr/bin/env python3
# ============================================================
# make_icon.py —— 生成 Aster 的应用图标（唯一图标源文件）
#
# 参考稿：暗紫圆角方块 + 五瓣粉色花，花瓣带内外两层与左上打光。
# 为什么用脚本而不是直接丢一张 PNG：
#   1) 图标要 16~1024 共 9 档，截图缩放会糊，脚本按每一档单独渲染再降采样，
#      16px 的小图也是从 4 倍画布缩下来的，边缘不会发毛；
#   2) 换色/改比例只动下面 CONST 区，一条命令重出全套（PNG + ICO + ICNS）；
#   3) 与工程里 Controls/Visuals.cs 的 LogoMark 同源（都是 5 瓣椭圆旋转 72°），
#      图标与界面里的标识不会各画各的。
#
# 用法：
#   python3 tools/make_icon.py            # 输出到 Dome/Aster/Assets/
#   python3 tools/make_icon.py --preview  # 额外输出 /tmp/aster-icon-preview.png 供肉眼比对
#
# 依赖：numpy + Pillow（本机跑在 WorkBuddy 的隔离 venv 里）
#   /Users/jiaqili/.workbuddy/binaries/python/envs/default/bin/python tools/make_icon.py
# ============================================================
from __future__ import annotations

import math
import os
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image

# ==================== 可调参数 ====================
# 画布按 1024 设计：所有几何量都用「画布比例」表达，改 size 不用改任何坐标。
MASTER = 1024

# 圆角方块：inset 是四周留白（像素），radius 是圆角半径。
# 22.5% 接近 macOS Big Sur 图标的观感（185/824），Windows 11 也在 20%~25% 之间。
INSET = 0.0
RADIUS = 0.2245

# 背景：左上角偏亮的深紫，向右下迅速沉到近黑。
# 参考稿实测：左上 #1D0E37 / 右侧 #0B0518 / 右下 #060110。
BG_LIGHT = "#271646"
BG_DARK = "#0A0414"
BG_GLOW = (0.22, 0.10)   # 光源中心（画布比例），radial 起点
BG_GLOW_R = 1.18         # 光晕半径（画布比例）

# 花朵：半径占画布 26.2%（直径 52.4%，参考稿实测 121/240 ≈ 50.4%，略放大一点更压得住 16px），
# 中心略微上移，视觉上比几何居中更稳。
FLOWER_R = 0.262
FLOWER_CY = 0.497

# 五瓣的外层椭圆：d=中心距（R 的倍数）、b=沿轴半长、a=垂直轴半宽。
PETAL_D, PETAL_B, PETAL_A = 0.635, 0.360, 0.365
# 内层高光椭圆：更小、更靠中心，是「一瓣里还套着一片浅色花瓣」的关键。
INNER_D, INNER_B, INNER_A = 0.360, 0.255, 0.175
# 收腰系数：越靠近花心，椭圆越窄（1.0=标准椭圆）。
# 参考稿里相邻花瓣之间能看见一直通到花心的细缝，标准椭圆做不到——
# 因为它在中段一样宽、内端就把缝堵死了。0.82 是「瓣身够饱满、缝又留得住」的折中。
PETAL_TAPER, INNER_TAPER = 0.82, 0.92

# 打光方向（单位向量，指向「亮的那一侧」）：左上。参考稿里左侧瓣最亮、右下最暗。
LIGHT = (-0.60, -0.80)
PETAL_DARK = "#8E1C52"   # 背光侧花瓣色
PETAL_LITE = "#E2648F"   # 迎光侧花瓣色
INNER_DARK = "#C2547C"   # 背光侧的内层高光
INNER_LITE = "#F7A7BF"   # 迎光侧的内层高光
# 内层不能写成「外层色 + 固定偏移」：实测暗瓣只提亮十几级、亮瓣要提亮八十几级，
# 所以它自己也是一套「暗→亮」的色阶，跟着同一个 lit 系数走。

# 花心：暗紫圆盘 + 琥珀小点（参考稿实测 #FFCF7E）
CORE_R, CORE_COLOR = 0.115, "#2A0D24"
DOT_R, DOT_COLOR = 0.042, "#FFCF7E"
DOT_DY = 0.018           # 琥珀点比圆心略低一点

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Dome", "Aster", "Assets")
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
PNG_SIZES = [16, 32, 48, 64, 128, 256, 512, 1024]
# ICNS 需要的 10 个槽位：(文件名, 像素) —— iconutil 只认这套命名
ICNS_SLOTS = [
    ("icon_16x16.png", 16), ("icon_16x16@2x.png", 32),
    ("icon_32x32.png", 32), ("icon_32x32@2x.png", 64),
    ("icon_128x128.png", 128), ("icon_128x128@2x.png", 256),
    ("icon_256x256.png", 256), ("icon_256x256@2x.png", 512),
    ("icon_512x512.png", 512), ("icon_512x512@2x.png", 1024),
]


def rgb(hex_color: str) -> np.ndarray:
    h = hex_color.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32) / 255.0


def over(dst_rgb: np.ndarray, dst_a: np.ndarray, src_rgb: np.ndarray, src_a: np.ndarray):
    """标准 source-over 合成；rgb 是 (H,W,3)，alpha 是 (H,W)。"""
    sa = src_a[..., None]
    out_a = src_a + dst_a * (1.0 - src_a)
    # 避免除零：alpha 为 0 的像素颜色本来就无意义
    out_rgb = src_rgb * sa + dst_rgb * dst_a[..., None] * (1.0 - sa)
    nz = out_a > 1e-6
    out_rgb[nz] /= out_a[nz][..., None]
    return out_rgb, out_a


def squircle_alpha(n: int):
    """圆角方块的覆盖率（0~1）。用有符号距离场算，斜边处天然抗锯齿。"""
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    half = (n - 2.0 * INSET) / 2.0
    r = RADIUS * n
    cx = cy = n / 2.0
    # 把坐标折到第一象限，圆角方块四角等价
    qx = np.abs(x + 0.5 - cx) - (half - r)
    qy = np.abs(y + 0.5 - cy) - (half - r)
    d = np.hypot(np.maximum(qx, 0.0), np.maximum(qy, 0.0)) + np.minimum(np.maximum(qx, qy), 0.0) - r
    return np.clip(0.5 - d, 0.0, 1.0)


def ellipse_alpha(n: int, u: np.ndarray, v: np.ndarray, d: float, b: float, a: float, taper: float = 1.0):
    """旋转坐标系里的椭圆覆盖率。u 沿花瓣轴、v 垂直轴向，都已按 R 归一化。

    taper < 1 时，半宽 a 会随 u 向内收窄（内置端最窄、外端最宽），
    得到参考稿那种「瓣尖圆、瓣根细」的水滴形，而不是一颗颗正圆。
    """
    # t：0 = 花瓣最内端，1 = 花瓣最外端
    t = np.clip(((u - (d - b)) / (2.0 * b)), 0.0, 1.0)
    width = a * (taper + (1.0 - taper) * t)
    f = ((u - d) / b) ** 2 + (v / width) ** 2
    # 0.06 的软边宽度 ≈ 画布上的 1.5px，配合 4 倍超采样足够干净
    return np.clip((1.0 - f) / 0.06, 0.0, 1.0)


def render(size: int) -> Image.Image:
    """按目标像素尺寸渲染一张 RGBA 图标（内部 4 倍超采样后降采样）。"""
    ss = 4 if size <= 256 else 2
    n = size * ss

    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    xn = (x + 0.5) / n
    yn = (y + 0.5) / n

    # ---------- 第 1 层：背景圆角方块 ----------
    a_bg = squircle_alpha(n)
    # 径向渐变：t 是到光源中心的归一化距离，开方让亮区收得快一点
    dist = np.hypot((xn - BG_GLOW[0]) * 1.35, yn - BG_GLOW[1])
    t = np.clip(dist / BG_GLOW_R, 0.0, 1.0) ** 0.95
    bg = rgb(BG_LIGHT)[None, None, :] * (1 - t[..., None]) + rgb(BG_DARK)[None, None, :] * t[..., None]
    canvas = bg * a_bg[..., None]
    canvas_a = a_bg.copy()

    # ---------- 第 2 层：五瓣花 ----------
    R = FLOWER_R * n
    cx, cy = n / 2.0, FLOWER_CY * n
    dx = (x + 0.5 - cx) / R
    dy = (y + 0.5 - cy) / R

    dark, lite = rgb(PETAL_DARK), rgb(PETAL_LITE)
    inner_dark, inner_lite = rgb(INNER_DARK), rgb(INNER_LITE)
    core_c, dot_c = rgb(CORE_COLOR), rgb(DOT_COLOR)

    for i in range(5):
        ang = math.radians(i * 72.0 + 270.0)   # 起始角 270° = 正上方，与 LogoMark 一致
        cos, sin = math.cos(ang), math.sin(ang)
        u = dx * cos + dy * sin               # 沿花瓣轴
        v = -dx * sin + dy * cos              # 垂直花瓣轴

        # 每瓣的明度由「花瓣轴 · 光方向」决定：迎着光的瓣亮，背光的瓣暗
        lit = (cos * LIGHT[0] + sin * LIGHT[1] + 1.0) / 2.0
        base = dark * (1 - lit) + lite * lit

        # 外层：沿轴由内向外微微提亮，模拟花瓣边缘的受光
        a_out = ellipse_alpha(n, u, v, PETAL_D, PETAL_B, PETAL_A, PETAL_TAPER)
        shade = 0.90 + 0.20 * np.clip(u / (PETAL_D + PETAL_B), 0.0, 1.0)
        canvas, canvas_a = over(canvas, canvas_a, base[None, None, :] * shade[..., None], a_out)

        # 内层：换一套更浅的色阶，再沿轴向外加亮，形成「瓣中瓣」
        a_in = ellipse_alpha(n, u, v, INNER_D, INNER_B, INNER_A, INNER_TAPER)
        inner = inner_dark * (1 - lit) + inner_lite * lit
        glow = 0.97 + 0.08 * np.clip((u - INNER_D) / INNER_B, -1.0, 1.0)
        canvas, canvas_a = over(canvas, canvas_a, np.clip(inner[None, None, :] * glow[..., None], 0, 1), a_in)

    # ---------- 第 3 层：花心 ----------
    dcx, dcy = (x + 0.5 - cx) / R, (y + 0.5 - cy) / R
    rr = np.hypot(dcx, dcy)
    a_core = np.clip((CORE_R - rr) / 0.03, 0.0, 1.0)
    canvas, canvas_a = over(canvas, canvas_a, np.broadcast_to(core_c, (*rr.shape, 3)), a_core)

    a_dot = np.clip((DOT_R - np.hypot(dcx, dcy - DOT_DY)) / 0.03, 0.0, 1.0)
    canvas, canvas_a = over(canvas, canvas_a, np.broadcast_to(dot_c, (*rr.shape, 3)), a_dot)

    # ---------- 输出 ----------
    out = np.clip(canvas, 0, 1)
    rgba = np.dstack([out, canvas_a]) * 255.0
    img = Image.fromarray(np.round(rgba).astype(np.uint8), "RGBA")
    return img.resize((size, size), Image.LANCZOS)


def main() -> int:
    out_dir = os.path.abspath(OUT_DIR)
    os.makedirs(out_dir, exist_ok=True)

    master = render(MASTER)
    master.save(os.path.join(out_dir, "aster-1024.png"))

    # 每个尺寸单独渲染：小图也走 4 倍超采样，避免 16px 糊成一团
    rendered: dict[int, Image.Image] = {}
    for s in sorted(set(PNG_SIZES + ICO_SIZES)):
        rendered[s] = master if s == MASTER else render(s)

    rendered[512].save(os.path.join(out_dir, "aster-512.png"))
    rendered[256].save(os.path.join(out_dir, "aster-256.png"))
    rendered[128].save(os.path.join(out_dir, "aster-128.png"))

    # ICO：Pillow 支持多帧写入，Windows 会按 DPI 自取最合适的一档
    rendered[256].save(
        os.path.join(out_dir, "aster.ico"),
        format="ICO",
        sizes=[(s, s) for s in ICO_SIZES],
    )

    # ICNS：macOS 只认 iconutil 生成的包，先铺 iconset 再转换
    iconset = os.path.join(out_dir, "aster.iconset")
    shutil.rmtree(iconset, ignore_errors=True)
    os.makedirs(iconset)
    for name, px in ICNS_SLOTS:
        rendered[px].save(os.path.join(iconset, name))
    subprocess.run(["iconutil", "-c", "icns", iconset, "-o", os.path.join(out_dir, "aster.icns")], check=True)
    shutil.rmtree(iconset, ignore_errors=True)

    if "--preview" in sys.argv:
        preview = Image.new("RGBA", (1080, 300), (10, 6, 20, 255))
        x = 20
        for s in (256, 128, 64, 48, 32, 24, 16):
            preview.alpha_composite(rendered[s], (x, 20))
            x += s + 24
        preview.save("/tmp/aster-icon-preview.png")

    for f in ("aster-1024.png", "aster-512.png", "aster-256.png", "aster-128.png", "aster.ico", "aster.icns"):
        p = os.path.join(out_dir, f)
        print(f"{f:16s} {os.path.getsize(p):>8,} B")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
