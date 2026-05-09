#!/usr/bin/env python3
"""
Regenerate the panel favicons (PNG + ICO + SVG) from the canonical logo
PNG so they all stay in visual lockstep.

Source:      frontend/public/kitsune-command-logo-transparent.png
Outputs:     frontend/public/favicon.svg          (96px embedded raster)
             frontend/public/favicon-32x32.png    (LANCZOS downsample)
             frontend/public/favicon-16x16.png    (LANCZOS downsample)
             frontend/public/favicon.ico          (multi-frame: 16/32/48)

Run from repo root:
    python tools/regen-favicons.py

Why an embedded raster in the SVG: the logo is detailed line-art with
circuit traces; redrawing it cleanly as a vector path would diverge from
the source logo over time. Pinning the SVG to a 96px raster of the same
PNG keeps every favicon variant identical to the logo source.

Requires Pillow (pip install pillow).
"""

import base64
import io
import os
from pathlib import Path
from PIL import Image

REPO_ROOT = Path(__file__).resolve().parent.parent
SRC = REPO_ROOT / "frontend" / "public" / "kitsune-command-logo-transparent.png"
OUT_DIR = REPO_ROOT / "frontend" / "public"


def main() -> None:
    if not SRC.exists():
        raise SystemExit(f"Source logo not found: {SRC}")
    src = Image.open(SRC).convert("RGBA")

    # PNG variants
    img32 = src.resize((32, 32), Image.LANCZOS)
    img32.save(OUT_DIR / "favicon-32x32.png", optimize=True)
    print("  wrote favicon-32x32.png")

    img16 = src.resize((16, 16), Image.LANCZOS)
    img16.save(OUT_DIR / "favicon-16x16.png", optimize=True)
    print("  wrote favicon-16x16.png")

    # Multi-frame ICO for legacy browsers
    img48 = src.resize((48, 48), Image.LANCZOS)
    img48.save(OUT_DIR / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)])
    print("  wrote favicon.ico (16/32/48)")

    # SVG with embedded 96px base64 PNG. Browsers that prefer SVG render
    # this for high-DPI sharpness; we pin to the same logo source so SVG
    # and rasters always agree.
    small = src.resize((96, 96), Image.LANCZOS)
    buf = io.BytesIO()
    small.save(buf, "PNG", optimize=True)
    data = base64.b64encode(buf.getvalue()).decode("ascii")

    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" '
        'xmlns:xlink="http://www.w3.org/1999/xlink" '
        'viewBox="0 0 96 96" width="96" height="96">\n'
        "  <!--\n"
        "    KitsuneCommand favicon. Embeds a downscaled raster of\n"
        "    kitsune-command-logo-transparent.png so the favicon and the panel logo\n"
        "    are visually identical. Pure-vector wouldn't reproduce the logo's\n"
        "    circuit-trace detail at 16-32px anyway, so we accept the tradeoff and\n"
        "    pin the SVG to the same source of truth as the PNG/ICO variants.\n"
        "\n"
        "    Regenerate via: python tools/regen-favicons.py\n"
        "  -->\n"
        f'  <image x="0" y="0" width="96" height="96" xlink:href="data:image/png;base64,{data}"/>\n'
        "</svg>\n"
    )
    (OUT_DIR / "favicon.svg").write_text(svg, encoding="utf-8")
    print(f"  wrote favicon.svg ({len(svg)} bytes)")


if __name__ == "__main__":
    main()
