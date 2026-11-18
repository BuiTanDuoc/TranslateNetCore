#!/usr/bin/env python3
"""
Tải model MarianMT (Helsinki-NLP OPUS-MT) từ Hugging Face và export sang ONNX cho NetTranslate.

Mỗi cặp ngôn ngữ được ghi vào models/<src>-<tgt>/ gồm:
    encoder_model.onnx, decoder_model.onnx, decoder_with_past_model.onnx,
    source.spm, target.spm, vocab.json, config.json, generation_config.json, model.json

Cách dùng:
    pip install -r scripts/requirements.txt
    python scripts/download_models.py                      # các cặp mặc định
    python scripts/download_models.py en-vi vi-en          # chỉ các cặp chỉ định
    python scripts/download_models.py --repo Helsinki-NLP/opus-mt-tc-big-en-ko --pair en-ko
    python scripts/download_models.py en-vi --quantize     # thêm bản int8 (*_quantized.onnx)
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
import tempfile
from pathlib import Path

# Cặp mặc định: tiếng Việt <-> Anh, và tiếng Anh <-> các ngôn ngữ phổ biến (dịch chéo đi qua tiếng Anh)
DEFAULT_PAIRS = [
    "en-vi", "vi-en",
    "en-fr", "fr-en",
    "en-de", "de-en",
    "en-es", "es-en",
    "en-zh", "zh-en",
    "ja-en",
    "ko-en",
]

# Cặp cần repo khác tên mặc định opus-mt-<src>-<tgt> (thêm vào đây nếu cần)
REPO_OVERRIDES: dict[str, str] = {}

# Model đa ngôn ngữ đích cần token đầu câu chọn ngôn ngữ (xem model card trên Hugging Face)
TARGET_PREFIXES = {
    "en-zh": ">>cmn_Hans<<",
}

ROOT = Path(__file__).resolve().parent.parent
REQUIRED_FILES = ["encoder_model.onnx", "decoder_model.onnx", "source.spm", "vocab.json"]


def export_pair(pair: str, repo: str, out_dir: Path, quantize: bool, force: bool) -> None:
    from optimum.exporters.onnx import main_export

    if out_dir.exists() and all((out_dir / f).exists() for f in REQUIRED_FILES) and not force:
        print(f"[skip] {pair}: already exists in {out_dir}")
        return

    print(f"[export] {pair} <- {repo}")
    with tempfile.TemporaryDirectory() as tmp:
        tmp_path = Path(tmp)
        # --task ...-with-past + no_post_process: giữ decoder_model.onnx và decoder_with_past_model.onnx
        # tách riêng (NetTranslate không dùng decoder_model_merged.onnx).
        main_export(
            model_name_or_path=repo,
            output=tmp_path,
            task="text2text-generation-with-past",
            no_post_process=True,
        )

        missing = [f for f in REQUIRED_FILES if not (tmp_path / f).exists()]
        if missing:
            raise RuntimeError(f"Export of {repo} is missing files: {missing}")

        if quantize:
            from onnxruntime.quantization import QuantType, quantize_dynamic

            for name in ["encoder_model", "decoder_model", "decoder_with_past_model"]:
                src = tmp_path / f"{name}.onnx"
                if src.exists():
                    print(f"  [quantize] {name}")
                    quantize_dynamic(str(src), str(tmp_path / f"{name}_quantized.onnx"), weight_type=QuantType.QInt8)

        src_lang, tgt_lang = pair.split("-", 1)
        (tmp_path / "model.json").write_text(
            json.dumps({"source": src_lang, "target": tgt_lang, "repo": repo}, indent=2), encoding="utf-8"
        )

        if out_dir.exists():
            shutil.rmtree(out_dir)
        out_dir.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(tmp_path, out_dir)

    print(f"[done] {pair} -> {out_dir}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Download & export OPUS-MT models to ONNX for NetTranslate")
    parser.add_argument("pairs", nargs="*", help="Language pairs like en-vi (default: a common set)")
    parser.add_argument("--repo", help="Custom Hugging Face repo (use with --pair)")
    parser.add_argument("--pair", help="Language pair code for --repo, e.g. en-ko")
    parser.add_argument("--target-prefix", help="Target token for multilingual models, e.g. '>>vie<<'")
    parser.add_argument("--out", default=str(ROOT / "models"), help="Models directory (default: ./models)")
    parser.add_argument("--quantize", action="store_true", help="Also create int8 *_quantized.onnx files")
    parser.add_argument("--force", action="store_true", help="Re-export even if the folder exists")
    args = parser.parse_args()

    out_root = Path(args.out)
    jobs: list[tuple[str, str]] = []

    if args.repo:
        if not args.pair:
            parser.error("--repo requires --pair")
        jobs.append((args.pair, args.repo))
    else:
        for pair in args.pairs or DEFAULT_PAIRS:
            if "-" not in pair:
                parser.error(f"Invalid pair '{pair}', expected format like en-vi")
            jobs.append((pair, REPO_OVERRIDES.get(pair, f"Helsinki-NLP/opus-mt-{pair}")))

    failed = []
    for pair, repo in jobs:
        try:
            out_dir = out_root / pair
            export_pair(pair, repo, out_dir, args.quantize, args.force)
            prefix = args.target_prefix or TARGET_PREFIXES.get(pair)
            if prefix:
                manifest = out_dir / "model.json"
                data = json.loads(manifest.read_text(encoding="utf-8"))
                data["targetPrefix"] = prefix
                manifest.write_text(json.dumps(data, indent=2), encoding="utf-8")
        except Exception as ex:  # noqa: BLE001
            print(f"[error] {pair}: {ex}", file=sys.stderr)
            failed.append(pair)

    if failed:
        print(f"\nFailed: {', '.join(failed)}", file=sys.stderr)
        return 1
    print("\nAll done. Restart NetTranslate or call POST /admin/models/reload.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
