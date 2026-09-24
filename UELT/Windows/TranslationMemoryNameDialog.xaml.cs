using System.Windows;

namespace UELT;

public partial class TranslationMemoryNameDialog : Window
{
    public string ResultName { get; private set; } = "";

    public TranslationMemoryNameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameTextBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string name = NameTextBox.Text.Trim();

        if (!TranslationMemoryManager.IsValidMemoryName(name, out string error))
        {
            MessageBox.Show(this, error, "Некоректна назва", MessageBoxButton.OK);
            return;
        }

        if (TranslationMemoryManager.Instance.MemoryNameExists(name))
        {
            MessageBox.Show(this, $"Пам’ять з назвою «{name}» вже існує.", "Пам’ять перекладів", MessageBoxButton.OK);
            return;
        }

        ResultName = name;
        DialogResult = true;
    }
}