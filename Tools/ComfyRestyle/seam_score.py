"""
How well do sliced sprites reproduce what ComfyUI painted in context?

    python seam_score.py <compositeDir> <restyled.png> <reassembled.png>

Mean absolute RGB difference between the restyled image and the reassembled image, over
pixels owned by flat sprites (sloped land is excluded, it is not sliced yet). Lower = fewer
seams/repetition artifacts; 0 means the per-graphic sprites tile exactly as painted.
"""
import json
import struct
import sys

from PIL import Image, ImageChops, ImageStat


def main(comp_dir, restyled_path, reassembled_path):
    with open(f"{comp_dir}/items.json") as f:
        items = json.load(f)["items"]
    with open(f"{comp_dir}/ownership.bin", "rb") as f:
        w, h = struct.unpack("<ii", f.read(8))
        owner = struct.unpack(f"<{w * h}i", f.read(4 * w * h))

    mask = Image.new("L", (w, h), 0)
    flat = [i for i, it in enumerate(items) if not it["stretched"]]
    flat_set = set(flat)
    mask.putdata([255 if o in flat_set else 0 for o in owner])

    restyled = Image.open(restyled_path).convert("RGB")
    reassembled = Image.open(reassembled_path).convert("RGB")
    scale = round(restyled.width / w)
    mask = mask.resize((w * scale, h * scale), Image.NEAREST)

    # The 2x render's frame can sit a pixel or two off the 2x-upscaled 1x frame (sloped-land padding);
    # score the best alignment.
    best = None
    for dy in range(-2, 3):
        for dx in range(-2, 3):
            box = (max(0, dx), max(0, dy), min(restyled.width, reassembled.width + dx), min(restyled.height, reassembled.height + dy))
            a = restyled.crop(box)
            b = reassembled.crop((box[0] - dx, box[1] - dy, box[2] - dx, box[3] - dy))
            m = mask.crop(box)
            score = sum(ImageStat.Stat(ImageChops.difference(a, b), m).mean) / 3
            if best is None or score < best[0]:
                best = (score, dx, dy)
    print(f"{best[0]:.1f}  (mean |restyled - reassembled| per channel, flat sprites only; frame shift {best[1]},{best[2]})")


if __name__ == "__main__":
    main(*sys.argv[1:4])
