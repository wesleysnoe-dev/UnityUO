"""
Restyle a UO composite with a local ComfyUI (SDXL img2img + ControlNet Union tile + IP-Adapter style transfer).

    E:\\ComfyUI\\python_embeded\\python.exe Tools\\ComfyRestyle\\restyle.py Exports\\comfy_test\\block

reads   <dir>/composite.png
writes  <dir>/restyled.png       exactly composite size x --scale, ready for `uoclient slice`
        <dir>/restyle_graph.json the API graph that produced it (for reproducing / loading in ComfyUI)

Pipeline: composite -> 4x-UltraSharp -> down to --scale (so SDXL starts from clean detail, not pixel
blocks) -> img2img at --denoise. ControlNet Union in "tile" mode, fed the same upscaled image, pins
geometry so every edge stays on UO's tile grid. IP-Adapter "style transfer" carries the look of the
style image (default: the mockup's town scene) without copying its content.

Needs only the Python stdlib + Pillow (both in ComfyUI's python_embeded).
"""

import argparse
import io
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_STYLE = os.path.join(HERE, "style", "mockup_scene.png")

POSITIVE = (
    "hand-painted isometric fantasy medieval town, detailed painterly game art, "
    "timber-framed plaster houses, slate roof tiles, worn cobblestone street, weathered wood, "
    "warm afternoon light, soft ambient occlusion, rich texture, crisp edges, classic isometric RPG"
)
NEGATIVE = (
    "text, letters, watermark, signature, people, characters, figures, blurry, soft focus, "
    "perspective distortion, fisheye, photo, photorealistic, 3d render, plastic, lowres, jpeg artifacts, "
    "pixelated, noise, oversaturated, deformed, extra objects"
)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("dir", help="composite folder from `uoclient compose`")
    ap.add_argument("--server", default="http://127.0.0.1:8188")
    ap.add_argument("--style", default=DEFAULT_STYLE, help="style reference image for IP-Adapter")
    ap.add_argument("--scale", type=int, default=2)
    ap.add_argument("--checkpoint", default="dreamshaperXL_alpha2Xl10.safetensors")
    ap.add_argument("--denoise", type=float, default=0.55)
    ap.add_argument("--steps", type=int, default=30)
    ap.add_argument("--cfg", type=float, default=5.5)
    ap.add_argument("--seed", type=int, default=1234)
    ap.add_argument("--cn-strength", type=float, default=0.6)
    ap.add_argument("--cn-end", type=float, default=0.9)
    ap.add_argument("--ipa-weight", type=float, default=0.8, help="0 disables IP-Adapter")
    ap.add_argument("--positive", default=POSITIVE)
    ap.add_argument("--negative", default=NEGATIVE)
    ap.add_argument("--background", default="181818", help="hex color behind the composite's transparent area")
    ap.add_argument("--output", default="restyled.png", help="file name inside <dir>")
    args = ap.parse_args()

    src_path = os.path.join(args.dir, "composite.png")
    composite = Image.open(src_path).convert("RGBA")
    w, h = composite.size

    # Flatten, and pad right/bottom so the scaled size is a multiple of 8 (SDXL's VAE would crop otherwise).
    step = max(1, 8 // args.scale) if 8 % args.scale == 0 else 8
    pw, ph = -(-w // step) * step, -(-h // step) * step
    bg = tuple(int(args.background[i:i + 2], 16) for i in (0, 2, 4))
    flat = Image.new("RGB", (pw, ph), bg)
    flat.paste(composite, (0, 0), composite)

    tag = uuid.uuid4().hex[:8]
    input_name = upload(args.server, flat, f"uo_restyle_{tag}.png")
    style_name = upload(args.server, Image.open(args.style).convert("RGB"), f"uo_style_{tag}.png")

    graph = build_graph(args, input_name, style_name)
    with open(os.path.join(args.dir, "restyle_graph.json"), "w") as f:
        json.dump(graph, f, indent=2)

    print(f"Queued {w}x{h} -> {pw * args.scale}x{ph * args.scale} (denoise {args.denoise}, seed {args.seed})")
    started = time.time()
    result = run(args.server, graph)
    print(f"Done in {time.time() - started:.1f}s")

    out = result.convert("RGB").crop((0, 0, w * args.scale, h * args.scale))
    out_path = os.path.join(args.dir, args.output)
    out.save(out_path)
    print(f"Wrote {out_path} ({out.size[0]}x{out.size[1]})")


def build_graph(a, input_name, style_name):
    g = {
        "ckpt": node("CheckpointLoaderSimple", ckpt_name=a.checkpoint),
        "src": node("LoadImage", image=input_name),
        "upmodel": node("UpscaleModelLoader", model_name="4x-UltraSharp.safetensors"),
        "up4": node("ImageUpscaleWithModel", upscale_model=ref("upmodel"), image=ref("src")),
        "upN": node("ImageScaleBy", image=ref("up4"), upscale_method="lanczos", scale_by=a.scale / 4.0),
        "latent": node("VAEEncode", pixels=ref("upN"), vae=ref("ckpt", 2)),
        "pos": node("CLIPTextEncode", text=a.positive, clip=ref("ckpt", 1)),
        "neg": node("CLIPTextEncode", text=a.negative, clip=ref("ckpt", 1)),
        "cn": node("ControlNetLoader", control_net_name="diffusion_pytorch_model_promax.safetensors"),
        "cn_tile": node("SetUnionControlNetType", control_net=ref("cn"), type="tile"),
        "cn_apply": node("ControlNetApplyAdvanced", positive=ref("pos"), negative=ref("neg"),
                         control_net=ref("cn_tile"), image=ref("upN"),
                         strength=a.cn_strength, start_percent=0.0, end_percent=a.cn_end),
        "decode": node("VAEDecode", samples=ref("sample"), vae=ref("ckpt", 2)),
        "save": node("SaveImage", images=ref("decode"), filename_prefix="uo_restyle/restyle"),
    }

    model = ref("ckpt")
    if a.ipa_weight > 0:
        g["style"] = node("LoadImage", image=style_name)
        g["ipa_loader"] = node("IPAdapterUnifiedLoader", model=ref("ckpt"), preset="PLUS (high strength)")
        g["ipa"] = node("IPAdapterAdvanced", model=ref("ipa_loader"), ipadapter=ref("ipa_loader", 1), image=ref("style"),
                        weight=a.ipa_weight, weight_type="style transfer", combine_embeds="concat",
                        start_at=0.0, end_at=1.0, embeds_scaling="V only")
        model = ref("ipa")

    g["sample"] = node("KSampler", model=model, seed=a.seed, steps=a.steps, cfg=a.cfg,
                       sampler_name="dpmpp_2m", scheduler="karras",
                       positive=ref("cn_apply"), negative=ref("cn_apply", 1),
                       latent_image=ref("latent"), denoise=a.denoise)
    return g


def node(class_type, **inputs):
    return {"class_type": class_type, "inputs": inputs}


def ref(name, output=0):
    return [name, output]


def upload(server, image, name):
    buf = io.BytesIO()
    image.save(buf, format="PNG")
    boundary = uuid.uuid4().hex
    body = (
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"{name}\"\r\n"
        f"Content-Type: image/png\r\n\r\n"
    ).encode() + buf.getvalue() + (
        f"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"overwrite\"\r\n\r\ntrue\r\n--{boundary}--\r\n"
    ).encode()
    req = urllib.request.Request(f"{server}/upload/image", data=body,
                                 headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    with urllib.request.urlopen(req) as r:
        return json.load(r)["name"]


def run(server, graph):
    req = urllib.request.Request(f"{server}/prompt", data=json.dumps({"prompt": graph}).encode(),
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req) as r:
            prompt_id = json.load(r)["prompt_id"]
    except urllib.error.HTTPError as e:
        sys.exit(f"ComfyUI rejected the graph:\n{e.read().decode(errors='replace')}")

    while True:
        time.sleep(1.0)
        with urllib.request.urlopen(f"{server}/history/{prompt_id}") as r:
            history = json.load(r)
        if prompt_id not in history:
            continue
        entry = history[prompt_id]
        status = entry.get("status", {})
        if status.get("status_str") == "error":
            sys.exit("ComfyUI reported an error:\n" + json.dumps(status.get("messages", []), indent=2)[:4000])
        images = entry.get("outputs", {}).get("save", {}).get("images", [])
        if images:
            img = images[0]
            query = urllib.parse.urlencode({"filename": img["filename"], "subfolder": img["subfolder"], "type": img["type"]})
            with urllib.request.urlopen(f"{server}/view?{query}") as r:
                return Image.open(io.BytesIO(r.read()))
        if status.get("completed"):
            sys.exit("ComfyUI finished without an output image.")


if __name__ == "__main__":
    main()
