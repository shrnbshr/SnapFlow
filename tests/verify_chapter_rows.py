"""Compare a live chapter capture with independent visible-row screenshots.

Requires Pillow and numpy. Generate private references with the desktop smoke
tool's --live-reference mode; no login URL or course text is embedded here.
"""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument("--expected", type=int, required=True)
parser.add_argument("--capture", default="live-chapters-after.png")
args = parser.parse_args()
out = Path(__file__).resolve().parents[1] / "artifacts"
folder = out / "live-chapter-reference"
rows = sorted(json.loads((folder / "rows.json").read_text(encoding="utf-8-sig")), key=lambda row: row["order"])
assert len(rows) == args.expected
Image.MAX_IMAGE_PIXELS = 120_000_000
capture = np.asarray(Image.open(out / args.capture).convert("RGB"))
dark = capture.mean(axis=2) < 130
minimum_y = 0
results = []
for row in rows:
    template = np.asarray(Image.open(folder / row["file"]).convert("RGB"))
    mask = template.mean(axis=2) < 130
    height, width = mask.shape
    assert mask.sum() > 100, f"Row {row['id']} has no text"
    anchor = int(np.argmax((mask[:, 1:] != mask[:, :-1]).sum(axis=1)))
    best = None
    for dx in range(-2, 3):
        x = row["x"] + dx
        errors = np.count_nonzero(dark[:, x:x + width:4] != mask[anchor, ::4], axis=1)
        for at in np.flatnonzero(errors <= 2):
            top = int(at) - anchor
            if top < minimum_y or top + height > capture.shape[0]:
                continue
            patch = capture[top:top + height, x:x + width]
            text_diff = int(np.count_nonzero((patch.mean(axis=2) < 130) != mask))
            rgb_diff = int(np.count_nonzero(np.any(patch != template, axis=2)))
            score = (text_diff, rgb_diff)
            if best is None or score < best[0]:
                best = (score, top, dx)
    assert best is not None, f"Row {row['id']} missing or out of order"
    score, top, dx = best
    assert score[0] == 0, f"Row {row['id']} text differs: {score[0]}"
    # The site has a fixed back-to-top button whose shadow can cross the blank
    # far-right row margin. Check all text silhouettes across the entire row,
    # then exact RGB over the complete text extent with surrounding padding.
    yy, xx = np.where(mask)
    left, right = max(0, int(xx.min()) - 8), min(width, int(xx.max()) + 9)
    upper, lower = max(0, int(yy.min()) - 8), min(height, int(yy.max()) + 9)
    patch = capture[top:top + height, row["x"] + dx:row["x"] + dx + width]
    text_rgb_diff = int(np.count_nonzero(np.any(patch[upper:lower, left:right] != template[upper:lower, left:right], axis=2)))
    assert text_rgb_diff == 0, f"Row {row['id']} text RGB differs: {text_rgb_diff}"
    minimum_y = top + height
    results.append({"id": row["id"], "y": top, "dx": dx, "text_differences": score[0],
                    "text_rgb_differences": text_rgb_diff, "whole_row_rgb_differences": score[1]})
    print(f"PASS: row {row['id']} complete, in order; text=0, text RGB=0; whole row RGB={score[1]}")
(out / "live-chapter-content-check.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(f"PASS: all {len(results)} chapter texts match independent visible screenshots exactly")
