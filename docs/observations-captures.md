# Observations des captures de référence (chat LoL — streamers SoloQ Challenge Corée)

> Transcription et analyse des captures fournies en juin 2026. Les captures proviennent de
> **VOD/streams Twitch (sortie OBS)**, pas du jeu natif : compression vidéo, hangul flouté.
> En production, l'app capturera l'écran **localement** (avant OBS) → qualité bien meilleure.
> Ces images restent le **pire cas** de référence pour le benchmark OCR.
>
> **Pseudos anonymisés** : ce repo est public — tous les pseudos de joueurs (y compris
> les noms cités dans le corps des messages) ont été remplacés par des placeholders de
> même structure (hangul/latin, espaces, chiffres), et les noms de chaînes retirés.
> La structure des lignes, seule chose qui compte pour le parseur, est fidèle.
> Les images originales ne sont **pas versionnées** pour la même raison (pseudos et
> habillage de stream visibles) : elles vivent en local dans `samples/`, ignoré par git.

---

## Capture 1 — client anglais, zoom chat (partie en cours, timestamps activés)

```text
16:38 도레미파솔 (Vladmir) has shut down blue forest (Lucian!) (Bonus Bounty: 460G)   [système, jaune]
16:46 Nautilus (Nautilus): KaiSa used Flash                                         [ping]
16:47 [Team] blue forest (Lucian): 에반데                                            [CHAT]
16:49 [Team] blue forest (Lucian): 몰라 / 디제압이라                                  [CHAT]
16:50 Nautilus (Nautilus) purchased Control Ward                                    [système]
16:52 Objective Bounties are falling off soon. Claiming an Objective Bounty...      [système, jaune]
16:53 [Team] 분홍 토끼 (Rek'Sai): 유리하게 싸울수있는데                              [CHAT]
```

## Captures 2 & 3 — client anglais, zoom chat

```text
04:24 동화 속 영웅처럼 (Lee Sin) is on the way                                      [ping]
04:24 이왕이면제대로하자 (Jayce) is on the way                                          [ping]
04:28 아무도 나를 막을 수 없다 (Jhin) has slain 갈까말까 (Ezreal) for a double kill! [kill]
04:33 [Team] 갈까말까 (Ezreal): 빨리처졍~                                             [CHAT — typo stylisée]
04:36 토끼부인 (Yuumi) purchased Control Ward                                        [système]
04:48 [Team] 토끼부인 (Yuumi): 럭스 e때매 죄송해요                                     [CHAT — touche "e" latine + argot 때매]
04:59 동화 속 영웅처럼 (Lee Sin) signals to be cautious                             [ping]
05:37 STREAMER IS BACK (Kennen) is on a killing spree!                               [kill — pseudo latin du streamer]
05:38 토끼부인 (Yuumi) signals enemy has vision here                                 [ping]
05:47 토끼부인 (Yuumi): Wait For Yuumi Heal - 0s                                     [ping timer de sort]
05:51 동화 속 영웅처럼 (Lee Sin) signals that enemies are missing                   [ping]
05:53 아무도 나를 막을 수 없다 (Jhin) has slain 토끼부인 (Yuumi) for a double kill!  [kill]
07:33 토끼부인 (Yuumi) Ward Quest Complete!                                          [système]
07:34 아무도 나를 막을 수 없다 (Jhin) is on a rampage!                              [kill, orange]
07:35 [Team] 갈까말까 (Ezreal): 아니                                                  [CHAT]
```

## Capture 4 — plein écran 1080p, stream Twitch (client anglais)

- Chat en **bas-gauche**, juste au-dessus de la barre de vie du champion ; zone minuscule
  par rapport au 1080p complet.
