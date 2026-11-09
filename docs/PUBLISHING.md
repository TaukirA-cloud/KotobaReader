# Publishing and releases

Repository: [TaukirA-cloud/KotobaReader](https://github.com/TaukirA-cloud/KotobaReader).

The public repository contains source, documentation, tests, workflow definitions,
and the original demo screenshot. It excludes personal comics and library data,
credentials, model weights, local environments, and generated executables.

## Before pushing

```powershell
python scripts/check-public-source.py
./scripts/test.ps1 -Integration
git diff --check
```

Use `-Ui` if local OCR/model dependencies are unavailable; it runs the same reader
smoke checks used in CI without claiming full translation integration. Inspect
`git diff --cached` before committing. The source scanner also checks staged files
that match ignore rules, but its pattern scan is not a guarantee that all sensitive
data is absent. Use a GitHub no-reply commit email if you want to keep your personal
address private.

## Build a preview release

Update the project version, changelog, and `docs/RELEASE_NOTES.md`, verify CI on
`main`, and create a matching tag, for example:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The Windows release workflow checks the source, reruns core/worker/UI checks, builds
the self-contained reader executable, and publishes a **prerelease** with a ZIP and
SHA-256 digest. It uses GitHub's automatically scoped workflow token; no personal
token is embedded in source. Only the release job receives repository write access.

The executable and runtime are bundled into one EXE. The ZIP also includes the
worker scripts, setup instructions, and attribution. Do not upload the local Python
environment or trained models. Review [third-party terms](THIRD_PARTY.md) before
changing what the release distributes.

The root `KotobaReader.exe` is a local convenience copy and is ignored by Git.
Regenerate it from a newly published build when needed; users obtain releases
through the Releases page, not the source ZIP.

## Repository settings

Suggested description: `Native Windows manga reader with local OCR, offline CPU translation, bookmarks, and translation notes.`

Suggested topics: `csharp`, `dotnet`, `wpf`, `windows`, `manga-reader`, `comic-reader`,
`ocr`, `tesseract`, `offline-translation`, `ctranslate2`.

Enable GitHub's secret scanning/push protection and private vulnerability reporting
when available. Keep CI read-only for pull requests. Dependabot is configured to
propose updates to GitHub Actions and the worker's pinned Python dependencies.
