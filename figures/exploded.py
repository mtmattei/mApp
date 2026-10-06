#!/usr/bin/env python3
"""Build the README's exploded view of one App Orbit screen.

An exploded axonometric in the grammar of the diagram-design skill
(cathrynlavery/diagram-design, references/type-exploded.md): one object,
its parts pulled apart along one axis, one label column, one focal part,
no arrows. Every coordinate comes from the 2:1 dimetric projection in
vendor/axonometry.py (MIT, copied unchanged from that repository), so the
skill's verifier can recompute each silhouette from what the part declares:

    python3 figures/exploded.py                          # writes figures/orbit-exploded*.html
    python3 <diagram-design>/scripts/verify-exploded.py figures/orbit-exploded.html

The skin is App Orbit's own (css/app-orbit.css tokens, light and dark), so
the figure matches the product it illustrates.
"""

from __future__ import annotations

import math
import sys
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "vendor"))
from axonometry import M, L, Proj, Rect, f, finish, outline, point, prism, solid  # noqa: E402

# ---------------------------------------------------------------- skin (App Orbit tokens)

SANS = "'Segoe UI', system-ui, -apple-system, Roboto, 'Helvetica Neue', sans-serif"
MONO = "ui-monospace, 'Cascadia Mono', Consolas, Menlo, monospace"

SKINS = {
    "light": dict(
        paper="#f5f4ef", ink="#1d2430", muted="#4a5160", soft="#7a8190", accent="#2457d6", base="#ffffff",
        shade=(None, "rgba(29,36,48,0.07)", "rgba(29,36,48,0.15)"),
        focal=("rgba(36,87,214,0.10)", "rgba(36,87,214,0.20)", "rgba(36,87,214,0.32)"),
        tint="rgba(29,36,48,0.08)", well="rgba(29,36,48,0.14)", chip="#4a5160",
        sil="#1d2430", inner="rgba(29,36,48,0.55)", trace="rgba(29,36,48,0.30)",
        lead="rgba(29,36,48,0.40)", lead_acc="rgba(36,87,214,0.60)", inner_acc="rgba(36,87,214,0.70)",
    ),
    "dark": dict(
        paper="#15181e", ink="#e8e9ec", muted="#b6bac4", soft="#848a97", accent="#7aa2ff", base="#1c2028",
        shade=("rgba(232,233,236,0.08)", None, "rgba(0,0,0,0.35)"),
        focal=("rgba(122,162,255,0.18)", None, "rgba(0,0,0,0.35)"),
        tint="rgba(232,233,236,0.08)", well="rgba(232,233,236,0.12)", chip="#848a97",
        sil="#e8e9ec", inner="rgba(232,233,236,0.45)", trace="rgba(232,233,236,0.30)",
        lead="rgba(232,233,236,0.40)", lead_acc="rgba(122,162,255,0.60)", inner_acc="rgba(122,162,255,0.70)",
    ),
}

# ---------------------------------------------------------------- the object

W, D, T = 200, 144, 12  # one screen's footprint and the thickness of each plate


@dataclass
class Part:
    key: str
    name: str
    sub: str
    rect: Rect
    kind: str
    z: float = 0


def parts():
    """Bottom to top: the four plates App Orbit's lenses read a screen as."""
    return [
        Part("routes", "Routes", "navigates-to · entered-via", Rect(0, 0, W, D, 10), "routes"),
        Part("viewmodel", "View model", "exposes · invokes · binds-to", Rect(16, 16, W - 16, D - 16, 8), "viewmodel"),
        Part("states", "States", "has-state · transitions-to", Rect(8, 8, W - 8, D - 8, 8), "states"),
        Part("ui", "UI", "contains · instance-of", Rect(0, 0, W, D, 10), "ui"),
    ]


FOCUS = "viewmodel"


def seg(P, a, b, z):
    pa, pb = P.iso(a[0], a[1], z), P.iso(b[0], b[1], z)
    return f"{M(pa)} {L(pb)}"


def box(P, r, z, fill, stroke, sw=0.8):
    return f'<path d="{outline(P, r, z)}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"/>'


