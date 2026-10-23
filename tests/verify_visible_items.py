"""Check full cover/title pixels against independent upper-viewport screenshots."""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument("--expected-cards", type=int, required=True)
parser.add_argument("--capture", default="live-space-after.png")
args = parser.parse_args()
out = Path(__file__).resolve().parents[1] / "artifacts"
folder = out / "live-card-reference"
items = sorted(json.loads((folder / "items.json").read_text(encoding="utf-8-sig")), key=lambda i: i["order"])
assert len(items) == args.expected_cards * 2
assert sum(i["kind"] == "cover" for i in items) == args.expected_cards
Image.MAX_IMAGE_PIXELS = 120_000_000
capture = np.asarray(Image.open(out / args.capture).convert("RGB"))
results = []
for item in items:
    reference = np.asarray(Image.open(folder / item["file"]).convert("RGB"))
    height, width = reference.shape[:2]
    best = None
    # Preserve the expected document position; don't match a duplicate cover elsewhere.
    for top in range(max(0, round(item["y"]) - 16), min(capture.shape[0] - height, round(item["y"]) + 16) + 1):
        x = item["x"]
        patch = capture[top:top + height, x:x + width]
        errors = int(np.count_nonzero(np.any(patch != reference, axis=2)))
        if best is None or errors < best[0]: best = (errors, top)
    assert best is not None and best[0] == 0, f"Item {item['order']} {item['kind']} missing/obscured: {best}"
    results.append({"order": item["order"], "kind": item["kind"], "y": best[1], "expected_y": item["y"], "rgb_differences": 0})
    print(f"PASS: item {item['order']} {item['kind']} complete at y={best[1]}; RGB=0")
(out / "live-card-content-check.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(f"PASS: all {args.expected_cards} complete covers and titles retain exact pixels")
