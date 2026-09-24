using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace UELT.Hikaro;

public partial class GlobalStatsWindow : Window
{
    private List<FileTabState> _tabs;
    private CancellationTokenSource _cts = new();
    private readonly ObservableCollection<GlobalStatsRow> _rows = new();
    private int _pending;
    private bool _isLoading = false;

    public GlobalStatsWindow(List<FileTabState> tabs)
    {
        InitializeComponent();
        _tabs = tabs;
        StatsGrid.ItemsSource = _rows;
        Loaded += async (_, _) => await LoadStatsAsync();
    }

    private void Window_Closed(object sender, System.EventArgs e)
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        RefreshButton.IsEnabled = false;
        RefreshButton.Content = "Оновлення...";

        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        _rows.Clear();
        await LoadStatsAsync();

        RefreshButton.IsEnabled = true;
        RefreshButton.Content = "Оновити";
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(BuildClipboardText());

    private async Task LoadStatsAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        var token = _cts.Token;

        if (_tabs.Count == 0)
        {
            SummaryText.Text = "Немає відкритих файлів.";
            _isLoading = false;
            return;
        }

        _pending = _tabs.Count;

        foreach (var tab in _tabs)
            _rows.Add(new GlobalStatsRow(Path.GetFileName(tab.FilePath)));

        var tasks = _tabs.Select(async (tab, i) =>
        {
            StatisticsManager.StatsResult result;
            try
            {
                result = await StatisticsManager.CalculateStatsAsync(tab.DataRows, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested) return;

            tab.CachedTotalWords = result.TotalWords;
            tab.CachedTranslatedRows = result.TranslatedRows;
            tab.CachedTranslatedWords = result.TranslatedWords;
            tab.CachedApprovedRows = result.ApprovedRows;
            tab.CachedApprovedWords = result.ApprovedWords;

            _rows[i].Update(tab.DataRows.Count, result);
            _pending--;
            Dispatcher.Invoke(() =>
            {
                UpdateSummary();
                RefreshColumnWidths();
            });
        });

        await Task.WhenAll(tasks);
        UpdateSummary();
        RefreshColumnWidths();
        _isLoading = false;
    }

    private void UpdateSummary()
    {
        int totalFiles = _tabs.Count;
        int totalRows = _tabs.Sum(t => t.DataRows.Count);
        int totalWords = _tabs.Sum(t => t.CachedTotalWords);
        int translatedRows = _tabs.Sum(t => t.CachedTranslatedRows);
        int translatedWords = _tabs.Sum(t => t.CachedTranslatedWords);
        int approvedRows = _tabs.Sum(t => t.CachedApprovedRows);
        int approvedWords = _tabs.Sum(t => t.CachedApprovedWords);

        double rowsPct = totalRows > 0 ? translatedRows * 100.0 / totalRows : 0;
        double wordsPct = totalWords > 0 ? translatedWords * 100.0 / totalWords : 0;
        double approvedRowsPct = totalRows > 0 ? approvedRows * 100.0 / totalRows : 0;
        double approvedWordsPct = translatedWords > 0 ? approvedWords * 100.0 / translatedWords : 0;

        string pendingSuffix = _pending > 0 ? $"   (рахується, ще {_pending})" : "";

        SummaryText.Text =
            $"Файлів: {totalFiles}   Рядків: {N(totalRows)}   Слів: {N(totalWords)}{pendingSuffix}\n" +
            $"Загалом перекладено: {N(translatedRows)} {PluralizationHelper.GetRowsWord(translatedRows)} ({rowsPct:F2}%), {N(translatedWords)} {PluralizationHelper.GetWordsWord(translatedWords)} ({wordsPct:F2}%)\n" +
            $"Загалом затверджено: {N(approvedRows)} {PluralizationHelper.GetRowsWord(approvedRows)} ({approvedRowsPct:F2}%), {N(approvedWords)} {PluralizationHelper.GetWordsWord(approvedWords)} ({approvedWordsPct:F2}%)";
    }

    private string BuildClipboardText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(SummaryText.Text);
        sb.AppendLine("─────────────────────────────");
        foreach (var r in _rows)
            sb.AppendLine($"{r.FileName}: {r.RowsText} {PluralizationHelper.GetRowsWord(int.Parse(r.RowsText.Replace(",", "")))}, {r.WordsText} {PluralizationHelper.GetWordsWord(int.Parse(r.WordsText.Replace(",", "")))}, перекл. {r.TranslatedRowsText} / {r.TranslatedWordsText}, затв. {r.ApprovedRowsText} / {r.ApprovedWordsText}");
        return sb.ToString();
    }

    public void Refresh(List<FileTabState> tabs)
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        _tabs = tabs;
        _rows.Clear();

        _ = LoadStatsAsync();
    }

    private void RefreshColumnWidths()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (var column in StatsGrid.Columns)
            {
                if (!column.Width.IsAuto) continue;

                var width = column.Width;
                column.Width = new DataGridLength(0);
                column.Width = width;
            }
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static string N(int n) => n.ToString("N0");
}

public class GlobalStatsRow : INotifyPropertyChanged
{
    public GlobalStatsRow(string fileName) => FileName = fileName;

    public string FileName { get; }

    private string _rowsText = "...";
    public string RowsText { get => _rowsText; private set { _rowsText = value; OnChanged(nameof(RowsText)); } }

    private string _wordsText = "...";
    public string WordsText { get => _wordsText; private set { _wordsText = value; OnChanged(nameof(WordsText)); } }

    private string _translatedRowsText = "...";
    public string TranslatedRowsText { get => _translatedRowsText; private set { _translatedRowsText = value; OnChanged(nameof(TranslatedRowsText)); } }

    private string _translatedWordsText = "...";
    public string TranslatedWordsText { get => _translatedWordsText; private set { _translatedWordsText = value; OnChanged(nameof(TranslatedWordsText)); } }

    private string _approvedRowsText = "...";
    public string ApprovedRowsText { get => _approvedRowsText; private set { _approvedRowsText = value; OnChanged(nameof(ApprovedRowsText)); } }

    private string _approvedWordsText = "...";
    public string ApprovedWordsText { get => _approvedWordsText; private set { _approvedWordsText = value; OnChanged(nameof(ApprovedWordsText)); } }

    public void Update(int rowCount, StatisticsManager.StatsResult r)
    {
        double rowsPct = rowCount > 0 ? r.TranslatedRows * 100.0 / rowCount : 0;
        double wordsPct = r.TotalWords > 0 ? r.TranslatedWords * 100.0 / r.TotalWords : 0;
        double apprRowsPct = rowCount > 0 ? r.ApprovedRows * 100.0 / rowCount : 0;
        double apprWordsPct = r.TranslatedWords > 0 ? r.ApprovedWords * 100.0 / r.TranslatedWords : 0;

        RowsText = rowCount.ToString("N0");
        WordsText = r.TotalWords.ToString("N0");
        TranslatedRowsText = $"{r.TranslatedRows:N0} ({rowsPct:F2}%)";
        TranslatedWordsText = $"{r.TranslatedWords:N0} ({wordsPct:F2}%)";
        ApprovedRowsText = $"{r.ApprovedRows:N0} ({apprRowsPct:F2}%)";
        ApprovedWordsText = $"{r.ApprovedWords:N0} ({apprWordsPct:F2}%)";
    }

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}