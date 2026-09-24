using System.Windows;
using UELT.Hikaro;

namespace UELT;

public partial class SettingsWindow : Window
{
    private bool _suppressUseAllHandler;

    public SettingsWindow()
    {
        InitializeComponent();
        CreateStatusFilesCheckBox.IsChecked = SettingsManager.CreateStatusFiles;
        DiscordPresenceCheckBox.IsChecked = SettingsManager.DiscordPresenceEnabled;
        CV2DecryptCheckBox.IsChecked = SettingsManager.CV2DecryptEnabled;

        if (SettingsManager.CV2IsDemo)
            CV2DemoRadio.IsChecked = true;
        else
            CV2ReleaseRadio.IsChecked = true;

        MemoriesListBox.ItemsSource = TranslationMemoryManager.Instance.Memories;
        MemoriesListBox.SelectionChanged += (_, _) => DeleteMemoryButton.IsEnabled = MemoriesListBox.SelectedItem != null;
        Closing += (_, _) =>
        {
            TranslationMemoryManager.Instance.SaveState();
            UnsubscribeMemoryEvents();
        };

        SubscribeMemoryEvents();
        UpdateUseAllCheckboxState();

        MaxVariantsSlider.Value = SettingsManager.MaxTranslationMemoryVariants;
        MaxVariantsLabel.Text = $"Максимум варіантів перекладу: {SettingsManager.MaxTranslationMemoryVariants}";

        Loaded += (_, _) =>
        {
            var openTabs = ((Owner as MainWindow)?.GetAllTabs() ?? new List<FileTabState>())
                .Where(t => !string.IsNullOrEmpty(t.FilePath))
                .ToList();

            bool anyOpenFiles = openTabs.Count > 0;

            bool hasRecordableChanges = openTabs.Any(tab => tab.DataRows.Any(row =>
                row.IsModified &&
                row.Translation != row.Text &&
                TranslationMemoryManager.IsMeaningfulTranslation(row.Translation)));

            RecordAllTranslationsButton.IsEnabled = anyOpenFiles && hasRecordableChanges;
            ApplyMemoryTranslationsButton.IsEnabled = anyOpenFiles;
        };
    }

    private void UpdateUseAllCheckboxState()
    {
        var memories = TranslationMemoryManager.Instance.Memories;
        _suppressUseAllHandler = true;
        UseAllMemoriesCheckBox.IsChecked = memories.Count > 0 && memories.All(m => m.IsActive);
        _suppressUseAllHandler = false;
    }

    private void SubscribeMemoryEvents()
    {
        foreach (var m in TranslationMemoryManager.Instance.Memories)
            m.PropertyChanged += Memory_PropertyChanged;

        TranslationMemoryManager.Instance.Memories.CollectionChanged += Memories_CollectionChanged;
    }

    private void UnsubscribeMemoryEvents()
    {
        foreach (var m in TranslationMemoryManager.Instance.Memories)
            m.PropertyChanged -= Memory_PropertyChanged;

        TranslationMemoryManager.Instance.Memories.CollectionChanged -= Memories_CollectionChanged;
    }

    private void Memories_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (TranslationMemory m in e.OldItems)
                m.PropertyChanged -= Memory_PropertyChanged;

        if (e.NewItems != null)
            foreach (TranslationMemory m in e.NewItems)
                m.PropertyChanged += Memory_PropertyChanged;

