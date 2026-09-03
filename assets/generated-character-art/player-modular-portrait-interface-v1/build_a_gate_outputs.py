#!/usr/bin/env python3
"""Build deterministic A-gate normalized output and visual contact sheet."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


NATIVE_SIZE = (1024, 1536)
SAFE_RECT = (128, 0, 896, 1536)
NORMALIZED_SIZE = (887, 1774)


def _load_rgba(path: Path, expected_size: tuple[int, int]) -> Image.Image:
    with Image.open(path) as image:
        if image.size != expected_size:
            raise ValueError(f"unexpected image size for {path}: {image.size}")
        if image.mode != "RGBA":
            raise ValueError(f"expected RGBA image for {path}, got {image.mode}")
        return image.copy()


def _resize_float_channel(channel: np.ndarray, size: tuple[int, int]) -> np.ndarray:
    image = Image.fromarray(channel.astype(np.float32), mode="F")
    return np.asarray(image.resize(size, Image.Resampling.LANCZOS), dtype=np.float32)


def normalize(native: Image.Image) -> Image.Image:
    source = np.asarray(native.crop(SAFE_RECT), dtype=np.float32)
    alpha = source[:, :, 3] / 255.0
    premultiplied = source[:, :, :3] * alpha[:, :, None]

    resized_alpha = np.clip(_resize_float_channel(alpha, NORMALIZED_SIZE), 0.0, 1.0)
    resized_premultiplied = np.stack(
        [
            np.clip(_resize_float_channel(premultiplied[:, :, channel], NORMALIZED_SIZE), 0.0, 255.0)
            for channel in range(3)
        ],
        axis=2,
    )

    straight = np.zeros_like(resized_premultiplied)
    visible = resized_alpha > 0.0
    straight[visible] = resized_premultiplied[visible] / resized_alpha[visible, None]

    output = np.zeros((NORMALIZED_SIZE[1], NORMALIZED_SIZE[0], 4), dtype=np.uint8)
    output[:, :, :3] = np.clip(np.rint(straight), 0, 255).astype(np.uint8)
    output[:, :, 3] = np.clip(np.rint(resized_alpha * 255.0), 0, 255).astype(np.uint8)
    output[output[:, :, 3] == 0, :3] = 0
    return Image.fromarray(output, mode="RGBA")


def canonicalize_transparent_rgb(image: Image.Image) -> Image.Image:
    pixels = np.asarray(image, dtype=np.uint8).copy()
    pixels[pixels[:, :, 3] == 0, :3] = 0
    return Image.fromarray(pixels, mode="RGBA")


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _alpha_metrics(image: Image.Image) -> dict[str, object]:
    pixels = np.asarray(image, dtype=np.uint8)
    alpha = pixels[:, :, 3]
    visible_y, visible_x = np.nonzero(alpha > 0)
    bbox = None
    if visible_x.size:
        bbox = [
            int(visible_x.min()),
            int(visible_y.min()),
            int(visible_x.max()),
            int(visible_y.max()),
        ]
    transparent = alpha == 0
    return {
        "size": list(image.size),
        "mode": image.mode,
        "alphaMin": int(alpha.min()),
        "alphaMax": int(alpha.max()),
        "transparentPixels": int(np.count_nonzero(transparent)),
        "partialAlphaPixels": int(np.count_nonzero((alpha > 0) & (alpha < 255))),
        "opaquePixels": int(np.count_nonzero(alpha == 255)),
        "visibleBboxInclusive": bbox,
        "transparentRgbZero": bool(np.all(pixels[transparent, :3] == 0)),
        "cornerAlpha": [
            int(alpha[0, 0]),
            int(alpha[0, -1]),
            int(alpha[-1, 0]),
            int(alpha[-1, -1]),
        ],
    }


def _checkerboard(size: tuple[int, int], tile: int = 24) -> Image.Image:
    width, height = size
    y, x = np.indices((height, width))
    mask = ((x // tile) + (y // tile)) % 2
    pixels = np.where(mask[:, :, None] == 0, 238, 210).astype(np.uint8)
    return Image.fromarray(np.repeat(pixels, 3, axis=2), mode="RGB")


def _fit(image: Image.Image, panel_size: tuple[int, int], transparent: bool) -> Image.Image:
    panel = _checkerboard(panel_size) if transparent else Image.new("RGB", panel_size, (235, 235, 235))
    preview = image.copy()
    preview.thumbnail(panel_size, Image.Resampling.LANCZOS)
    x = (panel_size[0] - preview.width) // 2
    y = (panel_size[1] - preview.height) // 2
    if transparent:
        panel.paste(preview, (x, y), preview)
    else:
        panel.paste(preview.convert("RGB"), (x, y))
    return panel


def contact_sheet(green_source: Image.Image, native: Image.Image, normalized: Image.Image) -> Image.Image:
    panel_size = (512, 768)
    labels = ("model green source", "native RGBA", "normalized RGBA")
    panels = (
        _fit(green_source, panel_size, False),
        _fit(native, panel_size, True),
        _fit(normalized, panel_size, True),
    )
    sheet = Image.new("RGB", (panel_size[0] * 3, panel_size[1] + 48), (32, 32, 32))
    draw = ImageDraw.Draw(sheet)
    for index, (panel, label) in enumerate(zip(panels, labels)):
        x = index * panel_size[0]
        sheet.paste(panel, (x, 0))
        draw.text((x + 16, panel_size[1] + 16), label, fill=(245, 245, 245))
    return sheet


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--green-source", type=Path, required=True)
    parser.add_argument("--native", type=Path, required=True)
    parser.add_argument("--normalized", type=Path, required=True)
    parser.add_argument("--contact-sheet", type=Path, required=True)
    args = parser.parse_args()

    with Image.open(args.green_source) as image:
        if image.size != NATIVE_SIZE:
            raise ValueError(f"unexpected green source size: {image.size}")
        green_source = image.convert("RGB")
    native = canonicalize_transparent_rgb(_load_rgba(args.native, NATIVE_SIZE))
    native.save(args.native, format="PNG", compress_level=9)
    normalized = normalize(native)

    args.normalized.parent.mkdir(parents=True, exist_ok=True)
    normalized.save(args.normalized, format="PNG", compress_level=9)
    args.contact_sheet.parent.mkdir(parents=True, exist_ok=True)
    contact_sheet(green_source, native, normalized).save(
        args.contact_sheet, format="PNG", compress_level=9
    )

    print(f"Wrote {args.normalized}")
    print(f"Wrote {args.contact_sheet}")
    source_pixels = np.asarray(green_source, dtype=np.uint8)
    native_pixels = np.asarray(native, dtype=np.uint8)
    opaque = native_pixels[:, :, 3] == 255
    metrics = {
        "greenSource": {
            "size": list(green_source.size),
            "mode": green_source.mode,
            "sha256": _sha256(args.green_source),
        },
        "native": {
            **_alpha_metrics(native),
            "sha256": _sha256(args.native),
            "opaqueRgbMatchesGreenSource": bool(
                np.array_equal(source_pixels[opaque], native_pixels[opaque, :3])
            ),
            "insideSafeRect": bool(
                _alpha_metrics(native)["visibleBboxInclusive"] is not None
                and _alpha_metrics(native)["visibleBboxInclusive"][0] >= SAFE_RECT[0]
                and _alpha_metrics(native)["visibleBboxInclusive"][2] < SAFE_RECT[2]
            ),
        },
        "normalized": {
            **_alpha_metrics(normalized),
            "sha256": _sha256(args.normalized),
        },
        "contactSheetSha256": _sha256(args.contact_sheet),
    }
    print(json.dumps(metrics, ensure_ascii=False, sort_keys=True))


if __name__ == "__main__":
    main()
