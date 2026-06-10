using KRTradToFRLoL.Core;

namespace KRTradToFRLoL.Parsing;

/// <summary>
/// Assemble les lignes OCR d'une frame en messages de chat, en recollant les messages
/// longs repliés sur plusieurs lignes : la ligne de continuation n'a pas de préfixe
/// « [Team] Pseudo (Champion): » (cf. capture 13, docs/observations-captures.md).
/// Une ligne orpheline n'est fusionnée que si elle suit IMMÉDIATEMENT un message parsé,
/// contient du hangul et ne ressemble pas à un fragment de message système coréen.
/// </summary>
public sealed class ChatFrameAssembler(ChatLineParser parser)
{
    /// <param name="mirrorAllLines">Si vrai, les lignes non-chat (pings, kills, système)
    /// deviennent des messages « Sys » : traduits si majoritairement coréens (client KR),
    /// copiés sinon — l'overlay reflète alors tout le bloc de chat.</param>
    public IReadOnlyList<ChatMessage> Assemble(IReadOnlyList<string> lines, bool mirrorAllLines = false)
    {
        var messages = new List<ChatMessage>();
        var lastParsedIndex = -2; // jamais adjacent au départ

        // Une frame qui contient des marqueurs in-game (timestamps, « (Champion) ») vient
        // du jeu : le format champ-select y est désactivé — en partie, il n'attrape que
        // des fragments d'OCR contenant le pseudo coréen d'un joueur.
        var allowChampSelect = !lines.Any(ChatLineParser.LooksLikeNewEntry);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var msg = parser.Parse(line, allowChampSelect);
            if (msg is not null)
            {
                messages.Add(msg);
                lastParsedIndex = i;
                continue;
            }

            // Continuation potentielle d'un message replié ?
            if (lastParsedIndex == i - 1
                && messages.Count > 0
                && ChatLineParser.ContainsHangul(line)
                && !ChatLineParser.LooksLikeKoreanSystemFragment(line)
                && !ChatLineParser.LooksLikeNewEntry(line))
            {
                var last = messages[^1];
                messages[^1] = last with
                {
                    Text = $"{last.Text} {line.Trim()}",
                    RawLine = $"{last.RawLine} ⏎ {line}",
                };
                lastParsedIndex = i; // autorise un repli sur 3 lignes et plus
                continue;
            }

            // Porte de qualité : une ligne système doit RESSEMBLER à une ligne de chat
            // (timestamp ou parenthèse champion ou hangul majoritaire) — les fragments
            // d'OCR sur lignes en plein fondu (« COIEUI », « 2B Da Qs ») échouent aux trois.
            var text = line.Trim();
            if (mirrorAllLines && text.Length >= 4
                && (ChatLineParser.LooksLikeNewEntry(line) || ChatLineParser.IsMostlyHangul(text)))
            {
                messages.Add(new ChatMessage
                {
                    Channel = "Sys",
                    Text = text,
                    RawLine = line,
                    // Ping/annonce du client coréen → traduisible ; ligne anglaise avec
                    // pseudo hangul → simple copie.
                    NeedsTranslation = ChatLineParser.IsMostlyHangul(text),
                });
            }
        }

        return messages;
    }
}
