# KRTradToFRLoL

[![CI](https://github.com/KotyV/KRTradToFRLoL/actions/workflows/ci.yml/badge.svg)](https://github.com/KotyV/KRTradToFRLoL/actions/workflows/ci.yml)

Traduction en temps réel du chat in-game de League of Legends, du coréen vers le
français — pensé pour les streamers FR en solo queue sur le serveur coréen (type
SoloQ Challenge). App Windows **100 % externe au jeu** : capture d'écran de la zone de
chat → OCR coréen → traduction → overlay transparent. Aucune lecture mémoire, aucune
injection, aucun input simulé (compatible Vanguard).

Plan directeur : [PLAN.md](PLAN.md) · Analyse des captures réelles :
[docs/observations-captures.md](docs/observations-captures.md)

## Comment ça marche

```text
capture zone chat (4 Hz) → diff → prétraitement (gris + contraste) →
  OCR coréen spécialisé (PaddleOCR PP-OCRv5, fallback OCR Windows)
  → parsing structurel multi-locale (EN/FR/KO)
  → recollage des messages repliés → déduplication (tolérante au jitter OCR)
  → traduction en cascade :
      1. glossaire local (330+ entrées d'argot LoL vérifiées, 0 ms)
      2. cache persistant
      3. Claude Haiku en streaming (timeout dur ~2,5 s)
      4. M2M-100 local (ONNX, hors ligne) si le LLM est indisponible
      5. coréen brut (jamais bloquant)
  → overlay WPF transparent click-through, ancré visuellement par les timestamps du chat
```

L'overlay est un panneau stable (pas un calque collé aux lignes du chat, qui défilent) :
chaque traduction est préfixée du timestamp du jeu (`24:28 [Sylas] non`), qui sert
d'ancre visuelle vers la ligne d'origine.

## Prérequis

1. **Windows 11** (Windows 10 fonctionne pour ce MVP).
2. **Moteur OCR coréen** (recommandé — bien plus précis que l'OCR Windows sur le hangul
   du chat) :

   ```powershell
   pip install rapidocr onnxruntime
   python tools/export_korean_ocr.py
   ```

   À défaut, l'app retombe sur l'OCR Windows : pack de langue **한국어** requis
   (Paramètres → Langue, cocher « Reconnaissance de texte »).
3. **LoL en mode « Sans bordure »** (l'overlay ne s'affiche pas en plein écran exclusif).
4. Recommandé côté client LoL : timestamps du chat activés, Chat visibility = Everyone.
5. Une source de traduction LLM (sinon : glossaire + traduction locale uniquement) —
   voir « Clé API & sécurité ».

## Lancer

```powershell
dotnet run --project src/KRTradToFRLoL -c Release
```

1. Renseigner la clé API (ou le proxy, cf. plus bas) → **Enregistrer**.
2. **Sélectionner la zone de chat** (rectangle à la souris, coin bas-gauche du jeu).
3. **Démarrer la traduction** — le panneau Diagnostic montre l'OCR brut et chaque étage
   de traduction utilisé (`glossaire`/`cache`/`llm`/`local`/`brut`).

## Clé API & sécurité

- **Aucun secret dans le repo, jamais.** La config locale chiffre la clé et le token
  proxy via **DPAPI Windows** (`%AppData%/KRTradToFRLoL/config.json` ne contient aucun
  secret en clair, et n'est pas déchiffrable depuis une autre machine/un autre compte).
- **Distribution aux streamers : mode proxy** ([server/vercel-proxy](server/vercel-proxy)) —
  l'app appelle un relais Vercel (région Séoul) avec un token individuel révocable ;
  la clé Anthropic reste côté serveur. Config : `"ProxyUrl": "https://….vercel.app/api/translate"`.
- **Mode direct** (dev/perso) : clé locale chiffrée ou variable d'env `ANTHROPIC_API_KEY`.
- **Mode 100 % local** : avec les modèles M2M-100 installés (voir ci-dessous), l'app
  traduit sans aucune clé ni réseau (qualité moindre sur l'argot, compensée par le
  glossaire et la pré-normalisation).

## Traduction locale (optionnelle, hors ligne)

```powershell
pip install "optimum[exporters]" optimum-onnx transformers sentencepiece onnx onnxruntime
python tools/export_m2m100.py --int8
```

Exporte [facebook/m2m100_418M](https://huggingface.co/facebook/m2m100_418M) (licence MIT)
en ONNX quantifié (~500 Mo) vers `%AppData%/KRTradToFRLoL/models/m2m100`. L'app le
détecte au lancement et l'utilise en filet de secours automatique.

## Développement

```powershell
dotnet build KRTradToFRLoL.slnx     # warnings = erreurs
dotnet test KRTradToFRLoL.slnx      # pyramide : unitaires + intégration (fichiers data réels)
```

- `src/KRTradToFRLoL.Core` — logique pure testable (capture, OCR, parsing, dédup, traduction).
- `src/KRTradToFRLoL` — app WPF (overlay, sélecteur de zone, panneau de contrôle).
- `tests/KRTradToFRLoL.Tests` — xUnit ; les cas de parsing viennent de captures réelles
  de parties sur le serveur KR (clients anglais, français et coréen).
- `server/vercel-proxy` — relais API pour la distribution sans clé embarquée.
- `tools/export_korean_ocr.py` — installation du moteur OCR coréen (PaddleOCR ONNX).
- `tools/export_m2m100.py` — export du modèle de traduction locale.

La CI GitHub Actions compile (warnings = erreurs) et exécute tous les tests sur Windows.

## Limites connues

- L'OCR sur flux vidéo recompressé (VOD) reste bruité même avec le moteur spécialisé ;
  la capture native du client est le cas nominal.
- Un message replié dont l'en-tête a défilé hors de la zone capturée est perdu.
- Le chat d'équipe adverse n'est jamais visible (limitation du jeu, pas de l'app).

> KRTradToFRLoL isn't endorsed by Riot Games and doesn't reflect the views or opinions of
> Riot Games or anyone officially involved in producing or managing Riot Games properties.
