# Windows setup

## Reader

Supported target: Windows 10/11 x64. Build with the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
The desktop reader uses C# and WPF and has no external NuGet packages.

```powershell
./scripts/run.ps1 -Demo
```

The script also finds a project-local SDK in `.tools/dotnet10` or `.tools/dotnet` if one is present.
With a system SDK, `dotnet run --project src/KotobaReader -c Release` works too.
The reader operates without Python or OCR installed.

## OCR and offline translation

Install **64-bit Python 3.11–3.13** from [python.org](https://www.python.org/downloads/windows/),
then run the explicit, one-time online setup from the repository root:

```powershell
./scripts/setup-engine.ps1 -InstallTesseract
```

This creates a local virtual environment, installs the pinned CTranslate2 and
SentencePiece packages, downloads a Japanese-to-English model, installs Tesseract
using Windows Package Manager, and downloads English, Japanese, and vertical
Japanese OCR data. Tesseract's installer may request administrator access.
If Tesseract is already installed, omit `-InstallTesseract`.

If `python` resolves to the Microsoft Store alias, supply the installed executable:

```powershell
./scripts/setup-engine.ps1 -Python 'C:/path/to/python.exe' -InstallTesseract
```

If Winget is unavailable, install Tesseract from the Windows distributor linked in
the [Tesseract documentation](https://tesseract-ocr.github.io/tessdoc/Downloads.html),
then run setup without `-InstallTesseract`. Its executable is usually
`C:/Program Files/Tesseract-OCR/tesseract.exe`.

Open **Settings** in the app if automatic discovery needs an override:

| Setting | Automatic default |
| --- | --- |
| Python executable | `runtime/engine/Scripts/python.exe`, then `python` on PATH |
| Tesseract executable | PATH, Program Files, or LocalAppData/Tesseract-OCR |
| Translation models | `models` beside the repository or published app |
| OCR data | `runtime/tessdata`, otherwise Tesseract's own data directory |

Click **Check engine**. The default setup should report one translation model and
three OCR languages. Then try **Demo**, drag tightly around its Japanese dialogue,
use **Single text block**, and click **Translate offline**. Selecting dialogue
automatically starts OCR; the Recognize selection button repeats it.

**Vertical Japanese** uses `jpn_vert` data and Tesseract's vertical-block layout.
**Whole page** uses automatic layout. Whole-page extraction does not reconstruct
manga bubble order; individual bubble selection usually gives better results.

### Additional language pairs

```powershell
./scripts/setup-engine.ps1 -Source ko -Target en
./scripts/setup-engine.ps1 -Source en -Target es
```

The UI lists Japanese, English, Chinese, Korean, Spanish, French, and German.
Each direction needs its own direct model and each OCR source needs its own
language data. Only SentencePiece-based Argos CTranslate2 packages are supported;
setup reports an error for incompatible formats. No automatic pivoting occurs.

### Offline operation and storage

Setup requires internet access. Reading, OCR, diagnostics, and translation do not
download anything. There are no subscriptions, accounts, API keys, telemetry, or
comic-upload services in the application. Temporary selected-page crops are deleted
after OCR. Text results, notes, and book paths remain in local library storage.

Library data: `%LOCALAPPDATA%/KotobaReader/library.json`. Saves replace the file
atomically, keep the previous version in `.bak`, and preserve malformed data as
`.damaged-*` during recovery. Back up this directory if you want to retain notes.
`KOTOBA_DATA_DIR` overrides storage for isolated testing or portable use.

The Japanese translation package is about 117 MB downloaded; the environment,
native dependencies, unpacked model, and OCR data require additional disk space.
No GPU is required. First translation loads the model; later requests reuse it.

### Troubleshooting

- **C# Dev Kit: compatible SDK not found:** run `dotnet --list-sdks` and
  `dotnet --version` from the repository. This project requires a .NET 10 SDK;
  installed runtimes alone are insufficient. A project-local SDK is found by
  `scripts/run.ps1` but is not automatically discovered by VS Code. Install the
  standard Windows SDK with `winget install --id Microsoft.DotNet.SDK.10 --exact --source winget`,
  then run **Developer: Reload Window** from VS Code's Command Palette. If you
  changed PATH, fully close and reopen VS Code first.
- **Python missing:** run setup or set the virtual-environment interpreter path.
- **DLL import failure:** use Python x64 and install the free Microsoft Visual C++
  x64 redistributable linked from [Microsoft's runtime documentation](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist).
- **No language data:** rerun `scripts/setup-ocr.ps1 -Source ja`, or set Tessdata directory.
- **No model for a pair:** install that exact source/target direction and check the engine.
- **Poor OCR:** select just text, avoid bubble edges, and change the layout. Edit
  extracted text before translating. Tiny lettering, furigana, handwriting, and
  effects are difficult for general-purpose OCR.
- **Poor translation:** local models can mistranslate names, idioms, context, and
  short utterances. Edit results before saving notes. Untranslated-source warnings
  are heuristic; fluent English is not proof of accuracy.
- **Blocked task:** Cancel or Escape terminates the worker and its child processes;
  the next request starts a fresh worker. Tasks also have a three-minute timeout.

## Publish a portable reader

```powershell
./scripts/publish.ps1
```

The publish profile bundles the .NET reader runtime into `KotobaReader.exe`, so
users do not need to install .NET or use a terminal to launch the reader. Double-click
the executable after extracting the ZIP. Keep the companion folders beside it for
translation setup and worker access. Repeated publishing preserves previous builds
by giving new output folders a timestamp suffix when necessary.

The ZIP under `artifacts` includes the reader executable, worker source, setup
scripts, and documentation. Extract it, run `scripts/setup-engine.ps1 -InstallTesseract`
to add language features, and launch `KotobaReader.exe`. Python, Tesseract, and model
weights are deliberately installed separately. The package is unsigned; this is
a portable build, not an installer or a Microsoft Store submission.
