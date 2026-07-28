#!/usr/bin/env python3
"""Simple OCR script for up to two image files.

Usage:
    python ocr_two_images.py <image1> [image2]

The script creates a PaddleOCR engine (English) and prints the extracted
text together with a confidence score for each line.

To run inside a virtual environment:
    python -m venv .venv
    source .venv/bin/activate   # on Windows use `.venv\Scripts\activate`
    pip install -r requirements.txt
    python ocr_two_images.py path/to/img1.jpg path/to/img2.jpg
"""

import sys
from pathlib import Path
from paddleocr import PaddleOCR


def ocr_image(image_path: Path, engine: PaddleOCR):
    """Run OCR on a single image and return a list of (text, confidence)."""
    result = engine.ocr(str(image_path), cls=True)
    lines = []
    for page in result or []:
        for line in page:
            bbox, (text, confidence) = line
            lines.append((text, float(confidence)))
    return lines


OUTPUT_DIR = Path(__file__).parent / "ocr_two_images"


def main():
    if len(sys.argv) < 2 or len(sys.argv) > 3:
        print("Usage: python ocr_two_images.py <image1> [image2]")
        sys.exit(1)

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    output_file = OUTPUT_DIR / "a"

    ocr_engine = PaddleOCR(use_angle_cls=True, lang="vi")
    for arg in sys.argv[1:]:
        img_path = Path(arg)
        if not img_path.is_file():
            print(f"[ERROR] File not found: {img_path}")
            continue
        lines = ocr_image(img_path, ocr_engine)
        print(f"--- OCR result for {img_path.name} ---")
        for text, conf in lines:
            line = f"{text} (conf: {conf:.2f})"
            print(line)
            with open(output_file, "a") as f:
                f.write(line + "\n")
        print()


if __name__ == "__main__":
    main()
