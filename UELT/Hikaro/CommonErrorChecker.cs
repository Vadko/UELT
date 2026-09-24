using System.Text.RegularExpressions;

namespace UELT.Core;

public static class CommonErrorChecker
{
    private static readonly Regex PlaceholderRegex = new(@"\{[^{}]*\}|%\d*\$?[sdifgxXeEoc%]|<[^<>]*>|\\[nrt]", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex RepeatedWordRegex = new(@"\b(\p{L}+)\s+\1\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DoubleSpaceRegex = new(@"  +", RegexOptions.Compiled);
    private static readonly char[] TerminalPunctuation = { '.', '!', '?', ':', '…' };
    private static readonly Regex MissingSpaceAfterPunctuationRegex = new(@"[.,!?:](?=\p{L})", RegexOptions.Compiled);
    private static readonly Regex PipeFunctionRegex = new(@"\|(?:platform|gender|plural|text_gp|fmt)\([^)]*\)", RegexOptions.Compiled);
    private static readonly char[] ClosingWrappers = { '"', '\'', '»', ')', ']', '”', '’' };
    private static readonly Regex TagPairRegex = new(@"</>|<(/?)([a-zA-Z][\w-]*)[^<>]*?(/?)>", RegexOptions.Compiled);
    private static readonly Regex DoubledMinorPunctuationRegex = new(@"([,;:])\1+", RegexOptions.Compiled);
    private static readonly HashSet<string> StandaloneMarkerTags = new(StringComparer.OrdinalIgnoreCase) { "cf", "cr", "lf", "tb" };

    public static bool HasCommonErrors(string original, string translation)
    {
        if (string.IsNullOrEmpty(translation) || translation == original)
            return false;

        var whitespace = GetWhitespaceMismatch(original, translation);

        return HasPlaceholderMismatch(original, translation)
            || HasNumberMismatch(original, translation)
            || RepeatedWordRegex.IsMatch(translation)
            || DoubleSpaceRegex.IsMatch(translation)
            || whitespace.Leading || whitespace.Trailing
            || HasTerminalPunctuationMismatch(original, translation)
            || HasUnbalancedSymbols(original, translation)
            || HasMissingSpaceAfterPunctuationMismatch(original, translation)
            || HasMismatchedTagPairs(original, translation)
            || DoubledMinorPunctuationRegex.IsMatch(translation);
    }

    public static List<string> GetCommonErrors(string original, string translation)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(translation) || translation == original)
            return errors;

        if (HasPlaceholderMismatch(original, translation))
            errors.Add("Не збігаються теги/плейсхолдери");

        if (HasNumberMismatch(original, translation))
            errors.Add("Не збігаються числа з оригіналом");

        if (RepeatedWordRegex.IsMatch(translation))
            errors.Add("Повторене слово підряд");

        if (DoubleSpaceRegex.IsMatch(translation))
            errors.Add("Подвійний пробіл");

        if (!string.IsNullOrEmpty(original) && !string.IsNullOrEmpty(translation))
        {
            bool origLeading = char.IsWhiteSpace(original[0]);
            bool trLeading = char.IsWhiteSpace(translation[0]);

            if (origLeading && !trLeading)
                errors.Add("Відсутній пробіл на початку рядка");
            else if (!origLeading && trLeading)
                errors.Add("Зайвий пробіл на початку рядка");

            bool origTrailing = char.IsWhiteSpace(original[^1]);
            bool trTrailing = char.IsWhiteSpace(translation[^1]);

            if (origTrailing && !trTrailing)
                errors.Add("Відсутній пробіл у кінці рядка");
            else if (!origTrailing && trTrailing)
                errors.Add("Зайвий пробіл у кінці рядка");
        }

        if (HasTerminalPunctuationMismatch(original, translation))
            errors.Add("Розбіжність кінцевої пунктуації");

        if (HasUnbalancedSymbols(original, translation))
            errors.Add("Не закриті дужки чи лапки");

        if (HasMissingSpaceAfterPunctuationMismatch(original, translation))
            errors.Add("Відсутній пробіл після розділового знаку");

        if (HasMismatchedTagPairs(original, translation))
            errors.Add("Неправильна пара тегів або незакритий тег");

        if (DoubledMinorPunctuationRegex.IsMatch(translation))
            errors.Add("Подвоєний розділовий знак");

        return errors;
    }

    private static bool HasPlaceholderMismatch(string original, string translation)
        => !TokenMultisetsEqual(PlaceholderRegex, original, translation);

