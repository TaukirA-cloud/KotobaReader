"""Check Git publication candidates without printing secret values."""
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
FORBIDDEN_PARTS = {".tools", "runtime", "models", "artifacts", ".venv", ".vs", "bin", "obj", "__pycache__"}
FORBIDDEN_SUFFIXES = {".exe", ".dll", ".pdb", ".zip", ".cbz", ".cbr", ".argosmodel", ".traineddata", ".onnx", ".pem", ".key", ".pfx", ".p12"}
PATTERNS = {
    "GitHub token": re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})\b"),
    "AWS access key": re.compile(r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
    "private key": re.compile(r"-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----"),
    "Slack token": re.compile(r"\bxox[baprs]-[A-Za-z0-9-]{20,}\b"),
    "machine-specific user path": re.compile(r"[A-Za-z]:[\\/]Users[\\/][^\s\\/]+", re.IGNORECASE),
    "personal email": re.compile(r"\b[A-Za-z0-9._%+-]+@(?:gmail|hotmail|outlook|yahoo)\.com\b", re.IGNORECASE),
}


def main() -> int:
    git = subprocess.run(["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
                         cwd=ROOT, check=True, capture_output=True)
    names = sorted(set(name.decode("utf-8") for name in git.stdout.split(b"\0") if name))
    failures = []
    total = 0
    for name in names:
        relative = Path(name)
        file = ROOT / relative
        if not file.is_file():
            failures.append(f"{name}: missing or unsupported publication entry")
            continue
        if (FORBIDDEN_PARTS.intersection(relative.parts) or file.suffix.lower() in FORBIDDEN_SUFFIXES
                or (file.name.startswith(".env") and file.name != ".env.example")
                or file.name.startswith("library.json")):
            failures.append(f"{name}: generated, private, or binary distribution file")
            continue
        size = file.stat().st_size
        total += size
        if size > 5 * 1024 * 1024:
            failures.append(f"{name}: exceeds the 5 MB source-file limit")
            continue
        content = file.read_bytes().decode("utf-8", errors="ignore")
        for label, pattern in PATTERNS.items():
            match = pattern.search(content)
            if match:
                line = content.count("\n", 0, match.start()) + 1
                failures.append(f"{name}:{line}: possible {label} (value withheld)")
    if failures:
        print("Publication checks failed:\n" + "\n".join(failures), file=sys.stderr)
        return 1
    print(f"Public-source checks passed: {len(names)} files, {total:,} bytes. No configured secret/privacy patterns detected.")
    print("Pattern checks complement review; they do not prove the absence of all secrets.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