def detail(P, p: Part, z, sk, focal):
    """Marks on the top face. Three levels at most: the plate, insets, micro detail."""
    inner = sk["inner_acc"] if focal else sk["inner"]
    thin = f'fill="none" stroke="{inner}" stroke-width="0.8"'
    out = []
    if p.kind == "routes":
        # a lane: a pad at each end, the screen's own route box in the middle
        for cx in (24, W - 24):
            out.append(box(P, Rect(cx - 6, 66, cx + 6, 78, 6), z, "none", inner))
        out.append(f'<path d="{seg(P, (30, 72), (80, 72), z)} {seg(P, (120, 72), (W - 30, 72), z)}" {thin}/>')
        out.append(box(P, Rect(80, 50, 120, 94, 4), z, sk["tint"], inner))
    elif p.kind == "viewmodel":
        # member rows; the ringed row is the command the button binds to
        rows = ((36, 100), (52, 132), (68, 88), (84, 120), (100, 156))
        out.append(f'<path d="{"".join(seg(P, (32, y), (x1, y), z) for y, x1 in rows)}" {thin}/>')
        out.append(box(P, Rect(26, 61, W - 26, 75, 3), z, "none", sk["accent"] if focal else inner, 1))
    elif p.kind == "states":
        # three thumbnails of the same screen; the middle one is the active state
        for i, x in enumerate((20, 76, 132)):
            out.append(box(P, Rect(x, 24, x + 48, 120, 4), z, sk["tint"] if i == 1 else "none", inner))
            out.append(f'<path d="{seg(P, (x + 8, 36), (x + 28, 36), z)}" {thin}/>')
            out.append(box(P, Rect(x + 8, 102, x + 40, 112, 2), z, sk["well"] if i == 1 else "none", inner))
    elif p.kind == "ui":
        # the screen: a header, two list rows, a field, and the button
        out.append(f'<path d="{seg(P, (16, 22), (96, 22), z)} {seg(P, (40, 42), (150, 42), z)} {seg(P, (40, 66), (130, 66), z)}" {thin}/>')
        out.append(box(P, Rect(16, 34, 32, 50, 3), z, "none", inner))
        out.append(box(P, Rect(16, 58, 32, 74, 3), z, "none", inner))
        out.append(box(P, Rect(16, 86, W - 16, 102, 3), z, "none", inner))
        out.append(box(P, Rect(16, 112, W - 16, 132, 4), z, sk["well"], inner))
    return out


# ---------------------------------------------------------------- layout

LEAD_W, LABEL_W, PITCH = 64, 240, 36


def explode(ps):
    """One part per level. gap = max(0.5 x top-face height, 3 x thickness), on the 4 grid,
    raised in steps of 4 until the labels sit at least PITCH apart."""
    top_h = max((p.rect.x1 - p.rect.x0 + p.rect.y1 - p.rect.y0) / 2 for p in ps)
    gap = math.ceil(max(0.5 * top_h, 3 * T) / 4) * 4
    while True:
        for k, p in enumerate(ps):
            p.z = k * (T + gap)
        P = Proj(0, 0)
        ys = sorted(prism(P, p.rect, p.z, p.z + T)["anchor"][1] for p in ps)
        if all(b - a >= PITCH for a, b in zip(ys, ys[1:])):
            return gap
        gap += 4


def label(sk, p, anchor, col_x, focal):
    ax, ay = anchor
    lead = sk["lead_acc"] if focal else sk["lead"]
    dot = sk["accent"] if focal else sk["ink"]
    sub = sk["accent"] if focal else sk["muted"]
    return (f'<g data-role="label">'
            f'<line data-role="leader" x1="{f(ax + 6)}" y1="{f(ay)}" x2="{f(col_x - 12)}" y2="{f(ay)}" stroke="{lead}" stroke-width="0.8"/>'
            f'<circle cx="{f(ax)}" cy="{f(ay)}" r="2" fill="{dot}"/>'
            f'<text data-role="name" x="{f(col_x)}" y="{f(ay - 1)}" fill="{sk["ink"]}" font-size="16" font-weight="600" font-family="{SANS}">{p.name}</text>'
            f'<text x="{f(col_x)}" y="{f(ay + 15)}" fill="{sub}" font-size="10" font-family="{MONO}" letter-spacing="0.08em">{p.sub}</text></g>')