    private static bool HasNumberMismatch(string original, string translation)
    {
        string origClean = PlaceholderRegex.Replace(original ?? "", " ");
        string trClean = PlaceholderRegex.Replace(translation ?? "", " ");
        return !TokenMultisetsEqual(NumberRegex, origClean, trClean);
    }

    private static bool TokenMultisetsEqual(Regex regex, string a, string b)
    {
        var tokensA = regex.Matches(a ?? "").Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal);
        var tokensB = regex.Matches(b ?? "").Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal);
        return tokensA.SequenceEqual(tokensB);
    }

    private static (bool Leading, bool Trailing) GetWhitespaceMismatch(string original, string translation)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(translation))
            return (false, false);

        bool origLeading = char.IsWhiteSpace(original[0]);
        bool trLeading = char.IsWhiteSpace(translation[0]);
        bool origTrailing = char.IsWhiteSpace(original[^1]);
        bool trTrailing = char.IsWhiteSpace(translation[^1]);

        return (origLeading != trLeading, origTrailing != trTrailing);
    }

    private static bool HasTerminalPunctuationMismatch(string original, string translation)
    {
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(translation))
            return false;

        char? GetLastPunctuation(string text)
        {
            for (int i = text.Length - 1; i >= 0; i--)
            {
                char c = text[i];

                if (char.IsWhiteSpace(c) || ClosingWrappers.Contains(c))
                    continue;

                if (TerminalPunctuation.Contains(c))
                    return c;

                break;
            }
            return null;
        }

        var origPunct = GetLastPunctuation(original);
        var trPunct = GetLastPunctuation(translation);

        if (origPunct.HasValue && !trPunct.HasValue)
            return true;

        if (origPunct.HasValue && trPunct.HasValue && origPunct.Value != trPunct.Value)
            return true;

        return false;
    }

    private static bool HasMissingSpaceAfterPunctuationMismatch(string original, string translation)
    {
        string trClean = PipeFunctionRegex.Replace(translation ?? "", " ");

        if (!MissingSpaceAfterPunctuationRegex.IsMatch(trClean))
            return false;

        string origClean = PipeFunctionRegex.Replace(original ?? "", " ");

        if (!string.IsNullOrEmpty(origClean) && MissingSpaceAfterPunctuationRegex.IsMatch(origClean))
            return false;

        return true;
    }

    private static bool HasUnbalancedSymbols(string original, string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        if (IsUnbalanced(text) && !IsUnbalanced(original))
            return true;

        return false;
    }

    private static bool IsUnbalanced(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var stack = new Stack<char>();
        bool insidePlaceholder = false;

        foreach (char c in text)
        {
            if (c == '{') insidePlaceholder = true;
            else if (c == '}') insidePlaceholder = false;

            if (insidePlaceholder)
                continue;

            switch (c)
            {
                case '(':
                case '[':
                case '«':
                case '„':
                    stack.Push(c);
                    break;

                case ')':
                    if (!TryClose(stack, '(')) return true;
                    break;
                case ']':
                    if (!TryClose(stack, '[')) return true;
                    break;
                case '»':
                    if (!TryClose(stack, '«')) return true;
                    break;

                case '“':
                    if (stack.Count > 0 && stack.Peek() == '„')
                        stack.Pop();
                    else
                        stack.Push(c);
                    break;

                case '”':
                    if (stack.Count == 0) return true;
                    char open = stack.Pop();
                    if (open != '„' && open != '“') return true;
                    break;
            }
        }

        return stack.Count > 0;
    }

    private static bool TryClose(Stack<char> stack, char expectedOpen)
    {
        if (stack.Count == 0) return false;
        return stack.Pop() == expectedOpen;
    }

    private static bool HasMismatchedTagPairsInternal(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var stack = new Stack<string>();

        foreach (Match m in TagPairRegex.Matches(text))
        {
            if (m.Value == "</>")
            {
                if (stack.Count == 0)
                    return true;

                stack.Pop();
                continue;
            }

            bool isClosing = m.Groups[1].Value == "/";
            bool isSelfClosing = m.Groups[3].Value == "/";
            string name = m.Groups[2].Value;

            if (StandaloneMarkerTags.Contains(name))
                continue;

            if (isSelfClosing)
                continue;

            if (isClosing)
            {
                if (stack.Count == 0)
                    return true;

                string open = stack.Pop();
                if (!string.Equals(open, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                stack.Push(name);
            }
        }

        return stack.Count > 0;
    }

    private static bool HasMismatchedTagPairs(string original, string translation)
    {
        if (string.IsNullOrEmpty(translation))
            return false;

        return HasMismatchedTagPairsInternal(translation) && !HasMismatchedTagPairsInternal(original ?? "");
    }
}