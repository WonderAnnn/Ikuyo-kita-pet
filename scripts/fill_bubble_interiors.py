"""Create bubble-filled.png while preserving every original non-transparent pixel."""

from __future__ import annotations

import json
import shutil
from pathlib import Path

import cv2
import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1] / "assets" / "bubbles"


def enclosed_region(alpha: np.ndarray, seed_x: int, seed_y: int) -> np.ndarray:
    free = (alpha == 0).astype(np.uint8)
    height, width = free.shape
    for radius in (0, 2, 4, 8, 12, 16, 24, 32):
        candidate = free
        if radius:
            barrier = cv2.morphologyEx(
                (alpha > 0).astype(np.uint8),
                cv2.MORPH_CLOSE,
                np.ones((radius * 2 + 1, radius * 2 + 1), np.uint8),
            )
            candidate = (barrier == 0).astype(np.uint8)
        _, labels, stats, _ = cv2.connectedComponentsWithStats(candidate, 8)
        label = int(labels[seed_y, seed_x])
        if label == 0:
            continue
        x, y, component_width, component_height, _ = stats[label]
        touches_edge = (
            x == 0
            or y == 0
            or x + component_width == width
            or y + component_height == height
        )
        if not touches_edge:
            return labels == label
    raise RuntimeError("The text-safe-area interior is not enclosed by the artwork outline.")


def process(directory: Path) -> None:
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8-sig"))
    original = directory / "bubble.png"
    source = directory / "bubble-source.png"
    output = directory / "bubble-filled.png"
    if not source.exists():
        shutil.copy2(original, source)

    pixels = np.array(Image.open(source).convert("RGBA"))
    height, width = pixels.shape[:2]
    safe = manifest["textSafeArea"]
    seed_x = min(width - 1, int((safe["left"] + safe["width"] / 2) * width))
    seed_y = min(height - 1, int((safe["top"] + safe["height"] / 2) * height))
    if pixels[seed_y, seed_x, 3] == 0:
        fill = enclosed_region(pixels[:, :, 3], seed_x, seed_y)
        pixels[fill] = (255, 255, 255, 255)
    Image.fromarray(pixels, "RGBA").save(output, optimize=True)


if __name__ == "__main__":
    for theme_directory in sorted(path for path in ROOT.iterdir() if path.is_dir()):
        process(theme_directory)
