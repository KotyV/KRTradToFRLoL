# Proxy de traduction (Vercel)

Relais minimal entre l'app des streamers et l'API Anthropic : **la clé API ne quitte
jamais ce serveur**. Chaque streamer reçoit un token d'app individuel et révocable.

## Déploiement

```bash
cd server/vercel-proxy
npx vercel deploy --prod
```

Puis dans le dashboard Vercel (Settings → Environment Variables) :

| Variable | Valeur |
|---|---|
| `ANTHROPIC_API_KEY` | ta clé Anthropic (type **Sensitive**) |
| `APP_TOKENS` | tokens autorisés séparés par des virgules, un par streamer — ex. `tok-streamer1-Xk29qLmAw7,tok-streamer2-Zp41RnQc8x` (génère-les longs et aléatoires) |

Redéploie après modification des variables. La fonction tourne en région `icn1` (Séoul)
pour minimiser la latence depuis la Corée.

## Côté app

Dans `%AppData%/KRTradToFRLoL/config.json` du streamer :

```json
{
  "ProxyUrl": "https://TON-PROJET.vercel.app/api/translate"
}
```

et le token via l'app (il est chiffré DPAPI, jamais stocké en clair).

## Sécurité & coûts

- Un token qui fuite ne permet **que** de traduire des messages courts (modèle imposé
  `claude-haiku*`, `max_tokens` plafonné à 300) et se révoque en l'enlevant d'`APP_TOKENS`.
- Suivi des coûts : dashboard Anthropic (par clé) + logs Vercel (par token si besoin).
- Ne mets JAMAIS la clé dans le repo, l'app ou un fichier de config distribué.
