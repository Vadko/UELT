using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace UELT.Hikaro;

public class GlossaryEntry : INotifyPropertyChanged
{
    private string _original = "";
    private ObservableCollection<string> _translations = new();
    private Regex _cachedRegex;

    public string Original
    {
        get => _original;
        set
        {
            _original = value;
            _cachedRegex = null;
            OnPropertyChanged(nameof(Original));
        }
    }

    public Regex GetMatchRegex()
    {
        if (_cachedRegex != null) return _cachedRegex;

        string escaped = Regex.Escape(_original);
        string leadingBoundary = _original.Length > 0 && IsWordChar(_original[0]) ? @"\b" : "";
        string trailingBoundary = _original.Length > 0 && IsWordChar(_original[^1]) ? @"\b" : "";

        _cachedRegex = new Regex(
            $@"{leadingBoundary}{escaped}{trailingBoundary}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        return _cachedRegex;
    }

    internal static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    public ObservableCollection<string> Translations
    {
        get => _translations;
        set { _translations = value; OnPropertyChanged(nameof(Translations)); }
    }

    public string PrimaryTranslation
    {
        get => _translations.Count > 0 ? _translations[0] : "";
        set
        {
            if (_translations.Count > 0)
                _translations[0] = value;
            else
                _translations.Add(value);
            OnPropertyChanged(nameof(PrimaryTranslation));
        }
    }

    public string TranslationsDisplay => string.Join(" / ", _translations);

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}