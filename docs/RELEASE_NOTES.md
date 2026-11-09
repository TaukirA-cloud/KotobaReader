# KotobaReader — Windows preview

A native Windows comic and manga reader with local OCR, offline neural translation,
bookmarks, saved reading progress, and page-linked translation notes.

## Download and launch

Download `KotobaReader-win-x64.zip`, extract it, and double-click `KotobaReader.exe`.
The .NET runtime is included. Keep the companion folders next to the executable.
Use Demo to try the original sample comic without importing your own files.

OCR and translation need one-time setup using Python 3.11–3.13 x64:

```powershell
./scripts/setup-engine.ps1 -InstallTesseract
```

Setup downloads dependencies and a Japanese-to-English model. Reading and language
inference then run locally without paid APIs. See `docs/SETUP.md` in the download
for manual installation and custom paths. OCR binaries and models are not bundled.

The companion `.sha256` file records the ZIP's SHA-256 digest. Compare it with
`Get-FileHash ./KotobaReader-win-x64.zip -Algorithm SHA256` after downloading.

## Preview limits

The executable is unsigned. OCR and translation quality vary with lettering and
context; both text fields are editable. PDF/CBR support, automatic bubble detection,
and replacement lettering on artwork are not implemented. This is an early
portfolio release, not a professional translation service.
