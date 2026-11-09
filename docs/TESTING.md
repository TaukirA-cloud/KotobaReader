# Verification

Run from Windows with the .NET 10 SDK and Python 3.11–3.13:

```powershell
./scripts/test.ps1
./scripts/test.ps1 -Ui
```

The first command builds the desktop app, runs 14 C# core regression checks, and
runs 11 standard-library Python worker/setup checks. `-Ui` also runs the WPF smoke
executable on an interactive Windows desktop, with its window placed off-screen.
It needs no OCR engine or translation model. Its screenshot contains illustrative
text, not a claim that inference ran.

After the explicit engine setup, run the actual language integration checks:

```powershell
./scripts/test.ps1 -Integration
```

That adds real Japanese OCR, actual local CPU inference with a known greeting,
selection-to-OCR UI behavior, translation caching, process cancellation, and worker
restart checks. Screenshots and crop images are written under `artifacts/smoke`.
Tests use a unique library directory and do not modify your normal library.

To use a project-local SDK:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.tools/cli"
./scripts/test.ps1 -Dotnet "$PWD/.tools/dotnet10/dotnet.exe" -Integration
```

## Manual release checklist

- Open a nested image folder and CBZ; verify `page2` precedes `page10`.
- Turn pages both ways, jump to an invalid page, and check first/last-page bounds.
- Resize the split panes and window; verify zoom, page fit, and width fit.
- Select the same dialogue after zooming, including dragging up and left.
- Use a vertical Japanese sample with Vertical Japanese layout.
- Change pages during OCR and cancel translation; verify no stale result appears.
- Edit recognized text during translation and confirm your edit is preserved.
- Save a note, restart the app, and revisit its page. Copy and export the note.
- Rename a library source, then open its stale entry and confirm a useful error.
- Open an empty folder, corrupt image, oversized page, and invalid CBZ.
- Try focus mode with F11 and restore it with Escape.
- Navigate input controls with the keyboard and confirm arrow keys edit text.
- Extract the published ZIP on a clean Windows machine; test reader-only startup,
  then provision the engine and repeat the demo offline.

## Coverage boundaries

Automated checks establish expected behavior on generated and adversarial fixtures.
They do not measure translation accuracy on a representative manga corpus, establish
OCR quality across fonts, verify every Windows DPI/accessibility configuration, or
replace a clean-machine installation check. CI runs core/worker/UI checks without
downloading ML models; full language integration is opt-in.
