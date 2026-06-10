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
    public IReadOnlyList<ChatMessage> Assemble(IReadOnlyList<string> lines)
    {
        var messages = new List<ChatMessage>();
        var lastParsedIndex = -2; // jamais adjacent au départ

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var msg = parser.Parse(line);
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
            }
        }

        return messages;
    }
}
