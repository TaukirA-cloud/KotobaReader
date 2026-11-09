# Contributing

Use Windows x64 with the .NET 10 SDK and Python 3.11–3.13. Open `KotobaReader.sln`
in your editor, or launch the original demo with `./scripts/run.ps1 -Demo`.

Before opening a pull request, run:

```powershell
python scripts/check-public-source.py
./scripts/test.ps1 -Ui
```

Use `-Integration` after engine setup when changing OCR, inference, or the worker
protocol. See [testing instructions](docs/TESTING.md) and
[architecture](docs/ARCHITECTURE.md) for test boundaries and design choices.

Keep changes focused. Describe the user-visible problem, the resulting behavior,
and your validation. Add meaningful regression coverage for bug fixes, and update
setup instructions if dependencies or supported inputs change.

Use original or appropriately licensed fixtures. Do not submit personal comics,
model weights, credentials, library files, machine-specific paths, or build outputs.
Distribute executable builds through GitHub Releases rather than Git history.

For bug reports, include Windows version, app version, reproduction steps, and the
visible error. Remove personal book paths and private text from logs/screenshots.
If a problem depends on a comic file, try reproducing it with an original minimal
fixture before sharing anything.
