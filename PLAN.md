# KRTradToFRLoL — Traducteur temps réel du chat LoL (Coréen → Français)

> Application Windows pour streamers FR en rush solo queue en Corée (type SoloQ Challenge Corée) :
> lit le chat in-game de League of Legends, traduit le coréen en français et l'affiche en quasi
> temps réel.
>
> **Plan v2 — 10 juin 2026.** Chaque affirmation structurante a été vérifiée puis contre-vérifiée
> par recherche web (rapport complet : `docs/recherche-rapport-2026-06-10.json` ;
> observations des captures réelles : `docs/observations-captures.md`).

> ⚠️ **Document d'époque, conservé pour référence — le code et le [README](README.md) font
> foi.** Le produit a évolué depuis la rédaction : capture GDI/BitBlt 4 Hz sur zone d'écran
> (WGC `CreateForWindow` reste la cible v2), fallback de traduction **local M2M-100 ONNX**
> (les pistes Google Cloud Translation / Azure Translator ont été abandonnées), timeout LLM
> 2,5 s, .NET 10, pipeline OCR ONNX maison installé via `tools/export_korean_ocr.py` (pas de
> lib RapidOcrNet ni de modèles embarqués), glossaire 800+ entrées. L'essentiel des
> phases 0 à 2 est livré, plus une partie de la phase 3 (CI, proxy de distribution,
> auto-recovery, écran de diagnostic) — pas encore de source OBS, d'installeur ni de
> détection de partie automatique. Les cases à cocher du §6 n'ont pas été tenues à jour.

---

## 1. Contraintes fondamentales

### 1.1 Vanguard (anti-cheat kernel) — ligne rouge absolue
- Actif sur LoL depuis le patch 14.9 (1er mai 2024). **Interdit** : lecture mémoire, injection
  DLL, hook du process, modification des fichiers du jeu. FAQ DevRel officielle : « External
  tools reading memory will no longer work ».
- **Toléré** : capture d'écran externe (OBS le fait sur tous les streams LoL), fenêtre overlay
  externe topmost, outils basés sur les API officielles. FAQ : « Overlays and internal tools
  using the API… should continue to function ».
