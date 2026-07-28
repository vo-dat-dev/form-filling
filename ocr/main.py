import os
import uuid
import shutil
from pathlib import Path
from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from paddleocr import PaddleOCR

app = FastAPI(title="OCR Service")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

UPLOAD_DIR = Path("/tmp/ocr-uploads")
UPLOAD_DIR.mkdir(parents=True, exist_ok=True)

ocr_engine = PaddleOCR(use_angle_cls=True, lang="en")


@app.post("/ocr")
async def ocr(file: UploadFile = File(...)):
    if not file.content_type or not file.content_type.startswith(
        ("image/", "application/pdf")
    ):
        raise HTTPException(400, "Only image or PDF files are supported")

    ext = Path(file.filename or "file").suffix or ".bin"
    filename = f"{uuid.uuid4().hex}{ext}"
    filepath = UPLOAD_DIR / filename

    try:
        with open(filepath, "wb") as f:
            f.write(await file.read())

        result = ocr_engine.ocr(str(filepath), cls=True)

        pages = []
        for page_idx, page_result in enumerate(result):
            lines = []
            for line in page_result or []:
                bbox, (text, confidence) = line
                lines.append(
                    {
                        "text": text,
                        "confidence": round(float(confidence), 4),
                        "bbox": [[round(float(c), 2) for c in point] for point in bbox],
                    }
                )
            pages.append({"page": page_idx, "lines": lines})

        return {"pages": pages, "total_pages": len(pages)}
    finally:
        if filepath.exists():
            filepath.unlink()


@app.get("/health")
async def health():
    return {"status": "ok"}
