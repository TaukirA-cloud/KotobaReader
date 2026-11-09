import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile
from unittest.mock import patch

WORKER = Path(__file__).resolve().parents[1] / "worker" / "engine.py"
spec = importlib.util.spec_from_file_location("engine", WORKER)
engine = importlib.util.module_from_spec(spec)
spec.loader.exec_module(engine)
install_spec = importlib.util.spec_from_file_location("install_model", WORKER.parent / "install_model.py")
installer = importlib.util.module_from_spec(install_spec)
install_spec.loader.exec_module(installer)


class EngineTests(unittest.TestCase):
    def test_model_inventory_ignores_incomplete_models(self):
        with tempfile.TemporaryDirectory() as folder:
            model = Path(folder) / "ja_en"
            model.mkdir()
            (model / "metadata.json").write_text('{"from_code":"ja","to_code":"en"}')
            self.assertEqual(engine.model_inventory(folder), [])
            (model / "model").mkdir()
            (model / "model/model.bin").write_bytes(b"model")
            (model / "sentencepiece.model").write_bytes(b"tokenizer")
            self.assertEqual(engine.model_inventory(folder)[0]["source"], "ja")

    def test_model_identity_changes_on_replacement(self):
        with tempfile.TemporaryDirectory() as folder:
            model = Path(folder) / "ja_en"
            (model / "model").mkdir(parents=True)
            (model / "metadata.json").write_text('{"from_code":"ja","to_code":"en"}')
            (model / "model/model.bin").write_bytes(b"one")
            (model / "sentencepiece.model").write_bytes(b"tokenizer")
            before = engine.model_inventory(folder)[0]["id"]
            (model / "model/model.bin").write_bytes(b"different model")
            self.assertNotEqual(before, engine.model_inventory(folder)[0]["id"])

    def test_missing_language_pair_has_actionable_error(self):
        with self.assertRaisesRegex(ValueError, "setup-engine.ps1"):
            engine.translate({"text": "Hello", "source": "en", "target": "ja", "config": {"models": "missing"}})

    def test_text_limits(self):
        for text in ("", " " * 10, "x" * 8001):
            with self.assertRaisesRegex(ValueError, "8,000"):
                engine.translate({"text": text})

    def test_ocr_uses_argument_list_and_vertical_japanese_data(self):
        with tempfile.NamedTemporaryFile(suffix=".png") as image:
            with patch.object(engine, "tesseract_path", return_value="tesseract.exe"), patch.object(engine, "run_tesseract", return_value="こん にちは\n世界") as run:
                result = engine.recognize({"image": image.name, "source": "ja", "layout": "Vertical", "config": {}})
            self.assertIn("jpn_vert", run.call_args.args[0])
            self.assertEqual(result["text"], "こんにちは世界")

    def test_ocr_missing_image_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "missing"):
            engine.recognize({"image": "missing-file.png"})

    def test_identity_translation_needs_no_model(self):
        self.assertEqual(engine.translate({"text": "Hello", "source": "en", "target": "en"})["text"], "Hello")

    def test_protocol_recovers_after_bad_request_and_preserves_unicode(self):
        requests = '\n'.join(["{malformed", json.dumps({"id": "2", "operation": "translate", "text": "こんにちは", "source": "ja", "target": "ja"})]) + '\n'
        completed = subprocess.run([sys.executable, str(WORKER)], input=requests,
                                   capture_output=True, encoding="utf-8", timeout=10)
        responses = [json.loads(line) for line in completed.stdout.splitlines()]
        self.assertFalse(responses[0]["ok"])
        self.assertTrue(responses[1]["ok"])
        self.assertEqual(responses[1]["result"]["text"], "こんにちは")

    def test_unknown_operation_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Unknown"):
            engine.dispatch({"operation": "download"})

    def test_model_installer_rejects_archive_traversal(self):
        with tempfile.TemporaryDirectory() as folder:
            archive = io.BytesIO()
            with zipfile.ZipFile(archive, "w") as zipped:
                zipped.writestr("../outside.txt", "unsafe")
            archive.seek(0)
            with zipfile.ZipFile(archive) as zipped:
                with self.assertRaisesRegex(ValueError, "Unsafe"):
                    installer.validate_members(zipped, Path(folder) / "unpacked")

    def test_model_installer_accepts_contained_paths(self):
        with tempfile.TemporaryDirectory() as folder:
            archive = io.BytesIO()
            with zipfile.ZipFile(archive, "w") as zipped:
                zipped.writestr("ja_en/metadata.json", "{}")
            archive.seek(0)
            with zipfile.ZipFile(archive) as zipped:
                installer.validate_members(zipped, Path(folder) / "unpacked")


if __name__ == "__main__":
    unittest.main()
