using System.Windows;
using System.Windows.Input;

namespace UELT.Hikaro;

public partial class AddTranslationDialog : Window
{
    public string ResultTranslation { get; private set; } = "";
    private readonly string _original;

    public AddTranslationDialog(string original)
    {
        InitializeComponent();
        _original = original;
        TitleText.Text = $"Новий варіант перекладу для «{original}»";
        TranslationBox.Focus();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ResultTranslation = TranslationBox.Text.Trim();
        if (string.IsNullOrEmpty(ResultTranslation))
        {
            MessageBox.Show(this, "Введіть переклад.", "Глосарій", MessageBoxButton.OK);
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