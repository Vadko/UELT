using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace UELT.Hikaro;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
            return visibility == Visibility.Visible;
        return false;
    }
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
            return visibility == Visibility.Collapsed;
        return true;
    }
}

public class SearchModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SearchMode mode && parameter is string param)
        {
            return mode.ToString() == param;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string param)
        {
            return Enum.Parse(typeof(SearchMode), param);
        }
        return SearchMode.Text;
    }
}

public class SearchFilterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SearchFilter filter && parameter is string param)
        {
            return filter.ToString() == param;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string param)
        {
            return Enum.Parse(typeof(SearchFilter), param);
        }
        return SearchFilter.Both;
    }
}

public class StringEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
            return !string.IsNullOrWhiteSpace(str);
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class HighlightData
{
    public string Text { get; set; } = "";
    public string Query { get; set; } = "";
    public StringComparison Comparison { get; set; } = StringComparison.OrdinalIgnoreCase;
    public bool IsRegex { get; set; } = false;
}

public class HighlightDataConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return new HighlightData
        {
            Text = values[0]?.ToString() ?? "",
            Query = values[1]?.ToString() ?? "",
            Comparison = values[2] is StringComparison c ? c : StringComparison.OrdinalIgnoreCase,
            IsRegex = values.Length > 3 && values[3] is bool b && b
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public static class TextBlockHighlightHelper
{
    private static readonly SolidColorBrush HighlightBrush =
        new((Color)ColorConverter.ConvertFromString("#3399ff"));

    public static readonly DependencyProperty HighlightProperty = DependencyProperty.RegisterAttached(
        "Highlight",
        typeof(HighlightData),
        typeof(TextBlockHighlightHelper),
        new PropertyMetadata(null, OnHighlightChanged));

    public static HighlightData GetHighlight(DependencyObject obj)
        => (HighlightData)obj.GetValue(HighlightProperty);

    public static void SetHighlight(DependencyObject obj, HighlightData value)
        => obj.SetValue(HighlightProperty, value);

    private static void OnHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBlock tb) return;
        tb.Inlines.Clear();

        if (e.NewValue is not HighlightData data || string.IsNullOrEmpty(data.Text))
            return;

        string text = data.Text;
        string query = data.Query;

        if (string.IsNullOrEmpty(query))
        {
            tb.Inlines.Add(new Run(text));
            return;
        }

        var matches = data.IsRegex
            ? GetRegexMatches(text, query, data.Comparison)
            : GetPlainMatches(text, query, data.Comparison);

        int lastIndex = 0;
        foreach (var (index, length) in matches)
        {
            if (index > lastIndex)
                tb.Inlines.Add(new Run(text[lastIndex..index]));

            string matchText = text.Substring(index, length);
            int i = 0;
            while (i < matchText.Length)
            {
                bool isSpace = matchText[i] == ' ';
                int j = i + 1;
                while (j < matchText.Length && (matchText[j] == ' ') == isSpace)
                    j++;

                tb.Inlines.Add(new Run(isSpace ? new string('•', j - i) : matchText[i..j])
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = HighlightBrush
                });
                i = j;
            }

            lastIndex = index + length;
        }

        if (lastIndex < text.Length)
            tb.Inlines.Add(new Run(text[lastIndex..]));
    }

    private static List<(int index, int length)> GetPlainMatches(string text, string query, StringComparison comparison)
    {
        var result = new List<(int, int)>();
        int i = 0;
        int idx;
        while ((idx = text.IndexOf(query, i, comparison)) != -1)
        {
            result.Add((idx, query.Length));
            i = idx + query.Length;
        }
        return result;
    }

    private static List<(int index, int length)> GetRegexMatches(string text, string query, StringComparison comparison)
    {
        var result = new List<(int, int)>();
        try
        {
            var options = comparison == StringComparison.OrdinalIgnoreCase
                ? RegexOptions.IgnoreCase
                : RegexOptions.None;
            foreach (Match m in Regex.Matches(text, query, options))
                result.Add((m.Index, m.Length));
        }
        catch (ArgumentException) { }
        return result;
    }
}

public class SpellCheckHighlightData
{
    public string Text { get; set; } = "";
    public IReadOnlyCollection<string> MisspelledWords { get; set; } = Array.Empty<string>();
}

public class SpellCheckHighlightDataConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return new SpellCheckHighlightData
        {
            Text = values[0]?.ToString() ?? "",
            MisspelledWords = values[1] as IReadOnlyCollection<string> ?? Array.Empty<string>()
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public static class SpellCheckHighlightHelper
{
    private static readonly SolidColorBrush ErrorBrush =
        new((Color)ColorConverter.ConvertFromString("#e05252"));

    public static readonly DependencyProperty HighlightProperty = DependencyProperty.RegisterAttached(
        "Highlight",
        typeof(SpellCheckHighlightData),
        typeof(SpellCheckHighlightHelper),
        new PropertyMetadata(null, OnHighlightChanged));

    public static SpellCheckHighlightData GetHighlight(DependencyObject obj)
        => (SpellCheckHighlightData)obj.GetValue(HighlightProperty);

    public static void SetHighlight(DependencyObject obj, SpellCheckHighlightData value)
        => obj.SetValue(HighlightProperty, value);

    private static void OnHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBlock tb) return;
        tb.Inlines.Clear();

        if (e.NewValue is not SpellCheckHighlightData data || string.IsNullOrEmpty(data.Text))
            return;

        if (data.MisspelledWords == null || data.MisspelledWords.Count == 0)
        {
            tb.Inlines.Add(new Run(data.Text));
            return;
        }

        var text = data.Text;
        var ranges = new List<(int start, int end)>();

        foreach (var word in data.MisspelledWords)
        {
            int i = 0;
            while (true)
            {
                int idx = text.IndexOf(word, i, StringComparison.Ordinal);
                if (idx < 0) break;
                bool wordStart = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
                bool wordEnd = idx + word.Length >= text.Length || !char.IsLetterOrDigit(text[idx + word.Length]);
                if (wordStart && wordEnd)
                    ranges.Add((idx, idx + word.Length));
                i = idx + 1;
            }
        }

        ranges.Sort((a, b) => a.start.CompareTo(b.start));

        var merged = new List<(int, int)>();
        foreach (var r in ranges)
        {
            if (merged.Count > 0 && r.start < merged[^1].Item2)
                merged[^1] = (merged[^1].Item1, Math.Max(merged[^1].Item2, r.end));
            else
                merged.Add(r);
        }

        int last = 0;
        foreach (var (start, end) in merged)
        {
            if (start > last)
                tb.Inlines.Add(new Run(text[last..start]));

            tb.Inlines.Add(new Run(text[start..end])
            {
                Foreground = ErrorBrush,
                FontWeight = FontWeights.SemiBold,
                TextDecorations = TextDecorations.Underline
            });
            last = end;
        }

        if (last < text.Length)
            tb.Inlines.Add(new Run(text[last..]));
    }
}