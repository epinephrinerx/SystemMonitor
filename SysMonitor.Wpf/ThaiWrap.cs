using System.Text;

namespace SysMonitor;

/// <summary>
/// Gives Thai text somewhere sensible to break.
///
/// Thai is written without spaces and WPF has no Thai word dictionary, so a
/// wrapping TextBlock breaks a Thai run at whichever character happens to
/// reach the edge: at a large font size the last letter or two of a word end
/// up alone on the next line ("เครือข่า" / "ย"). A zero-width space is a line
/// break opportunity that draws nothing, so putting one in front of each
/// leading vowel (เ แ โ ใ ไ) -- which always starts a syllable -- lets the line
/// break there instead.
///
/// This is a heuristic, not a segmenter: it never breaks anywhere a word could
/// not be split by a reader, but it does not find every word boundary, and a
/// run with no leading vowel in it still breaks at the character. Text with no
/// Thai in it comes back unchanged.
/// </summary>
internal static class ThaiWrap
{
    public const char Break = '\u200B';

    private static bool IsThai(char c) => c is >= 'ก' and <= '๛';

    private static bool IsLeadingVowel(char c) => c is 'เ' or 'แ' or 'โ' or 'ใ' or 'ไ';

    public static string Soften(string text)
    {
        StringBuilder? result = null;
        for (int i = 1; i < text.Length; i++)
        {
            if (IsLeadingVowel(text[i]) && IsThai(text[i - 1]) && !IsLeadingVowel(text[i - 1]))
            {
                result ??= new StringBuilder(text, 0, i, text.Length + 8);
                result.Append(Break);
            }
            result?.Append(text[i]);
        }
        return result?.ToString() ?? text;
    }
}
