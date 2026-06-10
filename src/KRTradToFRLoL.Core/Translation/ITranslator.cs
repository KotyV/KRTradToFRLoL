namespace KRTradToFRLoL.Translation;

public interface ITranslator
{
    /// <summary>
    /// Traduit un message. <paramref name="onPartial"/> reçoit le texte partiel au fil du
    /// streaming (peut n'être jamais appelé). Renvoie null si la traduction a échoué.
    /// </summary>
    Task<string?> TranslateAsync(string koreanText, Action<string>? onPartial, CancellationToken ct);
}