- Overlay stream autour : logo de l'event (haut-droite), badge rang du streamer
  (rang + LP + URL de l'event, haut-centre), webcam + minimap (bas-droite).
- 165 FPS / 3 ms ping (machine de jeu sur place en Corée).
- Lignes chat : `07:55 [Team] 토끼부인 (Yuumi): 죄송`, `07:56 [Team] 갈까말까 (Ezreal): 들맞아줘~~~~`
  → déjà à peine lisibles après compression Twitch (mais OK en capture locale).

## Capture 5 — plein écran 1080p, stream Twitch, **CLIENT EN FRANÇAIS** ⚠️

```text
04:24 Maître Yi (Maître Yi) indique que des ennemis ont disparu      [ping, en FR]
04:26 [Équipe] Maître Yi (Maître Yi): 라인                            [CHAT]
04:27 [Équipe] Maître Yi (Maître Yi): ㅋㅋㅋㅋㅋㅋ                       [CHAT — rire]
04:43 Jayce (Jayce) est imbattable !                                 [kill, en FR]
```

- Plusieurs noms de chaînes Twitch sont visibles dans l'habillage du stream (anonymisés
  ici — hors zone de chat, sans impact sur l'OCR).

## Capture 6 — plein écran 1080p, stream Twitch, **CLIENT EN CORÉEN** ⚠️ (scoreboard ouvert)

```text
못기다려 (사일러스) 님이 적 와드를 확인하고 신호를 보냄        [ping ward ennemi, en CORÉEN]
Latin Nick (요네) 님이 출숙격 신호를 보냄                  [ping all-in]
Latin Nick (요네) 님이 지원 요청을 보냄                    [ping assistance]
Latin Nick (요네) 님이 적이 사라졌다고 알림                 [ping mia]
햄스터 (르블랑) 님이 출숙격 신호를 보냄                        [ping]
Latin Nick (요네) 님이 강철멘탈 - (나피리) 님에게 퇴각 신호를 보냄 [ping retraite ciblé]
```

- Le streamer (« Latin Nick », pseudo latin) joue avec le **client 100 % coréen** :
  pings/système en coréen, **noms de champions en coréen** (요네 = Yone, 사일러스 = Sylas,
  르블랑 = LeBlanc, 나피리 = Naafiri).
- **Pas de timestamps** sur cette config → le parseur doit les traiter comme optionnels (fait).
- Le filtre structurel tient : les pings coréens suivent le motif « X (champion) 님이 … 보냄 »
  **sans deux-points** après la parenthèse → exclus par la règle « (Champion): message ».
- Conséquences code : charger la locale **ko_KR** de Data Dragon pour l'ancre champion
  (fait — `ChampionNames`), accepter les tags de canal coréens 팀/아군/전체/모두/파티
  (fait — `ChatLineParser` ; libellés exacts encore à confirmer sur un client KR réel).

## Capture 7 — client coréen, **cas de test négatif** (aucun vrai message de chat)

```text
몬스터 (르블랑) 님이 가고 있음                          [ping on-my-way]
Latin Nick (요네) 님이 첫 번째 포탑을 파괴했습니다!    [première tourelle, jaune]
강철멘탈 (나피리) 님이 적 와드를 확인하고 신호를 보냄        [ping ward ennemi]
몬스터 (르블랑) 님이 후퇴 신호를 보냄                    [ping retraite]
강철멘탈 (나피리) 님이 위험 신호를 보냄                    [ping danger]
못기다려 (사일러스)님이 제어 와드 아이템을 구입했습니다.   [achat control ward — noter: PAS d'espace avant 님이]
```

- 8 lignes, toutes système/ping en coréen, **zéro message joueur** : le parseur doit tout
  ignorer (aucune n'a de `:` après la parenthèse champion). À utiliser comme test négatif.
- Confirme : pas de timestamps sur cette config, espacement irrégulier autour de `님이`.

## Capture 8 — client anglais, timestamps activés (début de partie)

```text
02:24 브로콜리시러 (Ezreal) signals that enemies are missing      [ping]
02:33 [Team] 브로콜리시러 (Ezreal): AN                            [CHAT en latin pur — sans hangul]
02:34 [Team] 브로콜리시러 (Ezreal): 뭐해요                         [CHAT « tu fais quoi »]
02:51 복 많은 사람 (Lee Sin) is asking for assistance           [ping — pseudo avec espaces]
02:54 207호 (Vi) signals 복 많은 사람 - (Lee Sin) to fall back   [ping ciblé : DEUX parenthèses champion, pas de ':']
02:59 복 많은 사람 (Lee Sin): Soraka used Heal                  [ping avec ':' mais sans tag + message EN]
```

- Nouveaux cas de test : ping ciblé à deux parenthèses (rejet), message d'équipe en latin pur
  (« AN », probablement une erreur d'IME) → conservé en **copie non traduite** : l'overlay
  reflète tout le chat joueur, et un message sans hangul est déjà lisible tel quel.
- Pseudos : espaces (« 복 많은 사람 »), chiffres+hangul (« 207호 »), champion court (« Vi », 2 car.).

## Captures 9-11 — client anglais, timestamps (même partie, shop/scoreboard ouverts)

```text
08:00 [Team] 브로콜리시러 (Ezreal): ㅔ네 ㅋㅋㅋ                  [CHAT — jamo isolé en début de message]
08:11 Maxxy (Jax) signals to all in                          [ping — PSEUDO LATIN sur serveur KR]
08:27 [Team] 복 많은 사람 (Lee Sin): 궁을                     [CHAT — « l'ult »]
08:29 [Team] 복 많은 사람 (Lee Sin): 바로 찍네 ㄷㄷ            [CHAT — « il la monte direct, ㄷㄷ »]
08:42 복 많은 사람 (Lee Sin): Darius Ghost                    [ping spell ennemi : ':' mais EN → rejet]
08:45 복 많은 사람 (Lee Sin): Nocturne R                      [idem]
08:46 [Team] 브로콜리시러 (Ezreal): 칠거죠                       [CHAT]
08:47 [Team] 브로콜리시러 (Ezreal): 서렌?                        [CHAT — 서렌 = surrender/ff → glossaire]
08:51 [Team] 복 많은 사람 (Lee Sin): 상대가                    [CHAT — phrase coupée sur 2 messages]
08:53 [Team] 복 많은 사람 (Lee Sin): 좀못하는데?                [CHAT — suite]
```

- Pseudos latins purs possibles (« Maxxy ») ; vocabulaire à glossaire : 서렌 (ff/surrender),
  궁 (ult) ; les joueurs coupent leurs phrases en plusieurs messages courts (le LLM traduit
  message par message, c'est acceptable).
- La fenêtre de chat reste lisible avec shop/scoreboard ouverts (le chat est par-dessus).

## Captures 12-13 — client anglais, flame en chat + MESSAGE REPLIÉ SUR DEUX LIGNES ⚠️

```text
24:25 [Team] Nick9 (Yunara): ff                              [latin pur → copie non traduite]
24:26 [Team] 라면연구협회장 (Sylas): 아ㄴ;                      [typo jamo + ponctuation]
24:28 [Team] 라면연구협회장 (Sylas): 집짜                       [typo de 진짜]
24:31 [Team] Nick9 (Yunara): 하루종일고힐빨고                   [flame]
미안합니다나와아하는게 정상아닌가요?                              [⚠ LIGNE SANS PRÉFIXE]
24:55 [Team] Nick9 (Yunara): 본인이쓰레기처럼못한걸             [flame]
24:56 [Team] Nick9 (Yunara): 왜그렇게추하게삼? ㅋㅋ
25:04 [Team] Nick9 (Yunara): 없이살아서그런가
```

- **Messages longs repliés sur 2 lignes** : la ligne de continuation n'a pas de préfixe
  `[Team] Pseudo (Champion):` → géré par `ChatFrameAssembler` (fusion avec le message
  précédent si adjacent + hangul + pas un fragment système coréen). Si l'en-tête a défilé
  hors de la zone, la continuation orpheline est perdue (limitation assumée).
- Champion récent (« Yunara », 2025) → la liste Data Dragon, toujours à jour, est la bonne source.
- Corpus flame réel pour le test de traduction (본인이쓰레기처럼못한걸, 왜그렇게추하게삼…).

## Capture 14 — **CHAMP SELECT** (fenêtre client, ranked) ⚠️

```text
Krug : 아 홍길동 이새기                       [CHAT lobby — coéquipiers ANONYMISÉS en camps de jungle]
Krug : 진짜
Krug : ㅋㅋ
Krug : 연승치니까 주포절대안주네 4판연속        [râle sur l'autofill — négociation de lanes]
```

- Format différent de l'in-game : `Pseudo : message` sans tag de canal ni parenthèse champion,
  sans timestamps. En ranked, les coéquipiers apparaissent comme Krug/Gromp/Murk Wolf/
  Raptor/Scuttle Crab. → géré par le mode champ-select du parseur (`Channel = "Lobby"`).
- C'est la fenêtre du CLIENT (pas du jeu) : zone de capture différente à sélectionner.

## Capture 15 — client anglais, timestamps (régression : rien de nouveau, tout couvert)

```text
28:45 토마토 (Lucian) signals that enemies are missing               [ping → rejet]
28:47 [Team] 정글 어디 갔니 (Karma): 사이드를                        [CHAT — pseudo-phrase à espaces]
28:48 [Team] 정글 어디 갔니 (Karma): 그렇게                          [CHAT — phrase étalée sur 3 messages]
28:49 [Team] 정글 어디 갔니 (Karma): 쳐도시면                        [CHAT]
28:51 [Team] 정글 어디 갔니 (Karma): 상대바론인데                     [CHAT — « c'est le baron adverse »]
28:53 정글 어디 갔니 (Karma) purchased Mikael's Blessing            [achat, apostrophe dans l'objet → rejet]
29:24 정글 어디 갔니 (Karma) purchased Control Ward                 [achat → rejet]
```

## Capture 16 — client anglais, timestamps (transcription partielle, lignes reprises dans les tests)

```text
12:44 흰 화살 (Rek'Sai) has hit a new milestone on Jaws: 3,346,012!  [système → rejet]
13:05 참치김밥 (Katarina): Twisted Fate R                            [ping spell ennemi → rejet]
13:26 흰 곰 (Hwei) Bot Quest Complete!                               [système → rejet]
13:31 [Team] Vi (Vi): stop die                                      [CHAT latin pur → copie non traduite]
13:45 [Team] NickYS (Thresh): because bot AD                        [CHAT latin pur → copie non traduite]
13:49 [Team] 흰 곰 (Hwei): ㅋㅋ                                       [CHAT]
13:51 [Team] 흰 곰 (Hwei): 원숭이마낭                                 [CHAT]
13:52 [Team] 흰 곰 (Hwei): 쳐대주고                                   [CHAT]
13:52 [Team] 흰 곰 (Hwei): 입은                                       [CHAT — même seconde que le précédent]
```

- Deux messages différents du même auteur à la même seconde (13:52) → la clé de dédup
  doit inclure le texte, pas seulement `heure + auteur` (cf. `DeduplicatorTests`).

## Capture 17 — sémantique des couleurs du chat (client anglais)

- **Bleu** : pseudos et pings alliés ; **doré/jaune** : objets, quêtes d'objet, achats,
  annonces d'objectifs (« The enemy team has slain the Infernal Drake! » — corps doré) ;
  **rouge** : toute référence ennemie (« The enemy team », noms ennemis dans les kills
  « has slain 잘하는우리팀당 (Graves) », pings ciblés « has targeted 미드 홍길동 ») ;
  **orange** : kill streaks.
- L'overlay reprend ce code pour ce qu'il affiche : timestamp gris, pseudo bleu (allié),
  rouge pour le /all, doré pour le champ select, texte blanc (gris si non traduit).

---

## Implications de design (toutes reflétées dans le parseur)

1. **Le client peut être localisé en français** → le parsing doit être multi-locale :
   - Tag d'équipe : `[Team]` / `[Équipe]` / `[All]` / `[Tous]` (+ variantes selon locale).
   - **Noms de champions localisés** : « Maître Yi » et non « Master Yi ». La liste des noms
     s'obtient par locale via **Data Dragon** (`fr_FR`, `en_US`) — API statique Riot, autorisée.
   - Messages système/pings localisés dans la langue du client → filtre par structure, pas par langue.
   - Détecter la locale du client via le process LoL ou la config, ou tester FR puis EN au parsing.

2. **Filtre structurel, pas « contient du hangul »** : la majorité des lignes sont des
   pings/kills/système **contenant des pseudos coréens** mais sans contenu à traduire.
   Seules les lignes `[Team|Équipe|All|Tous] Pseudo (Champion): message` sont du chat ;
   on ne traduit que si la partie *message* (après `:`) contient du hangul
   (`ㅋㅋㅋ` peut passer tel quel ou être rendu « mdrrr »).

3. **Pseudos = phrases coréennes complètes avec espaces** (`아무도 나를 막을 수 없다`).
   L'ancre de parsing fiable = motif `(NomDeChampionConnu):` — chercher la **dernière**
   parenthèse avant `:` correspondant à un champion de la liste Data Dragon de la locale.

4. **Mode « masquer les pseudos » (streamer mode)** : capture 5, le coéquipier apparaît comme
   `Maître Yi (Maître Yi)` → le client remplace les pseudos par le nom du champion.
   Simplifie énormément le parsing et l'OCR (plus de hangul dans les pseudos).
   → **À recommander dans l'onboarding** des streamers.

5. **Timestamps activés** sur les captures in-game des clients EN/FR — mais absents sur le
   client coréen (captures 6-7) et en champ select (capture 14) → clé de dédup naturelle
   `heure + auteur + texte` quand ils sont là ; le parseur les traite comme optionnels (fait).
   À recommander/forcer dans l'onboarding (option du client).

6. **Particularités du chat coréen réel observées** (corpus pour glossaire/prompt LLM) :
   - `에반데` (= 에바인데, « c'est abusé »), `빨리처졍~` (typo stylisée de 빨리 쳐줘/처줘),
     `럭스 e때매 죄송해요` (touche de sort latine + 때매 = 때문에),
     `죄송` (pardon), `아니` (non), `몰라` (je sais pas), `디제압이라`,
     `유리하게 싸울수있는데`, `라인` (lane), `ㅋㅋㅋㅋ` (kkkk = mdr).
   - Mélange hangul + lettres latines (touches Q/W/E/R, noms de champions) dans un même message.

7. **Dataset OCR** : les VOD Twitch du SoloQ Challenge Corée (en cours) = source abondante de
   frames réelles pour le benchmark de la phase 1, en plus des captures locales non compressées.
