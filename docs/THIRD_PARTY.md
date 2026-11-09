# Dependencies, attribution, and model provenance

KotobaReader's source is MIT-licensed. That license does not replace the licenses
of engines, native dependencies, trained models, datasets, or personal comic files.

| Component | Role | Upstream |
| --- | --- | --- |
| .NET / WPF | Desktop runtime and UI | [dotnet/wpf](https://github.com/dotnet/wpf), MIT |
| Python | Local worker host | [Python licensing](https://docs.python.org/3/license.html), PSF terms |
| CTranslate2 4.7.1 | Local neural inference | [OpenNMT/CTranslate2](https://github.com/OpenNMT/CTranslate2), MIT |
| SentencePiece 0.2.1 | Tokenization | [google/sentencepiece](https://github.com/google/sentencepiece), Apache-2.0 |
| Tesseract | OCR executable | [tesseract-ocr/tesseract](https://github.com/tesseract-ocr/tesseract), Apache-2.0 |
| tessdata_fast 4.1.0 | OCR language data | [tesseract-ocr/tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast), Apache-2.0 |
| Argos package index | Model discovery | [argosopentech/argospm-index](https://github.com/argosopentech/argospm-index) |

The Python environment also installs CTranslate2's transitive dependencies, including
NumPy, PyYAML, and setuptools. Their distributions retain their own licenses. Native
Tesseract installers include additional libraries and license notices.

## Initial Japanese→English model

- Package: `translate-ja_en-1_1.argosmodel`, package version 1.1.
- Index: `https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json`.
- Official fallback used during development: `https://data.argosopentech.com/argospm/v1/translate-ja_en-1_1.argosmodel`.
- Archive SHA-256: `623e3477959a815eb0a5ef53e09079ae8f1f9d3bbcd230473baf28c03fb83335`.

The installer verifies the pinned checksum for this package, retains the package's
README and other files, and writes `provenance.json` with its URL, metadata, and
computed checksum. Other model pairs record checksums but are not independently
pinned in source. OCR downloads record their source URL and checksum as well.

The Japanese package README credits OPUS, Wiktionary/Wiktextract, Stanza, CCAligned,
WikiMatrix, and OpenSubtitles. It does not provide a complete standalone model
redistribution license. KotobaReader therefore does **not** include model weights,
OCR binaries, or Python environments in its repository or reader ZIP. The setup
script fetches them from upstream for the user's local use. Verify each upstream
package's terms before redistributing a bundle that includes them.

The generated *Little Explorer* demo is original project artwork drawn with WPF;
it does not contain pages from commercial manga. Do not commit personal comics or
unlicensed images to this repository.
