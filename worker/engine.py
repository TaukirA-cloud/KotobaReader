"""Offline JSON-lines worker. No network clients or automatic model downloads."""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

LANGUAGES = {"ja": "jpn", "zh": "chi_sim", "ko": "kor", "en": "eng",
             "es": "spa", "fr": "fra", "de": "deu"}
LAYOUTS = {"Auto": "3", "Block": "6", "Vertical": "5"}
_loaded = {}
PIPELINE_VERSION = b"kotoba-ct2-bubble-v2-length-penalty-0.2"


def model_inventory(root: str) -> list[dict]:
    result = []
    directory = Path(root)
    if not directory.is_dir():
        return result
    for metadata in sorted(directory.glob("*/metadata.json")):
        try:
            data = json.loads(metadata.read_text(encoding="utf-8"))
            folder = metadata.parent
            if not (folder / "model" / "model.bin").is_file() or not (folder / "sentencepiece.model").is_file():
                continue
            # A model identity changes when its installation changes, invalidating UI caches.
            stat = (folder / "model" / "model.bin").stat()
            identity = hashlib.sha256(PIPELINE_VERSION + metadata.read_bytes() + str(folder.resolve()).encode()
                                      + f"{stat.st_size}:{stat.st_mtime_ns}".encode()).hexdigest()
            result.append({"source": data["from_code"], "target": data["to_code"],
                           "version": data.get("package_version", "unknown"),
                           "id": identity, "path": str(folder.resolve()), "target_prefix": data.get("target_prefix", "")})
        except (OSError, ValueError, KeyError):
            continue
    return result


def tesseract_path(config: dict) -> str:
    configured = config.get("tesseract", "").strip()
    if configured:
        if not Path(configured).is_file():
            raise ValueError("Tesseract executable was not found. Check Settings.")
        return configured
    found = shutil.which("tesseract")
    if found:
        return found
    for base in (os.environ.get("ProgramFiles", "C:/Program Files"),
                 os.environ.get("LOCALAPPDATA", "")):
        candidate = Path(base) / "Tesseract-OCR" / "tesseract.exe"
        if candidate.is_file():
            return str(candidate)
    raise ValueError("Tesseract is not installed. Follow docs/SETUP.md, then check Settings.")


def tessdata_args(config: dict) -> list[str]:
    directory = config.get("tessdata", "").strip()
    return ["--tessdata-dir", directory] if directory else []


def run_tesseract(args: list[str]) -> str:
    completed = subprocess.run(args, capture_output=True, encoding="utf-8", errors="replace",
                               timeout=90, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    if completed.returncode:
        raise ValueError("OCR failed: " + completed.stderr.strip()[-1500:])
    return completed.stdout.strip()


def diagnose(config: dict) -> dict:
    warnings = []
    available = []
    try:
        available = run_tesseract([tesseract_path(config), "--list-langs", *tessdata_args(config)]).splitlines()[1:]
    except (ValueError, OSError, subprocess.TimeoutExpired) as exc:
        warnings.append(str(exc))
    for dependency in ("ctranslate2", "sentencepiece"):
        if importlib.util.find_spec(dependency) is None:
            warnings.append(f"Missing {dependency}. Run scripts/setup-engine.ps1.")
    models = model_inventory(config.get("models", ""))
    if not models:
        warnings.append("No translation model installed. Run scripts/setup-engine.ps1.")
    return {"languages": available, "models": models, "warnings": warnings}


def recognize(request: dict) -> dict:
    image = Path(request.get("image", ""))
    if not image.is_file() or image.stat().st_size > 64 * 1024 * 1024:
        raise ValueError("OCR image is missing or exceeds 64 MB.")
    source = request.get("source", "ja")
    if source not in LANGUAGES:
        raise ValueError("Unsupported OCR language.")
    layout = request.get("layout", "Auto")
    if layout not in LAYOUTS:
        raise ValueError("Unsupported OCR layout.")
    language = "jpn_vert" if source == "ja" and layout == "Vertical" else LANGUAGES[source]
    config = request["config"]
    text = run_tesseract([tesseract_path(config), str(image.resolve()), "stdout", "-l", language,
                          "--psm", LAYOUTS[layout], *tessdata_args(config)])
    if source in ("ja", "zh"):
        text = "".join(text.split())
    return {"text": text}


def translate(request: dict) -> dict:
    text = request.get("text", "").strip()
    if not text or len(text) > 8000:
        raise ValueError("Enter between 1 and 8,000 characters.")
    source, target = request.get("source"), request.get("target")
    if source == target:
        return {"text": text, "model": "identity"}
    models = model_inventory(request["config"].get("models", ""))
    model = next((m for m in models if m["source"] == source and m["target"] == target), None)
    if model is None:
        raise ValueError(f"No offline {source} → {target} model. Install this pair with scripts/setup-engine.ps1 -Source {source} -Target {target}.")
    if request.get("model") and request["model"] != model["id"]:
        raise ValueError("Translation model changed. Run Check engine again.")
    import ctranslate2
    import sentencepiece
    if model["id"] not in _loaded:
        _loaded.clear()  # Keep just one resident translation model.
        folder = Path(model["path"])
        _loaded[model["id"]] = (
            ctranslate2.Translator(str(folder / "model"), device="cpu", compute_type="int8",
                                  inter_threads=1, intra_threads=4),
            sentencepiece.SentencePieceProcessor(model_file=str(folder / "sentencepiece.model")))
    translator, tokenizer = _loaded[model["id"]]
    # Bound each sequence. Never silently truncate a long input.
    paragraphs = [line.strip() for line in text.splitlines() if line.strip()]
    encoded = [tokenizer.encode(line, out_type=str) for line in paragraphs]
    if any(len(tokens) > 450 for tokens in encoded):
        raise ValueError("Text is too long for one bubble. Split it into shorter lines (under 450 tokens each).")
    prefix = model["target_prefix"]
    results = translator.translate_batch(encoded, beam_size=4, max_input_length=0, max_decoding_length=512,
                                         replace_unknowns=True, length_penalty=0.2,
                                         target_prefix=[[prefix]] * len(encoded) if prefix else None)
    decoded = [tokenizer.decode(result.hypotheses[0]) for result in results]
    output = "\n".join(line.removeprefix(prefix).strip() if prefix else line.strip() for line in decoded)
    warning = ""
    if any(len(result.hypotheses[0]) >= 512 for result in results):
        warning = "Translation reached the output limit. Split the text into smaller selections."
    elif target == "en" and any('\u3040' <= char <= '\u9fff' for char in output):
        warning = "The model left some source text untranslated. Review the result or try a different selection."
    return {"text": output, "model": model["id"], "warning": warning}


def dispatch(request: dict) -> dict:
    operation = request.get("operation")
    if operation == "diagnose":
        return diagnose(request.get("config", {}))
    if operation == "ocr":
        return recognize(request)
    if operation == "translate":
        return translate(request)
    raise ValueError("Unknown engine operation.")


def main() -> None:
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8")
    for line in sys.stdin:
        request = {}
        try:
            if len(line) > 1_000_000:
                raise ValueError("Engine request exceeds size limit.")
            request = json.loads(line)
            # Libraries may write to stdout; reserve it exclusively for our protocol.
            from contextlib import redirect_stdout
            with redirect_stdout(sys.stderr):
                result = dispatch(request)
            response = {"id": request.get("id"), "ok": True, "result": result}
        except Exception as exc:
            response = {"id": request.get("id") if isinstance(request, dict) else None,
                        "ok": False, "error": str(exc)}
        print(json.dumps(response, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
