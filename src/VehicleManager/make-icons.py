#!/usr/bin/env python3
"""Makes Vehicle Manager's icons in wwwroot from one design: Bootstrap Icons' car-front-fill (MIT, as in the navbar),
white on the navbar's dark grey. Needs rsvg-convert (brew install librsvg). Run from the repo root:

    python3 src/VehicleManager/make-icons.py

It writes wwwroot/favicon.svg and favicon.ico (16, 32, 48 px), and wwwroot/icons/: apple-touch-icon.png (180 px,
full-bleed: iOS rounds the corners), icon-192.png and icon-512.png (rounded, the manifest's "any"), and
icon-maskable-512.png (full-bleed, the car within the centre 80% that Android's masks keep).
"""
import os, struct, subprocess, tempfile

W = os.path.join(os.path.dirname(os.path.abspath(__file__)), "GreenHedges.HomeServer.VehicleManager", "wwwroot")
S = tempfile.mkdtemp()
os.makedirs(os.path.join(W, "icons"), exist_ok=True)
CAR = ("M2.52 3.515A2.5 2.5 0 0 1 4.82 2h6.362c1 0 1.904.596 2.298 1.515l.792 1.848c.075.175.21.319.38.404.5.25.855.715.965 1.262l.335 1.679q.05.242.049.49v.413c0 .814-.39 1.543-1 1.997V13.5a.5.5 0 0 1-.5.5h-2a.5.5 0 0 1-.5-.5v-1.338c-1.292.048-2.745.088-4 .088s-2.708-.04-4-.088V13.5a.5.5 0 0 1-.5.5h-2a.5.5 0 0 1-.5-.5v-1.892c-.61-.454-1-1.183-1-1.997v-.413a2.5 2.5 0 0 1 .049-.49l.335-1.68c.11-.546.465-1.012.964-1.261a.8.8 0 0 0 .381-.404l.792-1.848ZM3 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2m10 0a1 1 0 1 0 0-2 1 1 0 0 0 0 2M6 8a1 1 0 0 0 0 2h4a1 1 0 1 0 0-2zM2.906 5.189a.51.51 0 0 0 .497.731c.91-.073 3.35-.17 4.597-.17s3.688.097 4.597.17a.51.51 0 0 0 .497-.731l-.956-1.913A.5.5 0 0 0 11.691 3H4.309a.5.5 0 0 0-.447.276L2.906 5.19Z")
BG = "#212529"   # Bootstrap's dark grey: the navbar
NOTE = "<!-- Vehicle Manager's icon: Bootstrap Icons car-front-fill (MIT, (c) The Bootstrap Authors), as in the navbar. -->"

def svg(rounded, car_width):
    # A 512 canvas; the car (16 x 12 units, centred on 8,8) scaled to car_width.
    k = car_width / 16
    bg = f'<rect width="512" height="512" rx="{96 if rounded else 0}" fill="{BG}"/>'
    car = f'<path fill="#fff" transform="translate(256 256) scale({k:.4f}) translate(-8 -8)" d="{CAR}"/>'
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">{NOTE}{bg}{car}</svg>\n'

def png(source, size, out):
    subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), "-o", out, source], check=True)

variants = {
    "rounded": svg(True, 384),    # tabs, manifest "any"
    "square": svg(False, 352),    # apple-touch-icon: iOS rounds the corners itself
    "maskable": svg(False, 288),  # Android's masks keep the centre 80%: the car stays inside it
}
for name, text in variants.items():
    open(os.path.join(S, f"{name}.svg"), "w").write(text)

open(os.path.join(W, "favicon.svg"), "w").write(variants["rounded"])
png(os.path.join(S, "square.svg"), 180, os.path.join(W, "icons", "apple-touch-icon.png"))
png(os.path.join(S, "rounded.svg"), 192, os.path.join(W, "icons", "icon-192.png"))
png(os.path.join(S, "rounded.svg"), 512, os.path.join(W, "icons", "icon-512.png"))
png(os.path.join(S, "maskable.svg"), 512, os.path.join(W, "icons", "icon-maskable-512.png"))

# favicon.ico: 16, 32 and 48 px PNGs inside an ICO container (supported by every browser that reads ICO).
images = []
for size in (16, 32, 48):
    out = os.path.join(S, f"fav-{size}.png")
    png(os.path.join(S, "rounded.svg"), size, out)
    images.append((size, open(out, "rb").read()))
header = struct.pack("<HHH", 0, 1, len(images))
offset = 6 + 16 * len(images)
entries, data = b"", b""
for size, blob in images:
    entries += struct.pack("<BBBBHHII", size, size, 0, 0, 1, 32, len(blob), offset + len(data))
    data += blob
open(os.path.join(W, "favicon.ico"), "wb").write(header + entries + data)
print("done")
