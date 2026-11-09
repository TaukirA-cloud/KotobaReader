"""Explicit, one-time online installation of an Argos-format SentencePiece model."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import urllib.error
import urllib.request
import zipfile

INDEX = "https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json"
KNOWN_HASHES = {"translate-ja_en-1_1.argosmodel": "623e3477959a815eb0a5ef53e09079ae8f1f9d3bbcd230473baf28c03fb83335"}


def validate_members(zipped: zipfile.ZipFile, extracted: Path) -> None:
    if len(zipped.infolist()) > 10_000 or sum(item.file_size for item in zipped.infolist()) > 2_000_000_000:
        raise ValueError("Model exceeds archive limits.")
    for item in zipped.infolist():
        candidate = (extracted / item.filename).resolve()
        if not candidate.is_relative_to(extracted.resolve()):
            raise ValueError("Unsafe model archive path.")


def install(source: str, target: str, destination: Path) -> Path:
    with urllib.request.urlopen(INDEX, timeout=60) as response:
        index = json.load(response)
    packages = [p for p in index if p["from_code"] == source and p["to_code"] == target]
    if not packages:
        raise ValueError(f"No direct {source} → {target} model is available in the Argos index.")
    package = sorted(packages, key=lambda p: tuple(int(n) for n in p["package_version"].split(".")))[-1]
    url = next(link for link in package["links"] if link.startswith("https://"))
    destination.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=destination) as temporary:
        root = Path(temporary)
        archive = root / "model.zip"
        urls = [link for link in package["links"] if link.startswith("https://")]
        # The upstream index host can reject some networks; this is Argos's own data mirror.
        urls.append("https://data.argosopentech.com/argospm/v1/" + url.rsplit("/", 1)[-1])
        errors = []
        for candidate in urls:
            try:
                print(f"Downloading {candidate}", flush=True)
                with urllib.request.urlopen(candidate, timeout=180) as response, archive.open("wb") as output:
                    shutil.copyfileobj(response, output)
                url = candidate
                break
            except (OSError, urllib.error.URLError) as exc:
                errors.append(f"{candidate}: {exc}")
        else:
            raise ValueError("Model download failed. " + "; ".join(errors))
        digest = hashlib.sha256(archive.read_bytes()).hexdigest()
        expected = KNOWN_HASHES.get(url.rsplit("/", 1)[-1])
        if expected and digest != expected:
            raise ValueError("Downloaded model checksum does not match the known package. Installation stopped.")
        extracted = root / "unpacked"
        with zipfile.ZipFile(archive) as zipped:
            validate_members(zipped, extracted)
            zipped.extractall(extracted)
        metadata = next(extracted.rglob("metadata.json"))
        model_root = metadata.parent
        if not (model_root / "sentencepiece.model").exists() or not (model_root / "model/model.bin").exists():
            raise ValueError("This model format is unsupported; a SentencePiece CTranslate2 model is required.")
        actual = json.loads(metadata.read_text(encoding="utf-8"))
        if actual["from_code"] != source or actual["to_code"] != target:
            raise ValueError("Model metadata does not match requested languages.")
        provenance = {"index": INDEX, "url": url, "sha256": digest, "package": package,
                      "notice": "Retain all license files in this package. See docs/THIRD_PARTY.md before redistribution."}
        (model_root / "provenance.json").write_text(json.dumps(provenance, indent=2), encoding="utf-8")
        final = destination / f"{source}_{target}"
        if final.exists():
            raise ValueError(f"{final} already exists. Remove it explicitly before replacing this model.")
        # Copy rather than rename: TemporaryDirectory can carry owner-only ACLs on Windows.
        # A new destination inherits the user's normal models-directory permissions.
        shutil.copytree(model_root, final)
    return final


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", default="ja")
    parser.add_argument("--target", default="en")
    parser.add_argument("--destination", type=Path, required=True)
    args = parser.parse_args()
    print(install(args.source, args.target, args.destination))