        UpdateUseAllCheckboxState();
    }

    private void Memory_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranslationMemory.IsActive))
            UpdateUseAllCheckboxState();
    }

    private void UseAllMemoriesCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressUseAllHandler) return;
        bool value = UseAllMemoriesCheckBox.IsChecked == true;
        foreach (var m in TranslationMemoryManager.Instance.Memories)
            m.IsActive = value;
    }

    private void MaxVariantsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxVariantsLabel == null) return;
        MaxVariantsLabel.Text = $"Максимум варіантів перекладу: {(int)e.NewValue}";
    }

    private void WriteTargetRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TranslationMemory tm)
            TranslationMemoryManager.Instance.SetWriteTarget(tm);
    }

    private void RecordAllTranslations_Click(object sender, RoutedEventArgs e)
    {
        var target = TranslationMemoryManager.Instance.Memories.FirstOrDefault(m => m.IsActive && m.IsWriteTarget);

        if (target == null)
        {
            MessageBox.Show(this, "Немає активної пам’яті для запису.\nОберіть ціль запису серед активних пам’ятей.", "Пам’ять перекладів", MessageBoxButton.OK);
            return;
        }

        var tabs = (Owner as MainWindow)?.GetAllTabs() ?? new List<FileTabState>();
        int recorded = 0;

        foreach (var tab in tabs)
        {
            foreach (var row in tab.DataRows)
            {
                if (!row.IsModified) continue;
                if (row.Translation == row.Text) continue;
                if (!TranslationMemoryManager.IsMeaningfulTranslation(row.Translation)) continue;

                TranslationMemoryManager.Instance.RecordTranslation(row.Text, row.Translation);
                recorded++;
            }
        }

        TranslationMemoryManager.Instance.SaveDirty();

        MessageBox.Show(this, recorded > 0
                ? $"Записано {recorded} {PluralizationHelper.GetTranslateWord(recorded)} у пам’ять «{target.Name}»."
                : "Немає змінених рядків для запису.", "Пам’ять перекладів", MessageBoxButton.OK);
    }

    private void ApplyMemoryTranslations_Click(object sender, RoutedEventArgs e)
    {
        var activeMemories = TranslationMemoryManager.Instance.Memories.Where(m => m.IsActive).ToList();

        if (activeMemories.Count == 0)
        {
            MessageBox.Show(this, "Активуйте хоча б одну пам’ять перед застосуванням.", "Немає активованих пам’ятей перекладу", MessageBoxButton.OK);
            return;
        }

        var confirm = MessageBox.Show(this,
            "Застосувати переклади з активованих пам’ятей перекладу до всіх вкладок?\n\n" +
            "Заповнюватимуться ЛИШЕ неперекладені рядки — для кожного збігу буде обрано перший (найновіший) варіант перекладу.\n" +
            "Дію можна скасувати через Undo (Ctrl+Z) окремо в кожній вкладці.",
            "Застосування перекладів із пам’яті", MessageBoxButton.YesNo);

        if (confirm != MessageBoxResult.Yes)
            return;

        var tabs = (Owner as MainWindow)?.GetAllTabs() ?? new List<FileTabState>();
        var affectedTabs = new List<FileTabState>();
        int appliedRows = 0;

        foreach (var tab in tabs)
        {
            var changes = new List<TranslationSnapshot>();

            foreach (var row in tab.DataRows)
            {
                if (!string.IsNullOrEmpty(row.Translation) && row.Translation != row.Text)
                    continue;

                string match = null;
                foreach (var m in activeMemories)
                {
                    if (m.TryGetFirstTranslation(row.Text, out var t) && !string.IsNullOrEmpty(t))
                    {
                        match = t;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(match) || match == row.Text)
                    continue;

                string oldTranslation = row.Translation ?? "";
                row.Translation = match;
                changes.Add(new TranslationSnapshot(row.Index, oldTranslation, match));
                appliedRows++;
            }

            if (changes.Count == 0) continue;

            tab.UndoRedo.Push(new UndoRedoAction
            {
                Description = $"Пам’ять перекладів: застосовано {changes.Count} {PluralizationHelper.GetTranslateWord(changes.Count)}",
                Changes = changes
            });
            tab.HasUnsavedChanges = true;
            affectedTabs.Add(tab);
        }

        (Owner as MainWindow)?.RefreshAfterBulkTranslationApply(affectedTabs);

        MessageBox.Show(this, appliedRows > 0
                ? $"Застосовано {appliedRows} {PluralizationHelper.GetTranslateWord(appliedRows)} у {affectedTabs.Count} {PluralizationHelper.GetTabsWordLocative(affectedTabs.Count)}"
                : "Не знайдено рядків для застосування перекладу.", "Пам’ять перекладів", MessageBoxButton.OK);
    }

    private void CreateMemory_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new TranslationMemoryNameDialog { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultName)) return;

        TranslationMemoryManager.Instance.CreateNew(dlg.ResultName.Trim());
        UpdateUseAllCheckboxState();
    }

    private void DeleteMemory_Click(object sender, RoutedEventArgs e)
    {
        if (MemoriesListBox.SelectedItem is not TranslationMemory tm) return;

        var result = MessageBox.Show(this, $"Видалити пам’ять «{tm.Name}»?\nФайл на диску буде видалено.", "Підтвердження", MessageBoxButton.YesNo);

        if (result != MessageBoxResult.Yes)
            return;

        TranslationMemoryManager.Instance.DeleteMemory(tm);
        UpdateUseAllCheckboxState();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.SetCreateStatusFiles(CreateStatusFilesCheckBox.IsChecked == true);
        SettingsManager.SetDiscordPresenceEnabled(DiscordPresenceCheckBox.IsChecked == true);
        SettingsManager.SetCV2DecryptEnabled(CV2DecryptCheckBox.IsChecked == true);
        SettingsManager.SetCV2IsDemo(CV2DemoRadio.IsChecked == true);
        SettingsManager.SetMaxTranslationMemoryVariants((int)MaxVariantsSlider.Value);
        TranslationMemoryManager.Instance.SaveState();
        DialogResult = true;
    }
}