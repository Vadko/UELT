using System.Windows;
using System.Windows.Input;

namespace UELT.Hikaro;

public partial class AddToGlossaryDialog : Window
{
    public string ResultOriginal { get; private set; } = "";
    public string ResultTranslation { get; private set; } = "";

    public AddToGlossaryDialog(string originalText, string suggestedTranslation)
    {
        InitializeComponent();
        OriginalTextBox.Text = originalText ?? "";
        TranslationBox.Text = suggestedTranslation ?? "";

        Loaded += (s, e) =>
        {
            if (string.IsNullOrEmpty(TranslationBox.Text))
                TranslationBox.Focus();
            else
                TranslationBox.SelectAll();
        };
    }

    private void OriginalTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        SelectedOriginalBox.Text = OriginalTextBox.SelectedText.Trim();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ResultOriginal = OriginalTextBox.SelectedText.Trim();
        ResultTranslation = TranslationBox.Text;

        if (string.IsNullOrEmpty(ResultOriginal))
        {
            MessageBox.Show(this, "Виділіть слово або фразу в оригінальному тексті.", "Глосарій", MessageBoxButton.OK);
            return;
        }

        if (string.IsNullOrEmpty(ResultTranslation))
        {
            MessageBox.Show(this, "Відсутній переклад.", "Глосарій", MessageBoxButton.OK);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TranslationBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OK_Click(sender, e);
        if (e.Key == Key.Escape) Cancel_Click(sender, e);
    }
}