"""Prépare le moteur OCR coréen de KRTradToFRLoL (reconnaissance seule).

Récupère le modèle korean_PP-OCRv5_rec_mobile DÉJÀ converti en ONNX via le hub de
modèles RapidOCR (ModelScope), extrait le dictionnaire embarqué dans les métadonnées
ONNX, et installe le tout dans le dossier modèles de l'app. La détection de lignes est
faite côté C# par projection horizontale (le chat LoL a des lignes régulières) : seul
le modèle de reconnaissance (~13 Mo) est nécessaire.

Usage :
  pip install rapidocr onnxruntime
  python tools/export_korean_ocr.py [--out DIR]

Produit : rec.onnx, charset.txt, krtrad-ocr-meta.json dans le dossier cible.
"""

import argparse
import json
import os
import shutil
from pathlib import Path

import onnxruntime as ort


def default_out() -> Path:
    appdata = os.environ.get("APPDATA", str(Path.home()))
    return Path(appdata) / "KRTradToFRLoL" / "models" / "ocr-ko"


def download_via_rapidocr() -> Path:
    """Laisse RapidOCR télécharger le modèle coréen, puis renvoie son chemin."""
    from rapidocr import EngineType, LangRec, ModelType, OCRVersion, RapidOCR

    RapidOCR(params={
        "Rec.engine_type": EngineType.ONNXRUNTIME,
        "Rec.lang_type": LangRec.KOREAN,
        "Rec.model_type": ModelType.MOBILE,
        "Rec.ocr_version": OCRVersion.PPOCRV5,
    })

    import rapidocr
    models_dir = Path(rapidocr.__file__).parent / "models"
    candidates = sorted(models_dir.glob("korean_*rec*.onnx"))
    if not candidates:
        raise SystemExit(f"Modèle coréen introuvable dans {models_dir}")
    return candidates[-1]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=default_out())
    args = parser.parse_args()
    out: Path = args.out
    out.mkdir(parents=True, exist_ok=True)

    src = download_via_rapidocr()
    print(f"Modèle : {src.name}")

    session = ort.InferenceSession(str(src), providers=["CPUExecutionProvider"])
    charset_raw = session.get_modelmeta().custom_metadata_map.get("character")
    if not charset_raw:
        raise SystemExit("Dictionnaire absent des métadonnées ONNX — modèle inattendu.")
    chars = charset_raw.splitlines()
    has_space = chars[-1] in (" ", "")

    shutil.copy2(src, out / "rec.onnx")
    (out / "charset.txt").write_text("\n".join(chars), encoding="utf-8")
    (out / "krtrad-ocr-meta.json").write_text(json.dumps({
        "model": src.stem,
        "inputHeight": 48,
        "maxWidth": 1024,
        "appendSpace": not has_space,
    }, indent=2), encoding="utf-8")
    print(f"Terminé : {out} ({len(chars)} caractères, appendSpace={not has_space})")


if __name__ == "__main__":
    main()
