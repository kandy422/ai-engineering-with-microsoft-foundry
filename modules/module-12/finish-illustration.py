from pathlib import Path
import argparse
import hashlib
import json
import numpy as np
from PIL import Image

root = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument("--replay", type=Path, help="Write a separate learner reproduction without changing archived assets.")
args = parser.parse_args()
base = root/"public"/"generated-kettle-v1.png"
if args.replay:
    args.replay.mkdir(parents=True, exist_ok=False)
    output = args.replay/"generated-kettle-v3.png"
else:
    output = root/"public"/"generated-kettle-v3.png"
source = np.asarray(Image.open(base).convert("RGB"),dtype=np.uint8)
hsv = np.asarray(Image.fromarray(source).convert("HSV"),dtype=np.uint8).copy()
hue, saturation, value = hsv.transpose(2,0,1)
# The visible cyan body is the only saturated cyan region in this specific asset.
mask = (hue >= 98) & (hue <= 140) & (saturation >= 45) & (value >= 50)
assert 1_000 < np.sum(mask) < source.shape[0]*source.shape[1]//2
hue[mask] = 27
saturation[mask] = np.maximum(saturation[mask],110)
candidate = np.asarray(Image.fromarray(hsv,"HSV").convert("RGB"))
result = np.where(mask[:,:,None],candidate,source)
assert not np.any(result[~mask] != source[~mask])
assert np.any(result[mask] != source[mask])
Image.fromarray(result).save(output)
provenance = root/"qa"/"12.6-provenance.json"
data = json.loads(provenance.read_text(encoding="utf-8-sig"))
if args.replay:
    expected = next(asset for asset in data["assets"] if asset["version"] == 3)
    changed = int(np.count_nonzero(np.any(result != source, axis=2)))
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    assert changed == expected["changedPixels"] and digest == expected["sha256"]
    print(json.dumps({"changedPixels": changed, "pixelsChangedOutsideMask": 0, "sha256": digest}, indent=2))
    raise SystemExit(0)
data["assets"][1]["review"]="Rejected: the requested amber handle did not appear and the lid changed."
data["assets"].append({
    "asset":str(output.relative_to(root)).replace("\\","/"),"version":3,
    "parent":str(base.relative_to(root)).replace("\\","/"),
    "method":"Pillow/NumPy HSV hue edit, limited to selected cyan pixels",
    "instruction":"Change the visible cyan body to amber without modifying other pixels.",
    "review":"Verified single-region colour edit. Retained as labelled draft artwork only; the generated kettle shape needs review.",
    "changedPixels":int(np.count_nonzero(np.any(result != source,axis=2))),
    "pixelsChangedOutsideMask":0,"sha256":hashlib.sha256(output.read_bytes()).hexdigest()
})
provenance.write_text(json.dumps(data,indent=2),encoding="utf-8")