def build_svg(skin: str):
    sk = SKINS[skin]
    ps = parts()
    gap = explode(ps)
    P0 = Proj(0, 0)
    pts = [pt for p in ps for pt in prism(P0, p.rect, p.z, p.z + T)["poly"]]
    minx, maxx = min(x for x, _ in pts), max(x for x, _ in pts)
    miny, maxy = min(y for _, y in pts), max(y for _, y in pts)
    left = math.floor((1000 - (maxx - minx + LEAD_W + LABEL_W)) / 2 / 4) * 4
    ox, oy = round((left - minx) / 4) * 4, round((40 - miny) / 4) * 4
    P = Proj(ox, oy)
    vh = math.ceil((oy + maxy + 72) / 4) * 4
    col_x = ox + maxx + LEAD_W

    out = [f'<rect width="100%" height="100%" fill="{sk["paper"]}"/>',
           f'<g data-exploded data-origin="{f(ox)} {f(oy)}" data-gap="{f(gap)}">']
    b, t = ps[0], ps[-1]
    # trace lines first, so every plate paints over them
    for th in (135, -45):
        x1, y1 = point(P, b.rect, th, b.z + T)
        x2, y2 = point(P, b.rect, th, t.z)
        out.append(f'<line data-role="trace" x1="{f(x1)}" y1="{f(y1)}" x2="{f(x2)}" y2="{f(y2)}" '
                   f'stroke="{sk["trace"]}" stroke-width="0.8" stroke-dasharray="4,3"/>')
    for i, p in enumerate(ps):
        focal = p.key == FOCUS
        tones = sk["focal"] if focal else sk["shade"]
        stroke = sk["accent"] if focal else sk["sil"]
        inner = sk["inner_acc"] if focal else sk["inner"]
        pr, body = solid(P, p.rect, p.z, p.z + T, sk, tones, stroke, inner)
        body += detail(P, p, p.z + T, sk, focal)
        body += finish(pr, stroke, inner)
        attrs = (f'data-part="{p.key}" data-name="{p.name}" data-rect="{p.rect.attr()}" '
                 f'data-z="{f(p.z)}" data-t="{f(T)}" data-level="{i}"' + (" data-focal" if focal else ""))
        out.append(f"<g {attrs}>" + "".join(body) + label(sk, p, pr["anchor"], col_x, focal) + "</g>")
    out.append("</g>")
    cy = vh - 28
    out.append(f'<text x="{left}" y="{cy}" fill="{sk["muted"]}" font-size="8" font-family="{MONO}" letter-spacing="0.18em">FOCAL PART</text>')
    out.append(f'<text x="{left + 88}" y="{cy}" fill="{sk["muted"]}" font-size="9" font-family="{SANS}" font-style="italic">'
               f'The view model is what the screen binds to. Each plate is one of App Orbit’s lenses.</text>')
    return "\n        ".join(out), vh


# ---------------------------------------------------------------- page

TITLE = "One screen, taken apart"
DESC = ("Exploded view of one app screen as App Orbit reads it: the routes it sits on at the base, "
        "the view model it binds to, its states, and the UI on top, with the view model as the focal part.")

PAGE = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>{title}</title>
  <style>
    *, *::before, *::after {{ box-sizing: border-box; margin: 0; padding: 0; }}
    :root {{
      --paper: {paper}; --ink: {ink}; --muted: {muted};
      --font: {sans};
      --mono: {mono};
    }}
    body {{
      font-family: var(--font); background: var(--paper); color: var(--ink);
      min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 3rem 2rem;
    }}
    .frame {{ max-width: 1200px; width: 100%; }}
    .diagram-container {{ width: 100%; overflow-x: auto; }}
    .eyebrow {{
      font-family: var(--mono); font-size: 0.66rem; font-weight: 500; letter-spacing: 0.18em;
      text-transform: uppercase; color: var(--muted); margin-bottom: 0.5rem;
    }}
    h1 {{ font-size: clamp(1.25rem, 1.6vw + 0.75rem, 1.5rem); font-weight: 500; letter-spacing: -0.01em; line-height: 1.2; margin-bottom: 1.5rem; }}
    svg {{ width: 100%; min-width: 1000px; display: block; }}
    @media print {{ .diagram-container {{ overflow-x: visible; }} svg {{ min-width: 0; }} }}
  </style>
</head>
<body>
  <div class="frame">
    <p class="eyebrow">App Orbit · exploded axonometric</p>
    <h1>{title}</h1>

    <div class="diagram-container">
      <svg viewBox="0 0 1000 {vh}" xmlns="http://www.w3.org/2000/svg" role="img" aria-labelledby="{slug}-title {slug}-desc">
        <title id="{slug}-title">{title}</title>
        <desc id="{slug}-desc">{desc}</desc>
        {body}
      </svg>
    </div>
  </div>
</body>
</html>
"""


def main() -> int:
    for variant, skin in (("", "light"), ("-dark", "dark")):
        slug = f"orbit-exploded{variant}"
        body, vh = build_svg(skin)
        sk = SKINS[skin]
        html = PAGE.format(title=TITLE, desc=DESC, slug=slug, vh=vh, body=body, sans=SANS, mono=MONO,
                           paper=sk["paper"], ink=sk["ink"], muted=sk["muted"])
        path = HERE / f"{slug}.html"
        path.write_text(html, encoding="utf-8")
        print(f"wrote {path.relative_to(HERE.parent)} ({vh}px tall)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
