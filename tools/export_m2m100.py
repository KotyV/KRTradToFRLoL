"""Exporte facebook/m2m100_418M (licence MIT) en ONNX pour la traduction locale de KRTradToFRLoL.

Produit dans le dossier cible :
  encoder_model.onnx / decoder_model.onnx  (+ fichiers *_data éventuels)
  sentencepiece.bpe.model, vocab.json
  krtrad-meta.json  (ids exacts des tokens de langue/spéciaux — évite tout id codé en dur côté C#)

Usage :
  pip install "optimum[exporters]" optimum-onnx transformers sentencepiece onnx onnxruntime
  python tools/export_m2m100.py [--out DIR] [--int8]

NB : depuis optimum 2.x, l'export ONNX vit dans le paquet séparé `optimum-onnx`.

--int8 : quantification dynamique (≈4x plus petit, plus rapide sur CPU, qualité quasi identique).
"""

import argparse
import json
import os
import shutil
import tempfile
from pathlib import Path

MODEL_ID = "facebook/m2m100_418M"
ONNX_FILES = ["encoder_model.onnx", "decoder_model.onnx"]


def default_out() -> Path:
    appdata = os.environ.get("APPDATA", str(Path.home()))
    return Path(appdata) / "KRTradToFRLoL" / "models" / "m2m100"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=default_out())
    parser.add_argument("--int8", action="store_true", help="quantification dynamique int8")
    args = parser.parse_args()
    out: Path = args.out
    out.mkdir(parents=True, exist_ok=True)

    from optimum.exporters.onnx import main_export
    from transformers import M2M100Tokenizer

    with tempfile.TemporaryDirectory() as tmp_str:
        tmp = Path(tmp_str)
        print(f"[1/4] Export ONNX de {MODEL_ID} (téléchargement ~1,9 Go au premier lancement)…")
        main_export(MODEL_ID, output=tmp, task="text2text-generation")

        print("[2/4] Copie des fichiers nécessaires…")
        for name in ONNX_FILES:
            for f in tmp.glob(name + "*"):  # inclut les éventuels .onnx_data externes
                shutil.copy2(f, out / f.name)

        tokenizer = M2M100Tokenizer.from_pretrained(MODEL_ID)
        tokenizer.save_pretrained(tmp / "tok")
        shutil.copy2(tmp / "tok" / "vocab.json", out / "vocab.json")
        shutil.copy2(tmp / "tok" / "sentencepiece.bpe.model", out / "sentencepiece.bpe.model")

        print("[3/4] Écriture de krtrad-meta.json…")
        meta = {
            "model": MODEL_ID,
            "srcLangId": tokenizer.get_lang_id("ko"),
            "tgtLangId": tokenizer.get_lang_id("fr"),
            "eosId": tokenizer.eos_token_id,
            "padId": tokenizer.pad_token_id,
            "unkId": tokenizer.unk_token_id,
        }
        (out / "krtrad-meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")

    if args.int8:
        print("[4/4] Quantification int8…")
        from onnxruntime.quantization import QuantType, quantize_dynamic

        for name in ONNX_FILES:
            src = out / name
            dst = out / (name + ".int8.tmp")
            quantize_dynamic(str(src), str(dst), weight_type=QuantType.QInt8)
            dst.replace(src)
            for data in out.glob(name + "_data"):
                data.unlink()  # le modèle quantifié est autonome
    else:
        print("[4/4] (pas de quantification — relancer avec --int8 si besoin)")

    print(f"Terminé. Modèles dans : {out}")
    print(json.dumps(meta, indent=2))


if __name__ == "__main__":
    main()