- **Nuances vérifiées** (importantes) :
  - Vanguard **peut voir notre overlay** (il capture l'écran pour inspecter les overlays). La
    protection vient de la **politique** Riot (pas d'avantage mesurable, pas d'automatisation),
    pas d'une invisibilité technique. → **L'app n'émet JAMAIS le moindre input vers le jeu**
    (pas de simulation clavier/souris, même pour ouvrir le chat — c'est au joueur de le faire).
  - **Blocage ≠ ban** : Vanguard *bloque* les logiciels incompatibles (erreurs VAN), il ne
    bannit que les cheats détectés. Faux positifs < 1/10 000, levés en < 72 h.
  - **Il n'existe AUCUNE allow-list Vanguard** (« absolutely no allow list », FAQ DevRel). La
    sécurisation = conformité architecturale + validation écrite (cf. §6).
  - Notre stack ne doit embarquer **aucun driver kernel tiers** (Vanguard bloque p. ex.
    RTCore64.sys de MSI Afterburner) → API Windows natives uniquement.
- Conséquence : « remplacer le texte dans le jeu » est impossible. On affiche la traduction
  **à côté** (overlay externe et/ou source OBS).

### 1.2 Aucune API Riot n'expose le chat in-game (vérifié juin 2026)
- **Live Client Data API** (port 2999) : 12 endpoints, tous gameplay, **aucun chat**.
- **LCU API** : chat de lobby/champ select seulement — et surtout **interdite aux joueurs en
  Corée** (politique Riot de janv. 2019 toujours en vigueur) → à n'utiliser sous aucun prétexte
  pendant l'event.
- **Replays .rofl** : paquets obfusqués, pas de chat exploitable, disponibles après la partie.
- **MAIS** — découverte de la contre-vérification : la plateforme **Overwolf expose
  officiellement le chat in-game de LoL** via son Game Events Provider (« chat: All the chat
  messages sent in a match », format `[Team] Pseudo (Lux): Hello`, usage temps réel uniquement,
  interdiction de logger le contenu). C'est ainsi que fonctionne **EzChat.gg** (traducteur de
  chat LoL existant sur Overwolf, sans OCR). → L'OCR n'est PAS la seule voie. Cf. §2.

### 1.3 Mode d'affichage du jeu
- L'overlay ne s'affiche pas par-dessus le « Plein écran exclusif » → jeu en **Sans bordure
  (Borderless)**, le standard streamer. L'app détecte le mode et alerte si fullscreen exclusif.
- **Windows 11 requis** sur les machines de l'event : seule version où la bordure jaune de la
  capture WGC est supprimable (`IsBorderRequired = false`) et où `MinUpdateInterval` permet de
  throttler la capture.

### 1.4 Réglages client à verrouiller (onboarding)
- **Chat visibility = Everyone** (sinon le /all ennemi est invisible ; le chat d'équipe adverse
  n'est jamais visible, c'est structurel).
- **Show Timestamps = ON** → clé de dédoublonnage naturelle (cf. captures).
- **Chat Scale** (curseur dédié, distinct du HUD Scale) + position `ChatX/ChatY` dans
  `Config/PersistedSettings.json` : figés sur les machines de l'event → zone de chat
  **déterministe** pour la capture. (On lit ce fichier, on ne le modifie jamais en jeu.)
- Recommander aux joueurs de garder l'**historique de chat étendu (touche Z)** : neutralise le
  fade-out (~10 s, non documenté — à mesurer) et stabilise le fond pour l'OCR.
- Depuis le patch 13.4, **plus de code couleur universel** allié/ennemi → le parsing s'appuie
  sur les **préfixes texte** (`[Team]/[Équipe]/[All]/[Tous]`), pas sur la couleur.
- Le client peut être **localisé en français** (vu sur les captures réelles : `[Équipe]`,
  « Maître Yi ») → parsing multi-locale, noms de champions par locale via **Data Dragon**.

---

## 2. Décision d'architecture n°1 : la source du chat (deux voies)

Le pipeline aval (parsing → traduction → affichage) est identique ; la source est un module
interchangeable (`IChatSource`). Deux implémentations possibles :

| | **Voie A — Overwolf GEP** | **Voie B — Capture + OCR (indépendante)** |
|---|---|---|
| Principe | Events `chat` du Game Events Provider Overwolf (texte brut exact) | Capture WGC de la zone chat + OCR coréen local |
| Qualité du texte | **Parfaite** (zéro erreur OCR) | ~88-95 % à valider sur captures réelles |
| Latence acquisition | ~0 ms | ~80-200 ms |
| Risque technique | Faible (EzChat.gg le prouve en prod) | Moyen (OCR hangul 12-18 px = le maillon faible) |
| Dépendances | Plateforme Overwolf (ou ow-electron), approbation Riot via le process Overwolf, **interdiction de logger le contenu du chat** | Aucune (100 % autonome, API Windows natives) |
| Contrôle | Limité (plateforme tierce, GEP peut changer) | Total |

**Recommandation : dual-track en phase 1, décision au gate.**
- La voie A est probablement la meilleure pour la **solidité broadcast** (texte exact, latence
  quasi nulle) — à prototyper en premier, et vérifier que le GEP `chat` fonctionne sur le
  serveur KR avec des messages en hangul.
- La voie B reste indispensable comme **plan d'indépendance** (pas de dépendance Overwolf,
  distribution sous notre marque) et comme fallback si l'approbation Overwolf/Riot traîne.
