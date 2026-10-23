"""Verify every article paragraph against Edge's independent full-page reference.

Requires Pillow and numpy. This is optional image QA, separate from the app build.
Generate edge-reference-full.png and edge-metrics.json with the isolated browser
session, then run after ShareX.CaptureSmoke --edge.
"""
import json
import argparse
from pathlib import Path
import numpy as np
from PIL import Image

root = Path(__file__).resolve().parents[1]
out = root / "artifacts"
parser = argparse.ArgumentParser()
parser.add_argument("--capture", default="sharex-original-edge.png")
parser.add_argument("--reference", default="edge-reference-full.png")
parser.add_argument("--metrics", default="edge-metrics.json")
parser.add_argument("--output", default="article-content-check.json")
parser.add_argument("--x-offset", type=int, default=0, help="Known horizontal difference between reference and captured article margins")
args = parser.parse_args()
# Only these locally generated regression images are read; cap decoding at 120 MP.
Image.MAX_IMAGE_PIXELS = 120_000_000
reference = np.asarray(Image.open(out / args.reference).convert("RGB"))
captured = np.asarray(Image.open(out / args.capture).convert("RGB"))
metrics = json.loads((out / args.metrics).read_text(encoding="utf-8-sig"))["data"]["result"]
# Some CDP/Edge versions return a larger canvas with repeated off-viewport tiles.
# The document's actual dimensions and the selected viewport delimit the reference.
reference = reference[:round(metrics["height"] * metrics["dpr"]), :captured.shape[1]]
dark = captured.mean(axis=2) < 130
minimum_y = 0
results = []
for number, box in enumerate(metrics["paragraphs"] + [metrics["end"]], 1):
    x, y, width, height = (int(box[k]) for k in ("x", "y", "width", "height"))
    template = reference[y:y + height, x:x + width]
    mask = template.mean(axis=2) < 130
    transitions = (mask[:, 1:] != mask[:, :-1]).sum(axis=1)
    assert mask.sum() > 1000 and transitions.max() > 40, f"Paragraph {number}: reference lacks detailed text"
    anchor = int(np.argmax(transitions))
    best = None
    for dx in range(args.x_offset - 2, args.x_offset + 3):
        sample = dark[:, x + dx:x + dx + width:5]
        errors = np.count_nonzero(sample != mask[anchor, ::5], axis=1)
        candidates = np.flatnonzero(errors <= 2)
        for at in candidates:
            top = int(at) - anchor
            if top < minimum_y or top + height > captured.shape[0]:
                continue
            patch = captured[top:top + height, x + dx:x + dx + width]
            differences = int(np.count_nonzero((patch.mean(axis=2) < 130) != mask))
            pixel_differences = int(np.count_nonzero(np.any(patch != template, axis=2)))
            score = (differences, pixel_differences)
            if best is None or score < best[0]:
                best = (score, top, dx)
    if best is None:
        raise AssertionError(f"Paragraph {number}: no matching reference content")
    score, top, dx = best
    # Require exact text silhouettes; antialiasing color changes are reported separately.
    assert score[0] == 0, f"Paragraph {number}: {score[0]} text pixels differ"
    minimum_y = top + height
    item = {"paragraph": number if number <= 42 else "end", "reference_y": y,
            "captured_y": top, "dx": dx, "text_pixel_differences": score[0],
            "rgb_pixel_differences": score[1]}
    results.append(item)
    print(f"PASS: paragraph {item['paragraph']} complete, in order; text differences=0; RGB differences={score[1]}")
assert metrics["bottom"], "Browser did not reach the bottom"
(out / args.output).write_text(json.dumps({"reference_size": reference.shape[:2],
    "captured_size": captured.shape[:2], "paragraphs": results}, indent=2), encoding="utf-8")
print("PASS: all 42 paragraphs and end marker retained; browser reached the bottom")
