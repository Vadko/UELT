using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using UELT.Core.locres;
using UELT.Hikaro;

namespace UELT;

public partial class CreateLocresWindow : Window
{
    public event Action<LocresFile, string> LocresCreated;

    private readonly List<string> _filePaths = new();
    private int _rowCount = 0;

    public CreateLocresWindow()
    {
        InitializeComponent();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        bool isValid = files?.Length > 0 && files.All(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

        DragOverlay.Visibility = isValid ? Visibility.Visible : Visibility.Collapsed;
        e.Effects = isValid ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null || files.Length == 0) return;

        var valid = files.Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();

        if (valid.Count == 0) return;

        AddFiles(valid);
        e.Handled = true;
    }

    private void TxtPathTextBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        Window_DragOver(sender, e);
        e.Handled = true;
    }

    private void TxtPathTextBox_PreviewDrop(object sender, DragEventArgs e)
    {
        Window_Drop(sender, e);
        e.Handled = true;
    }

    private void BrowseTXT_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "TXT файл|*.txt",
            Title = "Вибрати TXT файл(-и)",
            Multiselect = true
        };

        if (ofd.ShowDialog() != true) return;
        AddFiles(ofd.FileNames.ToList());
    }

    private void ClearFiles_Click(object sender, RoutedEventArgs e)
    {
        _filePaths.Clear();
        _rowCount = 0;
        TxtPathTextBox.Text = "";
        TxtInfoText.Text = "Очікується TXT файл(-и) (Див. документацію)";
        CreateButton.IsEnabled = false;
    }

    private void AddFiles(List<string> paths)
    {
        foreach (var path in paths)
        {
            if (!_filePaths.Contains(path))
                _filePaths.Add(path);
        }

        RefreshFileInfo();
    }

    private void RefreshFileInfo()
    {
        if (_filePaths.Count == 0)
        {
            TxtPathTextBox.Text = "";
            TxtInfoText.Text = "";
            _rowCount = 0;
            CreateButton.IsEnabled = false;
            return;
        }

        TxtPathTextBox.Text = _filePaths.Count == 1
            ? _filePaths[0]
            : $"{_filePaths.Count}: {string.Join(", ", _filePaths.Select(System.IO.Path.GetFileName))}";

        int total = 0;
        var errors = new List<string>();

        foreach (var path in _filePaths)
        {
            try
            {
                total += TxtHelper.ParseIDFormat(path).Count;
            }
            catch (Exception ex)
            {
                errors.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}");
            }
        }

        _rowCount = total;

        if (errors.Count > 0)
            TxtInfoText.Text = $"Знайдено {_rowCount} {PluralizationHelper.GetRowsWord(_rowCount)}. Помилки: {string.Join("; ", errors)}";
        else
            TxtInfoText.Text = $"Знайдено {_rowCount} {PluralizationHelper.GetRowsWord(_rowCount)} у {_filePaths.Count} {PluralizationHelper.GetFilesWord_CreateLocresWindow(_filePaths.Count)}";

        CreateButton.IsEnabled = _rowCount > 0;
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_filePaths.Count == 0) return;

        var selectedItem = VersionComboBox.SelectedItem as ComboBoxItem;
        if (selectedItem == null) return;

        byte versionByte = byte.Parse(selectedItem.Tag.ToString());
        var version = (LocresVersion)versionByte;

        try
        {
            var locres = new LocresFile(version);
            var seenKeys = new Dictionary<string, string>();
            var duplicates = new List<string>();

            foreach (var path in _filePaths)
            {
                string fileName = System.IO.Path.GetFileName(path);
                IEnumerable<(string id, string text)> entries;
                entries = TxtHelper.ParseIDFormat(path).Select(e => (e.Id, e.Text));

                foreach (var (id, text) in entries)
                {
                    var parts = id.Split(new[] { "::" }, 2, StringSplitOptions.None);
                    string ns = parts.Length == 2 ? parts[0] : "";
                    string key = parts.Length == 2 ? parts[1] : parts[0];

                    if (string.IsNullOrWhiteSpace(key)) continue;

                    string fullKey = $"{ns}::{key}";

                    if (seenKeys.TryGetValue(fullKey, out string firstFile))
                    {
                        duplicates.Add($"{id}\n({fileName} — уже є з {firstFile})");
                    }
                    else
                    {
                        seenKeys[fullKey] = fileName;
                        locres.AddString(ns, key, text);
                    }
                }
            }

            if (locres.Sum(n => n.Count) == 0)
            {
                MessageBox.Show(this, "Файли не містять жодного правильного рядка.", "Помилка", MessageBoxButton.OK);
                return;
            }

            if (duplicates.Count > 0)
            {
                string dupList = string.Join("\n", duplicates.Take(20));
                if (duplicates.Count > 20)
                    dupList += $"\n... і ще {duplicates.Count - 20}";

                var result = MessageBox.Show(this, $"Знайдено {duplicates.Count} {PluralizationHelper.GetDuplicatesWord(duplicates.Count)} ID — буде залишено перший:\n{dupList}\n\nПродовжити створення locres?", "Дублікати ключів", MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes) return;
            }

            string suggestedPath = System.IO.Path.ChangeExtension(_filePaths[0], ".locres");
            LocresCreated?.Invoke(locres, suggestedPath);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $":{ex.Message}", "Не вдалося створити locres", MessageBoxButton.OK);
        }
    }
}