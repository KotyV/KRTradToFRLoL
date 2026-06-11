// Proxy de traduction KRTradToFRLoL — Vercel Edge Function.
//
// Rôle : les apps des streamers n'embarquent JAMAIS la clé Anthropic. Elles appellent
// ce relais avec un token d'app révocable (header x-app-token) ; la clé vit ici, en
// variable d'environnement Vercel. Garde-fous : liste de tokens, modèle imposé,
// max_tokens plafonné — un token qui fuite ne peut pas servir à autre chose qu'à
// traduire des messages courts, et se révoque en retirant une entrée d'APP_TOKENS.
//
// Variables d'environnement (Settings → Environment Variables sur Vercel) :
//   ANTHROPIC_API_KEY  : la clé Anthropic (secret)
//   APP_TOKENS         : tokens autorisés, séparés par des virgules, un par streamer
//                        (ex: "tok-streamer1-x7Kq,tok-streamer2-p2Mn") — révocable individuellement.

export const config = {
  runtime: 'edge',
  regions: ['icn1'], // Séoul : latence minimale depuis la Corée
};

const ALLOWED_MODEL_PREFIX = 'claude-haiku';
const MAX_TOKENS_CAP = 300;

export default async function handler(req) {
  if (req.method !== 'POST') {
    return new Response('Method Not Allowed', { status: 405 });
  }

  const token = req.headers.get('x-app-token') ?? '';
  const allowed = (process.env.APP_TOKENS ?? '')
    .split(',')
    .map((t) => t.trim())
    .filter(Boolean);
  if (!token || !allowed.includes(token)) {
    return new Response('Unauthorized', { status: 401 });
  }

  let body;
  try {
    body = await req.json();
  } catch {
    return new Response('Bad Request', { status: 400 });
  }

  const model =
    typeof body.model === 'string' && body.model.startsWith(ALLOWED_MODEL_PREFIX)
      ? body.model
      : 'claude-haiku-4-5';

  const upstream = await fetch('https://api.anthropic.com/v1/messages', {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      'x-api-key': process.env.ANTHROPIC_API_KEY,
      'anthropic-version': '2023-06-01',
    },
    body: JSON.stringify({
      ...body,
      model,
      max_tokens: Math.min(Number(body.max_tokens) || 200, MAX_TOKENS_CAP),
    }),
  });

  // Relaye le flux SSE tel quel (le streaming de la traduction passe à travers).
  return new Response(upstream.body, {
    status: upstream.status,
    headers: {
      'content-type': upstream.headers.get('content-type') ?? 'text/event-stream',
      'cache-control': 'no-store',
    },
  });
}
