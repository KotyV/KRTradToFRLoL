# KRTradToFRLoL

[![CI](https://github.com/KotyV/KRTradToFRLoL/actions/workflows/ci.yml/badge.svg)](https://github.com/KotyV/KRTradToFRLoL/actions/workflows/ci.yml)

Traduction en temps réel du chat in-game de League of Legends, du coréen vers le
français — pensé pour les streamers FR en solo queue sur le serveur coréen (type
SoloQ Challenge). App Windows **100 % externe au jeu** : capture d'écran de la zone de
chat → OCR coréen → traduction → overlay transparent.

**Open source sous licence [MIT](LICENSE)** : tout le monde est libre de l'utiliser,
la modifier et la redistribuer.

## Streamers : ce que l'app fait — et ne fait jamais

Techniquement, l'app appartient à la même famille qu'OBS : elle regarde des pixels à
l'écran et affiche sa propre fenêtre par-dessus. Rien d'autre.

- **Jamais de lecture mémoire, d'injection, de hook du process LoL** ni de modification
  des fichiers du jeu — exactement les pratiques que Vanguard bloque.
- **Jamais le moindre input envoyé au jeu** : aucune simulation clavier/souris, aucune
  automatisation, aucun avantage en jeu. La traduction s'affiche **à côté** du chat,
  dans une fenêtre externe ; le jeu n'est pas modifié.
- Par honnêteté : Riot ne certifie aucun outil tiers (il n'existe pas d'« allow-list »
  Vanguard). L'app est simplement conçue pour rester dans la catégorie explicitement
  tolérée par la FAQ DevRel de Riot — capture d'écran externe et overlays, ce que font
  OBS et Discord sur tous les streams LoL.

Côté données, c'est ta machine qui décide :

- **Ce qui sort du PC** : uniquement les messages coréens à traduire, envoyés au service
  que tu choisis (proxy de l'event ou ta clé Anthropic). En mode 100 % local, rien.
- **Pas de télémétrie, pas de tracking.** Le seul autre accès réseau est le téléchargement,
  au premier lancement, des noms de champions et du lexique depuis **Data Dragon** (le CDN
  statique officiel de Riot, sans authentification), ensuite servi depuis le cache disque.
- **Ce qui reste sur le PC** (`%AppData%/KRTradToFRLoL/`) : la config — clé/token chiffrés
  via DPAPI Windows, jamais en clair — et un cache des traductions déjà effectuées.

## Comment ça marche

```text
capture zone chat (4 Hz) → diff → prétraitement (gris + contraste) →
  OCR coréen spécialisé (PaddleOCR PP-OCRv5, fallback OCR Windows)
  → parsing structurel multi-locale (EN/FR/KO)
  → recollage des messages repliés → déduplication (tolérante au jitter OCR)
  → traduction en cascade :
      1. glossaire local (800+ entrées d'argot LoL vérifiées, 0 ms)
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
2. **SDK .NET 10** ([télécharger](https://dotnet.microsoft.com/download/dotnet/10.0)) —
   requis pour `dotnet run`/`build`/`test` ; le format de solution `.slnx` exige un SDK
   récent (≥ 9.0.200).
3. **Moteur OCR coréen** (recommandé — bien plus précis que l'OCR Windows sur le hangul
   du chat) :

   ```powershell
   pip install rapidocr onnxruntime
   python tools/export_korean_ocr.py
   ```

   À défaut, l'app retombe sur l'OCR Windows : pack de langue **한국어** requis
   (Paramètres → Langue, cocher « Reconnaissance de texte »).
4. **LoL en mode « Sans bordure »** (l'overlay ne s'affiche pas en plein écran exclusif).
5. Recommandé côté client LoL : timestamps du chat activés, Chat visibility = Everyone.
6. Une source de traduction LLM (sinon : glossaire + traduction locale uniquement) —
   voir « Clé API & sécurité ».

## Lancer

```powershell
dotnet run --project src/KRTradToFRLoL -c Release
```

1. Renseigner la clé API (ou le proxy, cf. plus bas) → **Enregistrer**.
2. **Sélectionner la zone de chat** (rectangle à la souris, coin bas-gauche du jeu).
3. **Démarrer la traduction** — le panneau Diagnostic montre l'OCR brut et chaque étage
   de traduction utilisé (`glossaire`/`cache`/`llm`/`local`/`copie`/`brut`).

## Clé API & sécurité

- **Aucun secret dans le repo, jamais.** La config locale chiffre la clé et le token
  proxy via **DPAPI Windows** (`%AppData%/KRTradToFRLoL/config.json` ne contient aucun
  secret en clair, et n'est pas déchiffrable depuis une autre machine/un autre compte).
- **Distribution aux streamers : mode proxy** ([server/vercel-proxy](server/vercel-proxy)) —
  l'app appelle un relais Vercel (région Séoul) avec un token individuel révocable ;
  la clé Anthropic reste côté serveur. Config : `"ProxyUrl": "https://….vercel.app/api/translate"`.
- **Mode direct** (dev/perso) : clé locale chiffrée ou variable d'env `ANTHROPIC_API_KEY`.
- **Mode 100 % local** : avec les modèles M2M-100 installés (voir ci-dessous), la
  traduction fonctionne sans clé ni réseau (qualité moindre sur l'argot, compensée par
  le glossaire et la pré-normalisation). Seul le lexique Data Dragon est téléchargé au
  premier lancement puis mis en cache ; sans réseau du tout, l'app reste fonctionnelle,
  le parsing devient simplement plus permissif.

## Traduction locale (optionnelle, hors ligne)

```powershell
pip install "optimum[exporters]" optimum-onnx transformers sentencepiece onnx onnxruntime
python tools/export_m2m100.py --int8
```

Exporte [facebook/m2m100_418M](https://huggingface.co/facebook/m2m100_418M) (licence MIT)
en ONNX quantifié (~500 Mo) vers `%AppData%/KRTradToFRLoL/models/m2m100`. L'app le
détecte au lancement et l'utilise en filet de secours automatique.

## Distribution aux streamers (zéro installation)

```powershell
./tools/make-portable.ps1 -SingleExe   # un seul fichier KRTradToFRLoL.exe (~74 Mo)
./tools/make-portable.ps1 -Zip         # zip complet, avec les modèles s'ils sont installés
```

- **`-SingleExe`** : un unique `.exe` à envoyer tel quel — app, runtime .NET et données
  embarqués (extraits dans un cache au premier lancement ; démarrages suivants
  instantanés). L'OCR utilise le moteur Windows (pack de langue coréen requis), sauf si
  un dossier `models/` est posé à côté de l'exe.
- **`-Zip`** : dossier complet incluant les modèles OCR coréen et M2M-100 s'ils sont
  installés sur la machine de build (~600 Mo tout inclus) — la meilleure qualité OCR et
  la traduction hors ligne, sans aucune étape côté streamer.

Dans les deux cas : **aucun .NET requis**, l'app cherche les modèles d'abord dans
`models/` à côté de l'exe puis dans `%AppData%/KRTradToFRLoL/models`, et la config/le
cache du streamer restent dans `%AppData%`. L'exe n'étant pas signé, Windows SmartScreen
peut afficher un avertissement au premier lancement (« Informations complémentaires →
Exécuter quand même »).

## Développement

```powershell
dotnet build KRTradToFRLoL.slnx     # warnings = erreurs
dotnet test KRTradToFRLoL.slnx      # pyramide : unitaires + intégration (fichiers data réels)
```

- `src/KRTradToFRLoL.Core` — logique pure testable (capture, OCR, parsing, dédup, traduction).
- `src/KRTradToFRLoL` — app WPF (overlay, sélecteur de zone, panneau de contrôle).
- `tests/KRTradToFRLoL.Tests` — xUnit ; les cas de parsing viennent de captures réelles
  de parties sur le serveur KR (clients anglais, français et coréen — pseudos anonymisés).
- `server/vercel-proxy` — relais API pour la distribution sans clé embarquée.
- `tools/export_korean_ocr.py` — installation du moteur OCR coréen (PaddleOCR ONNX).
- `tools/export_m2m100.py` — export du modèle de traduction locale.
- `tools/make-portable.ps1` — assemble le zip portable de distribution.

La CI GitHub Actions compile (warnings = erreurs) et exécute la suite de tests sur
Windows. Les tests d'intégration OCR/M2M-100 passent en no-op sur la CI (modèles non
installés) ; ils s'exécutent réellement en local après les scripts `tools/export_*.py`.

Docs internes : [docs/observations-captures.md](docs/observations-captures.md) (analyse
des captures réelles, pseudos anonymisés) · [PLAN.md](PLAN.md) (document de conception
initial, juin 2026 — partiellement dépassé, le code et ce README font foi).

## Limites connues

- L'OCR sur flux vidéo recompressé (VOD) reste bruité même avec le moteur spécialisé ;
  la capture native du client est le cas nominal.
- Un message replié dont l'en-tête a défilé hors de la zone capturée perd son auteur et
  sa traduction : la ligne orpheline est ignorée (ou, si « Tout afficher » est coché et
  qu'elle est assez longue et majoritairement coréenne, affichée comme ligne système).
- Le chat d'équipe adverse n'est jamais visible (limitation du jeu, pas de l'app).

## Licence

[MIT](LICENSE) — utilisation, modification et redistribution libres, y compris
commerciales, tant que la notice de licence est conservée.

> KRTradToFRLoL isn't endorsed by Riot Games and doesn't reflect the views or opinions of
> Riot Games or anyone officially involved in producing or managing Riot Games properties.
