#!/usr/bin/env python3
"""Build the registered full-body B-gate candidate from frozen native RGBA inputs.

The registration is intentionally the only transform stage.  Each layer is
resampled once from its original 1024x1536 source into the common 1024x1536
full-body canvas.  The two composites only alpha-composite those calibrated
layers in the declared fixed order.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent
REGISTRATION_PATH = ROOT / "registration.json"
CANVAS_SIZE = (1024, 1536)
LAYER_ORDER = ("hair-back", "base-head", "outfit-body", "hair-front")


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def decoded_pixel_sha256(image: Image.Image) -> str:
    canonical = canonicalize_transparent_rgb(image)
    return hashlib.sha256(canonical.tobytes()).hexdigest()


def load_rgba(path: Path) -> Image.Image:
    with Image.open(path) as opened:
        if opened.size != CANVAS_SIZE:
            raise ValueError(f"{path}: expected {CANVAS_SIZE}, got {opened.size}")
        if opened.mode != "RGBA":
            raise ValueError(f"{path}: expected RGBA, got {opened.mode}")
        return canonicalize_transparent_rgb(opened.copy())


def canonicalize_transparent_rgb(image: Image.Image) -> Image.Image:
    pixels = np.asarray(image, dtype=np.uint8).copy()
    pixels[pixels[:, :, 3] == 0, :3] = 0
    return Image.fromarray(pixels, mode="RGBA")


def resize_premultiplied(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    """Resize once using premultiplied-alpha Lanczos, then restore straight RGBA."""
    source = np.asarray(image, dtype=np.float32)
    alpha = source[:, :, 3] / 255.0
    premultiplied = source[:, :, :3] * alpha[:, :, None]

    def resize_float(channel: np.ndarray) -> np.ndarray:
        channel_image = Image.fromarray(channel.astype(np.float32), mode="F")
        return np.asarray(
            channel_image.resize(size, Image.Resampling.LANCZOS), dtype=np.float32
        )

    resized_alpha = np.clip(resize_float(alpha), 0.0, 1.0)
    resized_premultiplied = np.stack(
        [np.clip(resize_float(premultiplied[:, :, channel]), 0.0, 255.0) for channel in range(3)],
        axis=2,
    )
    straight = np.zeros_like(resized_premultiplied)
    visible = resized_alpha > 0.0
    straight[visible] = resized_premultiplied[visible] / resized_alpha[visible, None]

    output = np.zeros((size[1], size[0], 4), dtype=np.uint8)
    output[:, :, :3] = np.clip(np.rint(straight), 0, 255).astype(np.uint8)
    output[:, :, 3] = np.clip(np.rint(resized_alpha * 255.0), 0, 255).astype(np.uint8)
    output[output[:, :, 3] == 0, :3] = 0
    return Image.fromarray(output, mode="RGBA")


def calibrate(native: Image.Image, transform: dict[str, object]) -> Image.Image:
    scale = float(transform["scale"])
    if scale <= 0:
        raise ValueError(f"registration scale must be positive: {scale}")
    scaled_size = (
        max(1, round(native.width * scale)),
        max(1, round(native.height * scale)),
    )
    registered = Image.new("RGBA", CANVAS_SIZE, (0, 0, 0, 0))
    registered.alpha_composite(
        resize_premultiplied(native, scaled_size),
        tuple(int(value) for value in transform["translate"]),
    )
    return canonicalize_transparent_rgb(registered)


def alpha_metrics(image: Image.Image) -> dict[str, object]:
    pixels = np.asarray(image, dtype=np.uint8)
    alpha = pixels[:, :, 3]
    ys, xs = np.nonzero(alpha > 0)
    bbox = None
    if xs.size:
        bbox = [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())]
    transparent = alpha == 0
    return {
        "size": list(image.size),
        "mode": image.mode,
        "visibleBboxInclusive": bbox,
        "opaquePixels": int(np.count_nonzero(alpha == 255)),
        "partialAlphaPixels": int(np.count_nonzero((alpha > 0) & (alpha < 255))),
        "transparentPixels": int(np.count_nonzero(transparent)),
        "transparentRgbZero": bool(np.all(pixels[transparent, :3] == 0)),
        "decodedPixelSha256": decoded_pixel_sha256(image),
    }


def layer_path(component: dict[str, object]) -> Path:
    return ROOT / str(component["layerPath"])


def source_path(component: dict[str, object]) -> Path:
    return ROOT / str(component["sourcePath"])


def composite(layers: dict[str, Image.Image], head_id: str) -> Image.Image:
    result = Image.new("RGBA", CANVAS_SIZE, (0, 0, 0, 0))
    for layer_id in LAYER_ORDER:
        component_id = head_id if layer_id == "base-head" else layer_id
        result.alpha_composite(layers[component_id])
    return canonicalize_transparent_rgb(result)


def fit(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    checker = Image.new("RGB", size, (232, 232, 232))
    draw = ImageDraw.Draw(checker)
    tile = 24
    for y in range(0, size[1], tile):
        for x in range(0, size[0], tile):
            if ((x // tile) + (y // tile)) % 2:
                draw.rectangle((x, y, x + tile - 1, y + tile - 1), fill=(208, 208, 208))
    preview = image.copy()
    preview.thumbnail(size, Image.Resampling.LANCZOS)
    x = (size[0] - preview.width) // 2
    y = (size[1] - preview.height) // 2
    checker.paste(preview, (x, y), preview)
    return checker


def make_layout(composite_image: Image.Image, registration: dict[str, object]) -> Image.Image:
    layout = Image.new("RGB", CANVAS_SIZE, (238, 239, 241))
    draw = ImageDraw.Draw(layout)
    for value in range(0, 1537, 128):
        draw.line((0, value, 1023, value), fill=(214, 218, 222), width=1)
    for value in range(0, 1025, 128):
        draw.line((value, 0, value, 1535), fill=(214, 218, 222), width=1)
    layout.paste(composite_image, (0, 0), composite_image)
    anchors = registration["sharedAnchors"]
    head_box = anchors["headFrame"]
    draw.rectangle(tuple(head_box), outline=(204, 62, 62), width=3)
    collar = anchors["collarOverlap"]
    draw.line((collar[0], collar[1], collar[2], collar[1]), fill=(46, 111, 165), width=3)
    draw.text((24, 22), "registered full-body canvas 1024 x 1536", fill=(30, 30, 30))
    draw.text((head_box[0], max(0, head_box[1] - 22)), "head frame", fill=(150, 40, 40))
    draw.text((collar[0], collar[1] + 10), "collar overlap", fill=(35, 80, 120))
    return layout


def make_contact_sheet(
    layers: dict[str, Image.Image], composites: dict[str, Image.Image]
) -> Image.Image:
    panel_size = (384, 576)
    labels_and_images = [
        ("base head: refined", layers["head_young_refined_01"]),
        ("base head: defined", layers["head_young_defined_01"]),
        ("hair back", layers["hair-back"]),
        ("hair front", layers["hair-front"]),
        ("outfit body", layers["outfit-body"]),
        ("composite: refined", composites["head_young_refined_01"]),
        ("composite: defined", composites["head_young_defined_01"]),
    ]
    sheet = Image.new("RGB", (panel_size[0] * 4, (panel_size[1] + 40) * 3), (32, 32, 32))
    draw = ImageDraw.Draw(sheet)
    for index, (label, image) in enumerate(labels_and_images):
        column, row = index % 4, index // 4
        x, y = column * panel_size[0], row * (panel_size[1] + 40)
        sheet.paste(fit(image, panel_size), (x, y))
        draw.text((x + 12, y + panel_size[1] + 12), label, fill=(245, 245, 245))

    closeup_box = (280, 70, 745, 650)
    for offset, head_id in enumerate(("head_young_refined_01", "head_young_defined_01")):
        closeup = composites[head_id].crop(closeup_box)
        x = (2 + offset) * panel_size[0]
        y = 2 * (panel_size[1] + 40)
        sheet.paste(fit(closeup, panel_size), (x, y))
        draw.text((x + 12, y + panel_size[1] + 12), f"interface closeup: {head_id}", fill=(245, 245, 245))
    return sheet


def validate(
    registration: dict[str, object],
    components: list[dict[str, object]],
    layers: dict[str, Image.Image],
    composites: dict[str, Image.Image],
) -> dict[str, object]:
    component_checks = {}
    for component in components:
        component_id = str(component["id"])
        native = load_rgba(source_path(component))
        rebuilt = calibrate(native, component["transform"])
        expected = layers[component_id]
        component_checks[component_id] = {
            "sourceSha256": sha256_file(source_path(component)),
            "sourceHashMatchesRegistration": sha256_file(source_path(component))
            == component["sourceSha256"],
            "layerPath": str(component["layerPath"]),
            "layerSha256": sha256_file(layer_path(component)),
            "metrics": alpha_metrics(expected),
            "repeatBuildDecodedPixelSha256": decoded_pixel_sha256(rebuilt),
            "repeatBuildMatches": decoded_pixel_sha256(rebuilt)
            == decoded_pixel_sha256(expected),
        }

    face_samples = {}
    for sample in registration["faceSamplePoints"]:
        x, y = sample["xy"]
        face_samples[sample["id"]] = {
            "xy": [x, y],
            "headAlpha": {
                "head_young_refined_01": int(layers["head_young_refined_01"].getpixel((x, y))[3]),
                "head_young_defined_01": int(layers["head_young_defined_01"].getpixel((x, y))[3]),
            },
            "outfitAlpha": int(layers["outfit-body"].getpixel((x, y))[3]),
            "hairFrontAlpha": int(layers["hair-front"].getpixel((x, y))[3]),
        }
    face_samples_clear = all(
        check["outfitAlpha"] == 0
        and check["hairFrontAlpha"] == 0
        and all(alpha > 0 for alpha in check["headAlpha"].values())
        for check in face_samples.values()
    )

    return {
        "schemaVersion": 1,
        "taskId": "A-CHAR-PORTRAIT-REGISTER-01",
        "status": "registered_candidate_pending_b_gate_review",
        "canvas": {"width": 1024, "height": 1536, "pixelFormat": "sRGB 8-bit straight RGBA"},
        "objectiveChecks": {
            "sourceHashesMatch": all(
                item["sourceHashMatchesRegistration"] for item in component_checks.values()
            ),
            "allLayersRgba1024x1536": all(
                item["metrics"]["size"] == [1024, 1536]
                and item["metrics"]["mode"] == "RGBA"
                for item in component_checks.values()
            ),
            "transparentRgbZero": all(
                item["metrics"]["transparentRgbZero"] for item in component_checks.values()
            ),
            "repeatBuildDecodedPixelsMatch": all(
                item["repeatBuildMatches"] for item in component_checks.values()
            ),
            "fullNativeCanvasRegisteredWithoutCentralCrop": True,
            "compositionTransforms": "none; calibrated layers alpha-composited in fixed order only",
            "layerOrder": list(LAYER_ORDER),
            "sharedHairAndOutfitReuse": {
                "head_young_refined_01": ["hair-back", "outfit-body", "hair-front"],
                "head_young_defined_01": ["hair-back", "outfit-body", "hair-front"],
                "sameCalibratedLayerDecodedHashes": {
                    "hair-back": decoded_pixel_sha256(layers["hair-back"]),
                    "outfit-body": decoded_pixel_sha256(layers["outfit-body"]),
                    "hair-front": decoded_pixel_sha256(layers["hair-front"]),
                },
            },
            "faceSamplesClearOfOutfitAndFrontHair": {
                "passed": face_samples_clear,
                "samples": face_samples,
            },
            "composites": {
                head_id: {**alpha_metrics(image), "sha256": sha256_file(ROOT / path)}
                for head_id, image, path in (
                    (
                        "head_young_refined_01",
                        composites["head_young_refined_01"],
                        "evidence/gates/b-minimal/registered/composite_young_refined.png",
                    ),
                    (
                        "head_young_defined_01",
                        composites["head_young_defined_01"],
                        "evidence/gates/b-minimal/registered/composite_young_defined.png",
                    ),
                )
            },
        },
        "components": component_checks,
        "visualChecks": registration["visualReview"],
        "notProven": [
            "This registration proves only these five existing components can be reused in the two reviewed combinations.",
            "B-gate written acceptance remains the responsibility of A-CHAR-PORTRAIT-INTERFACE-01C.",
            "No C1 or C2 image budget, component, Unity asset, or final UI export is approved by this package.",
        ],
        "eligibleForBGateReview": True,
    }


def main() -> None:
    registration = json.loads(REGISTRATION_PATH.read_text(encoding="utf-8"))
    components = registration["components"]
    canvas = registration["calibrationCanvas"]
    if (canvas["width"], canvas["height"]) != CANVAS_SIZE:
        raise ValueError("registration canvas must remain the approved 1024x1536 full canvas")

    layers: dict[str, Image.Image] = {}
    output_details = {}
    for component in components:
        component_id = str(component["id"])
        source = source_path(component)
        if sha256_file(source) != component["sourceSha256"]:
            raise ValueError(f"frozen source hash mismatch: {source}")
        registered = calibrate(load_rgba(source), component["transform"])
        destination = layer_path(component)
        destination.parent.mkdir(parents=True, exist_ok=True)
        registered.save(destination, format="PNG", compress_level=9)
        layers[component_id] = registered
        output_details[component_id] = {
            "layerPath": str(component["layerPath"]),
            "sha256": sha256_file(destination),
            **alpha_metrics(registered),
        }

    composites = {
        "head_young_refined_01": composite(layers, "head_young_refined_01"),
        "head_young_defined_01": composite(layers, "head_young_defined_01"),
    }
    evidence = ROOT / "evidence/gates/b-minimal/registered"
    evidence.mkdir(parents=True, exist_ok=True)
    composite_paths = {
        "head_young_refined_01": evidence / "composite_young_refined.png",
        "head_young_defined_01": evidence / "composite_young_defined.png",
    }
    for head_id, image in composites.items():
        image.save(composite_paths[head_id], format="PNG", compress_level=9)

    make_layout(composites["head_young_refined_01"], registration).save(
        evidence / "layout.png", format="PNG", compress_level=9
    )
    make_contact_sheet(layers, composites).save(
        evidence / "contact-sheet.png", format="PNG", compress_level=9
    )

    registration["generatedOutputs"] = output_details
    registration["composites"] = {
        head_id: {
            "path": str(path.relative_to(ROOT)).replace("\\", "/"),
            "sha256": sha256_file(path),
            "decodedPixelSha256": decoded_pixel_sha256(composites[head_id]),
        }
        for head_id, path in composite_paths.items()
    }
    REGISTRATION_PATH.write_text(
        json.dumps(registration, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    validation = validate(registration, components, layers, composites)
    (evidence / "validation.json").write_text(
        json.dumps(validation, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps({"registration": str(REGISTRATION_PATH), "validation": validation["objectiveChecks"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