- **Action immédiate (jour 1) : tester EzChat.gg tel quel** (Overwolf, 12K téléchargements,
  4,3/5). S'il traduit déjà KR→FR correctement, c'est le plan C de secours instantané — et dans
  tous les cas un étalon de comparaison. (Ne pas s'inspirer de « League Chat Translator » :
  noté 1/5, modèle à pubs in-game probablement non conforme depuis l'interdiction de mai 2025.)

---

## 3. Architecture générale (voie B illustrée ; voie A remplace les 3 premiers blocs)

```
┌──────────────────────────────────────────────────────────────────────┐
│                        KRTradToFRLoL (.NET 8)                         │
│                                                                       │
│  ┌──────────┐   ┌───────────┐   ┌─────────┐   ┌──────────────┐        │
│  │ Capture  │──▶│ Diff/hash │──▶│   OCR   │──▶│ Parsing +    │        │
│  │ WGC      │   │ de zone   │   │ coréen  │   │ déduplication│        │
│  │ (fenêtre │   │ (debounce │   │ (ONNX)  │   └──────┬───────┘        │
│  │  LoL)    │   │ 100-150ms)│   └─────────┘          │                │
│  └──────────┘   └───────────┘                        ▼                │
│   10-15 Hz                                   ┌──────────────┐        │
│                                              │ Traduction   │        │
│  ┌───────────────┐   ┌────────────┐          │ glossaire →  │        │
│  │ Overlay WPF   │◀──│  Bus de    │◀─────────│ cache → LLM  │        │
│  │ (streamer)    │   │  messages  │          │ → NMT fallback│        │
│  └───────────────┘   └─────┬──────┘          └──────────────┘        │
│                            ▼                                          │
│                  ┌──────────────────┐                                 │
│                  │ Serveur WebSocket │──▶ Source navigateur OBS       │
│                  │ local (Kestrel)   │    (les viewers voient la      │
│                  └──────────────────┘     traduction sur le stream)   │
└──────────────────────────────────────────────────────────────────────┘
```

### Budget latence (voie B, du message à l'écran)

| Étape | Cible | Vérifié |
|---|---|---|
| Capture WGC (fenêtre, GPU) | ~16 ms (1 frame) | oui |
| Diff/hash de la zone | ~1 ms | — |
| OCR (PP-OCRv5 mobile, zone croppée) | 60–150 ms (×1,5-2 sous charge jeu+OBS) | bench bloquant |
| Parsing + dédup | ~1 ms | — |
| Traduction glossaire/cache | 0 ms | — |
| Traduction LLM (Haiku 4.5, streaming) | ~1,0–1,3 s (TTFT 0,75 s + ~93 tok/s) | oui (benchs publics) |
| Traduction NMT fallback (Google v3) | 50–200 ms | oui |
| Affichage overlay/WS | ~15 ms | — |

Total pire cas ≈ 1,5 s (LLM), cas courant bien moindre (glossaire/cache/NMT). En voie A,
retrancher ~100-200 ms d'acquisition. L'overlay affiche le texte LLM **en streaming** (premiers
mots à ~0,9 s).

---

## 4. Choix techniques

### 4.1 Stack : **C#/.NET 8 + WPF** (vérifié — pas WinUI 3)
- **WPF** : `AllowsTransparency` + styles Win32 `WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW`
  + `HWND_TOPMOST` ré-asserté périodiquement (timer 1-2 s, robustesse broadcast).
  **WinUI 3 écarté** : transparence click-through toujours non supportée (issues #7276/#2515).
  **Electron écarté** (200-300 Mo RAM pendant un live). **Tauri v2** = seul plan B crédible.
- **Capture : Windows.Graphics.Capture en `CreateForWindow` sur la fenêtre LoL** (jamais le
  moniteur) : pas de boucle de feedback avec notre overlay (capture le contenu même recouvert),
  frames D3D11 GPU, crop GPU de la zone chat, throttle 10-15 fps via `MinUpdateInterval`.
  Gérer : fenêtre minimisée (flux gelé), perte de device D3D (TDR, alt-tab, changement de
  résolution) → auto-restart du pipeline. DXGI Desktop Duplication écarté (régression 24H2,
  capture moniteur entier). GDI écarté (CPU-bound).
- Distribution : **Velopack** (installeur 1-clic + auto-update delta), **exe signé**
  (un binaire non signé qui capture l'écran = alertes antivirus).

### 4.2 OCR — le maillon critique (voie B)
- **Principal : PaddleOCR PP-OCRv5 « korean » via ONNX Runtime en C#** — lib `RapidOcrNet`
  (NuGet, .NET 8, Apache-2.0, AOT) avec `korean_PP-OCRv5_mobile_rec` (88,0 % de précision
  ligne sur le benchmark coréen officiel, +65 pts vs v3) + `PP-OCRv5_mobile_det` (4,7 Mo,
  ~58 ms CPU officiel). Modèles embarqués dans l'app → zéro installation sur les PC de l'event.
  Nuance vérifiée : RapidOCR-ONNX a été mesuré 2-3× plus lent que PaddleOCR-ONNX à modèles
  identiques (issue #514) → le bench doit inclure un pipeline ONNX maison optimisé.
- **Optimisation clé** : position, hauteur de ligne et police du chat étant fixes (config
  verrouillée), on découpe les lignes **par géométrie** et on ne passe en reconnaissance que
  les lignes nouvelles → ~10-40 ms par nouvelle ligne.
- **Fallback : `Windows.Media.Ocr` (ko-KR)** — natif .NET, quasi gratuit en CPU. Contraintes :
  pack de langue coréen à installer sur chaque machine, espaces CJK erratiques (à reconstruire
  côté code), qualité sur petit texte de jeu **non prouvée** (le tier « good » souvent cité n'a
  pas de source officielle).
- **OneOCR** (moteur du Snipping Tool Win11) : meilleur moteur local d'après la communauté →
  l'utiliser **uniquement comme étalon interne** ; DLL non redistribuables (zone grise légale,
  inacceptable pour des streamers exposés). Tesseract et EasyOCR : écartés (faibles/lents sur
  le coréen). Google Vision : secours d'urgence activable à chaud (dépendance réseau).
- **Prétraitement** : capture résolution native, upscaling 2-3× Lanczos + étirement de
  contraste — **pas de binarisation dure** (fond semi-transparent animé).
- ⚠️ **Le bench OCR sur vraies captures du chat KR est un prérequis BLOQUANT** : les 88 % sont
  mesurés sur un dataset générique en précision de ligne *exacte* ; personne n'a publié de
  mesure sur du hangul 12-18 px de jeu. Critère go/no-go : > 95 % de lignes exploitables.

### 4.3 Parsing — ne traduire QUE le message (cf. `docs/observations-captures.md`)
- **Filtre structurel** (pas « contient du hangul » — les pseudos sont coréens dans les lignes
  système) : seules les lignes `[Team|Équipe|All|Tous] Pseudo (Champion): message` sont du chat.
  On ne traduit que si la partie *message* contient du hangul.
- **Ancre de parsing** : la **dernière** parenthèse avant `:` correspondant à un nom de champion
  de la locale (liste Data Dragon `fr_FR`/`en_US`) — les pseudos peuvent être des phrases
  coréennes avec espaces (`아무도 나를 막을 수 없다`).
- Pseudos : jamais traduits (option romanisation). Mode « masquer les pseudos » du client
  (affiche le nom du champion à la place) : **à recommander dans l'onboarding** — simplifie
  parsing et OCR.
- **Déduplication renforcée** (correction vérifiée : la similarité seule ne suffit pas) :
  similarité normalisée (~0,8) **+ suivi de position/ordre des lignes** (fenêtre glissante).
  Sinon : le spam légitime (`ㅋㅋㅋ` répété) serait supprimé à tort, et le décalage des lignes au
  scroll créerait doublons/pertes. Les timestamps activés rendent la clé `heure+auteur+texte`
  quasi unique.

### 4.4 Traduction — 3 étages + fallback (chiffres vérifiés)
1. **Glossaire local instantané (0 ms)** : ~200 termes LoL coréens ultra-fréquents.
   ⚠️ Corrections du vérificateur : `ㅈㅈ` = « gg/ff » (abandon) ≠ `ㄱㄱ` = « go go » ;
   `ㅅㄱ` = 수고 (« bien joué/ciao ») ; « ㅈㄱ = jungle » est faux (jungle = 정글).
   **Faire relire la table par un locuteur natif avant l'event** — un contresens en overlay
   broadcast devant des milliers de viewers coûte cher.
2. **Cache LRU persistant** après normalisation (trim, réduction des répétitions de jamo).
3. **LLM : Claude Haiku 4.5 en streaming** (`claude-haiku-4-5`, $1/$5 par MTok — tarif officiel
   vérifié) : system prompt = contexte LoL + glossaire complet + few-shot, `max_tokens` ~100.
   - ⚠️ **Piège vérifié** : le préfixe cacheable minimum de Haiku 4.5 est **4096 tokens** — en
     dessous, le prompt caching échoue *silencieusement* et le coût est ×6 (~$190/mois au lieu
     de ~$30). Glossaire ≥ 4096 tokens (ou padding) + vérifier `cache_read_input_tokens > 0`.
   - **Timeout dur ~1,2 s** → fallback automatique **Google Cloud Translation v3** (50-200 ms,
     $20/1M chars, glossaires personnalisés gratuits) → 3e rideau **Azure Translator F0**
     (2M chars/mois gratuits). Interface `ITranslator` provider-agnostique.
   - Si la latence devient critique : Vertex AI a le meilleur TTFT mesuré pour Haiku (0,57 s).
   - **Papago écarté comme brique principale** (vérifié) : paire KO→FR dispo en un appel, mais
     compte NAVER Cloud région Corée obligatoire, tarif non publié, attribution de marque
     imposée. À tester seulement si un compte NCP s'obtient facilement.
   - DeepL écarté comme primaire : KO→FR jeune (2023), pivot anglais non documenté, le plus cher.
4. **Mode dégradé** : pas de réseau / APIs down → glossaire + hangul brut affiché. Ne jamais
   bloquer le pipeline.
- **Coût total vérifié : < $40/mois** au volume attendu (10 parties/j × 150 msgs ≈ 0,9 M
  chars/mois) ; < $80 même doublé.
- ⚠️ L'avantage LLM sur l'argot est **plausible mais non benchmarké publiquement** (même GPT-4o
  bute sur l'argot KO d'après COLING 2025 ; Papago est crédité fort sur le KO informel) →
  **test en aveugle sur ~500 messages réels** (VODs SoloQ Challenge = corpus gratuit) = le seul
  vrai juge. C'est le 2e gate de la phase 1.

### 4.5 Affichage
- **Overlay streamer** : fenêtre WPF transparente click-through à côté du chat (position/
  taille/opacité configurables, draggable via hotkey). Affichage streaming du LLM.
  ⚠️ Vérifié : une fenêtre topmost permanente au-dessus d'un jeu borderless peut casser les
  optimisations DWM (independent flip → G-Sync/VRR désactivé, ~1 frame de latence — précédent
  overlay Discord) → **n'afficher la fenêtre que lorsqu'il y a du texte**, et mesurer.
- **Source navigateur OBS** (chemin principal pour les viewers) : Kestrel + WebSocket local +
  page HTML transparente stylisée — le chat coréen traduit fait partie du show.
- **Mode privé** : `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` cache l'overlay des
  captures logicielles. ⚠️ Vérifié : échoue sur un sous-ensemble de machines Win11 (tester le
  code retour) ; inutile si le streamer utilise Game Capture (qui n'inclut jamais l'overlay).
  **OBS ≥ 31.1 requis** (fix Game Capture pour les jeux Vanguard).
- **Hotkeys** : pause/reprise, toggle overlay, recalibration.

### 4.6 Calibration de la zone de chat
- Zone déterministe si config verrouillée : résolution + Chat Scale + `ChatX/ChatY`
  (`PersistedSettings.json`, lecture seule). Presets résolutions courantes + calibration
  manuelle (rectangle sur screenshot) en secours. Re-validation à chaque patch LoL (~2 sem.).
- Détection de partie : process/fenêtre LoL + Live Client Data API port 2999 (gameplay only —
  confirmer avec DevRel que son usage est OK pour des joueurs en Corée, par prudence post-LCU).

---

## 5. Conformité & officialisation (à lancer JOUR 1 — délais de 3 semaines)

1. **Enregistrer le produit sur le Riot Developer Portal** en décrivant explicitement la
   méthode (capture/GEP + traduction + overlay externe, lecture seule, zéro input simulé) —
   revue ~hebdomadaire, jusqu'à 3 semaines de délai.
2. **Contacter Riot Developer Relations** pour une confirmation écrite. Précédents favorables :
   EzChat.gg et autres traducteurs publiés sur Overwolf (store qui exige l'approbation Riot) —
   la *catégorie* « traduction de chat » est acceptée ; l'approbation reste app par app.
3. **Passer par le canal événementiel** : un rush Corée de cette ampleur implique Riot
   France/Riot Korea pour les **comptes KR vérifiés KSSN** — faire valider l'outil par écrit
   dans le même paquet. (Le vrai vecteur de ban historique des bootcamps est le **partage de
   compte**, pas l'outillage — affaire TF Blade 2023. Et Riot Korea accorde des exceptions
   négociées, précédent OP.GG Desktop.) Obtenir un **contact Riot nommé joignable pendant le
   live**.
4. **Règles overlay 2025 à respecter** (politique durcie) : pas de pubs in-game (interdites
   depuis le 29 mai 2025), pas de timers/infos d'avantage compétitif (ult timers interdits
   depuis mars 2025 — on s'en tient strictement à la traduction), apparence clairement
   distincte de l'UI du jeu, disclaimer obligatoire « [App] isn't endorsed by Riot Games… ».
5. Voie A (Overwolf) : respecter « real-time usage only » → **aucun stockage/archivage des
   messages de chat**.

---

## 6. Phasage

### Phase 0 — Dérisquage immédiat (en parallèle, dès maintenant)
- [ ] Lancer l'enregistrement Developer Portal + contact DevRel + canal event Riot (§5).
- [ ] **Tester EzChat.gg tel quel** (plan C instantané + étalon).
- [ ] Constituer le corpus : ~500 messages réels (VODs SoloQ Challenge en cours) + captures
      locales non compressées dans `samples/`.
- [ ] Faire relire le glossaire v0 par un locuteur coréen natif.

### Phase 1 — Spike technique, dual-track (~1 semaine) — 3 gates
- [ ] **Gate acquisition** : prototype Overwolf GEP `chat` (vérifier hangul + serveur KR) VS
      bench OCR (PaddleOCR-ONNX korean / pipeline maison / Windows.Media.Ocr, OneOCR en étalon)
      sur vraies captures — go/no-go OCR : > 95 % de lignes exploitables, < 200 ms sous charge
      jeu+OBS. → décision voie A / voie B / les deux.
- [ ] **Gate traduction** : test en aveugle Haiku 4.5+glossaire vs Google v3+glossaire
      (vs Papago si compte NCP) sur le corpus. Vérifier le prompt caching (≥ 4096 tokens).
- [ ] Prototype console bout en bout : source → parsing → traduction → stdout, latence mesurée.

### Phase 2 — MVP (~2 semaines)
- [ ] App WPF : overlay click-through + fenêtre de config (zone, clés API, position, locale).
- [ ] Pipeline complet : dédup renforcée (similarité + position), glossaire v1, cache, fallbacks.
- [ ] Parsing multi-locale (fr_FR/en_US via Data Dragon), détection de partie auto.
- [ ] Test en conditions réelles sur le serveur KR.

### Phase 3 — Solidité production (~2 semaines)
- [ ] Serveur WebSocket + page overlay OBS stylisée.
- [ ] Watchdog : auto-recovery de chaque composant (OCR, API, device D3D, fenêtre LoL perdue) ;
      test d'endurance 8-12 h (fuites du frame pool WGC).
- [ ] Logs structurés + écran de diagnostic auto-explicatif (« pourquoi ça ne traduit pas ? »).
- [ ] Installeur Velopack signé, auto-update, onboarding guidé (Borderless, Chat visibility,
      timestamps, touche Z, masquage des pseudos, Win11, OBS ≥ 31.1, pack ko-KR si fallback).
- [ ] Profil perf : < 3 % CPU, impact GPU négligeable, et **répétition générale sur les
      machines réelles de l'event, Vanguard actif, une semaine avant le live**.

### Phase 4 — Polish
- [ ] Glossaire enrichi des retours beta ; ton de traduction (familier/neutre) ; romanisation
      des pseudos.
- [ ] Traduction inverse (FR → coréen à copier-coller) pour répondre. ⭐ très demandé
- [ ] Multi-langue (KR→EN pour version internationale ?).

---

## 7. Risques & parades

| Risque | Impact | Parade |
|---|---|---|
| OCR insuffisant sur hangul 12-18 px | Bloquant (voie B) | Bench phase 1 BLOQUANT ; upscale + géométrie de lignes ; voie A en parallèle ; EzChat.gg plan C |
| GEP Overwolf KO sur serveur KR ou approbation lente | Bloquant (voie A) | Dual-track : la voie B reste prête |
| Latence LLM dérive (529/charge) | Moyen | Timeout 1,2 s + fallback Google v3 + Azure ; Vertex si TTFT critique |
| Prompt cache silencieusement inactif | Coût ×6 | Glossaire ≥ 4096 tokens + monitoring `cache_read_input_tokens` |
| Contresens de glossaire en live (ㅈㅈ/ㄱㄱ/ㅅㄱ) | Image | Relecture par natif + test corpus en aveugle |
| Patch LoL change l'UI/le GEP | Élevé | Calibration découplée + re-test à chaque patch ; auto-update pour pousser un fix vite |
| Overlay topmost casse VRR/independent flip | Faible-moyen | Fenêtre affichée seulement quand il y a du texte ; mesurer ; mode OBS-only sinon |
| Streamer en plein écran exclusif | Moyen | Détection + alerte ; la source navigateur OBS marche quand même pour les viewers |
| WDA_EXCLUDEFROMCAPTURE inopérant (sous-ensemble Win11) | Faible | Vérifier code retour ; Game Capture + browser source = chemins principaux |
| Blocage VAN au lancement (drivers RGB/monitoring tiers) | Moyen | Audit des machines de l'event ; répétition générale Vanguard actif |
| Ban lié aux comptes (KSSN/partage) — hors outil | Critique | Comptes fournis/validés par Riot via le canal event (précédent TF Blade) |
| Politique overlays Riot durcie d'ici l'event | Faible | Périmètre strict traduction ; veille @RiotGamesDevRel ; contact DevRel ouvert |

---

## 8. Décisions à trancher (avec recommandation)

0. **Source du chat** : Overwolf GEP **[probable]** vs OCR vs les deux → gate phase 1.
1. **Moteur de traduction** : Haiku 4.5 + glossaire **[recommandé]** vs Google v3 → gate
   corpus en aveugle.
2. **Coût API** : clé par streamer **[recommandé pour quelques streamers]** vs service hébergé.
3. **Romanisation des pseudos** : oui/non par défaut.
4. **Timing** : si l'outil doit servir pour l'event EN COURS, la seule option réaliste cette
   semaine = EzChat.gg (ou app Overwolf GEP minimale) ; l'app complète vise la prochaine
   édition / la suite de l'event.

---

## 9. Références clés (vérifiées juin 2026)

- Riot — Vanguard FAQ DevRel : riotgames.com/en/DevRel/vanguard-faq (« no allow list »,
  overlays OK, memory readers cassés)
- Riot — Third Party Applications (maj 29 mai 2025) ; /dev: Vanguard x LoL + Retrospective
- Riot Developer Portal (enregistrement produit) ; politique LCU Corée (janv. 2019)
- Overwolf GEP LoL — feature `chat` : dev.overwolf.com/ow-native/live-game-data-gep/supported-games/league-of-legends/
- EzChat.gg (Overwolf) — précédent direct traducteur de chat LoL sans OCR
- PaddleOCR PP-OCRv5 multi-languages (korean mobile rec 88,0 %) ; BobLd/RapidOcrNet (.NET 8)
- Anthropic — pricing + prompt caching (Haiku 4.5 : $1/$5 ; préfixe min 4096 tokens) ;
  Artificial Analysis (TTFT 0,57-0,95 s, ~93 tok/s)
- Google Cloud Translation v3 (tarifs, glossaires gratuits) ; Azure Translator F0 ;
  NCP Papago (KO→FR, région Corée, tarif non publié)
- Microsoft — Windows.Graphics.Capture, IsBorderRequired, MinUpdateInterval (24H2),
  SetWindowDisplayAffinity ; WinUI 3 #7276 (pas de transparence)
- OBS 31.1 — fix Game Capture jeux Vanguard ; guide de capture LoL
- Rapport de recherche complet (10 agents + contre-vérification) :
  `docs/recherche-rapport-2026-06-10.json`
