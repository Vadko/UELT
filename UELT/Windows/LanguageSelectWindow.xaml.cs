using System.Windows;

namespace UELT;

public partial class LanguageSelectWindow : Window
{
    public string SelectedLanguage { get; private set; }
    public bool UseForAll { get; private set; }
    public LanguageSelectWindow(List<string> languages, bool showUseForAll = false,
        Dictionary<string, string> previews = null)
    {
        InitializeComponent();

        if (previews != null && previews.Count > 0)
        {
            const int maxLen = 30;
            var displayItems = languages.Select(lang =>
            {
                if (previews.TryGetValue(lang, out var raw) && !string.IsNullOrWhiteSpace(raw))
                {
                    var flat = raw.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim();
                    var preview = flat.Length > maxLen ? flat[..maxLen] + "…" : flat;
                    return $"{lang}: {preview}";
                }
                return lang;
            }).ToList();

            LanguageComboBox.ItemsSource = displayItems;
            LanguageComboBox.SelectedIndex = 0;

            _languages = languages;
        }
        else
        {
            LanguageComboBox.ItemsSource = languages;
            LanguageComboBox.SelectedIndex = 0;
            _languages = languages;
        }

        if (showUseForAll)
            UseForAllCheckBox.Visibility = Visibility.Visible;
    }

    private readonly List<string> _languages;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        int idx = LanguageComboBox.SelectedIndex;
        SelectedLanguage = (idx >= 0 && idx < _languages.Count) ? _languages[idx] : null;
        UseForAll = UseForAllCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        UseForAll = UseForAllCheckBox.IsChecked == true;
        DialogResult = false;
        Close();
    }
}