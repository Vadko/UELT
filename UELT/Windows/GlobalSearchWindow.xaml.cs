using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UELT.Hikaro;

namespace UELT;

public class GlobalSearchResultItem : SearchResultItem
{
    public FileTabState Tab { get; set; }
    public string FileName { get; set; } = "";
    public int RowNumber { get; set; }
    public int DisplayRowNumber => RowNumber + 1;
    public string ColumnLabel { get; set; } = "";
    public IReadOnlyCollection<string> MisspelledWords { get; set; } = Array.Empty<string>();
    public bool IsSpellCheckResult => MisspelledWords != null && MisspelledWords.Count > 0;
}

public partial class GlobalSearchWindow : Window
{
    private readonly Func<IReadOnlyList<FileTabState>> _getTabsFunc;
    private readonly Action<FileTabState, int> _navigateToRowAction;
    private readonly RecentItemsManager<string> _recentSearchesManager;

    private readonly List<GlobalSearchResultItem> _allResults = new();

    private readonly ObservableCollection<GlobalSearchResultItem> _displayResults = new();

    private SearchFilter _currentFilter = SearchFilter.All;

    private StringComparison _lastComparison = StringComparison.OrdinalIgnoreCase;
    private SearchMode _searchMode = SearchMode.Text;
    private bool _searchWasRun = false;
    public string LastQuery => SearchComboBox.Text;

    public GlobalSearchWindow(Func<IReadOnlyList<FileTabState>> getTabsFunc, Action<FileTabState, int> navigateToRowAction, RecentItemsManager<string> recentSearchesManager,
        string initialQuery = "")
    {
        _getTabsFunc = getTabsFunc;
        _navigateToRowAction = navigateToRowAction;
        _recentSearchesManager = recentSearchesManager;

        InitializeComponent();

        ResultsList.ItemsSource = _displayResults;
        SearchComboBox.ItemsSource = _recentSearchesManager.GetAll();

        if (!string.IsNullOrEmpty(initialQuery))
            SearchComboBox.Text = initialQuery;

        Loaded += (s, e) => FocusSearchBox();
    }

    private void FocusSearchBox()
    {
        SearchComboBox.Focus();
        if (SearchComboBox.Template.FindName("PART_EditableTextBox", SearchComboBox) is TextBox editBox)
        {
            editBox.CaretIndex = editBox.Text.Length;
            editBox.Focus();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (SearchComboBox.IsDropDownOpen)
            {
                SearchComboBox.IsDropDownOpen = false;
                e.Handled = true;
            }
            else
            {
                Close();
            }
        }
    }

    private void SearchComboBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) RunSearch();
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => RunSearch();

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is GlobalSearchResultItem item)
            _navigateToRowAction(item.Tab, item.TermIndex);
    }

    private void SearchModeToggle_Click(object sender, RoutedEventArgs e)
    {
        _searchMode = _searchMode switch
        {
            SearchMode.Text => SearchMode.ID,
            SearchMode.ID => SearchMode.Regex,
            SearchMode.Regex => SearchMode.SpellCheck,
            SearchMode.SpellCheck => SearchMode.Text,
            _ => SearchMode.Text
        };

        SearchModeToggle.Content = _searchMode switch
        {
            SearchMode.Text => "Текст",
            SearchMode.ID => "ID",
            SearchMode.Regex => "Regex",
            SearchMode.SpellCheck => "Орфог",
            _ => "Текст"
        };

        bool isRegex = _searchMode == SearchMode.Regex;
        bool isSpell = _searchMode == SearchMode.SpellCheck;

        WholeWordCheckBox.IsEnabled = !isRegex && !isSpell;
        ExactMatchCheckBox.IsEnabled = !isRegex && !isSpell;
        CaseSensitiveCheckBox.IsEnabled = !isSpell;
        SearchComboBox.IsEnabled = !isSpell;

        if (!isRegex && !isSpell)
        {
            if (WholeWordCheckBox.IsChecked == true)
                ExactMatchCheckBox.IsEnabled = false;
            else if (ExactMatchCheckBox.IsChecked == true)
                WholeWordCheckBox.IsEnabled = false;
        }

        _allResults.Clear();
        _displayResults.Clear();
        StatusText.Text = "";
        _searchWasRun = false;
        _currentFilter = SearchFilter.All;
        UpdateFilterButtonStyles();
        UpdateFilterPanelVisibility();
    }

    private void TxtClear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is TextBox tb)
        {
            tb.Clear();
            tb.Focus();
        }
    }

    private void FilterBothButton_Click(object sender, RoutedEventArgs e) => ApplyFilter(SearchFilter.Both);
    private void FilterOriginalButton_Click(object sender, RoutedEventArgs e) => ApplyFilter(SearchFilter.Original);
    private void FilterTranslationButton_Click(object sender, RoutedEventArgs e) => ApplyFilter(SearchFilter.Translation);
    private void FilterResetButton_Click(object sender, RoutedEventArgs e) => ApplyFilter(SearchFilter.All);

    private void ApplyFilter(SearchFilter filter)
    {
        _currentFilter = filter;
        UpdateFilterButtonStyles();
        RepopulateDisplay();
        UpdateStatusText();
    }

    private void ChkMutualExclusive_Changed(object sender, RoutedEventArgs e)
    {
        if (WholeWordCheckBox.IsChecked == true)
            ExactMatchCheckBox.IsEnabled = false;
        else if (ExactMatchCheckBox.IsChecked == true)
            WholeWordCheckBox.IsEnabled = false;
        else
        {
            WholeWordCheckBox.IsEnabled = true;
            ExactMatchCheckBox.IsEnabled = true;
        }
    }

    private void RunSearch()
    {
        if (_searchMode == SearchMode.SpellCheck)
        {
            RunSpellCheck();
            return;
        }

        string query = SearchComboBox.Text ?? "";
        if (string.IsNullOrEmpty(query) || query == " ") return;

        _recentSearchesManager.Add(query);
        string saved = SearchComboBox.Text;
        SearchComboBox.ItemsSource = null;
        SearchComboBox.ItemsSource = _recentSearchesManager.GetAll();
        SearchComboBox.Text = saved;

        bool caseSensitive = CaseSensitiveCheckBox.IsChecked == true;
        bool wholeWord = WholeWordCheckBox.IsChecked == true;
        bool exactMatch = ExactMatchCheckBox.IsChecked == true;

        _lastComparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        _currentFilter = SearchFilter.All;
        UpdateFilterButtonStyles();

        var tabs = _getTabsFunc();

        if (_searchMode == SearchMode.Regex)
        {
            try { _ = new Regex(query); }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, $"Невірний regex: {ex.Message}", "Помилка", MessageBoxButton.OK);
                return;
            }
        }

        _allResults.Clear();
        _searchWasRun = true;

        foreach (var tab in tabs)
        {
            if (tab.DataRows == null || tab.DataRows.Count == 0) continue;
            string fileName = string.IsNullOrEmpty(tab.FilePath)
                ? "Новий файл"
                : System.IO.Path.GetFileName(tab.FilePath);

            foreach (var row in tab.DataRows)
            {
                if (_searchMode == SearchMode.ID)
                {
                    TryAddResult(row, "ID", row.ID ?? "", query, wholeWord, exactMatch, fileName, tab);
                }
                else
                {
                    bool inOriginal = MatchesQuery(row.Text ?? "", query, wholeWord, exactMatch);
                    bool inTranslation = MatchesQuery(row.Translation ?? "", query, wholeWord, exactMatch);

                    if (inOriginal || inTranslation)
                    {
                        string colLabel = (inOriginal && inTranslation) ? "В обох стовпцях"
                                        : inTranslation ? "У перекладі"
                                        : "В оригіналі";

                        string previewText = inTranslation ? row.Translation ?? "" : row.Text ?? "";

                        _allResults.Add(new GlobalSearchResultItem
                        {
                            Tab = tab,
                            FileName = fileName,
                            RowNumber = row.Index,
                            ColumnLabel = colLabel,
                            Term = row.ID ?? "",
                            ShowTerm = true,
                            Preview = BuildPreview(previewText, query, _lastComparison, _searchMode == SearchMode.Regex),
                            SearchQuery = query,
                            Comparison = _lastComparison,
                            TermIndex = row.Index,
                            Number = row.Index,
                            ColumnType = (inOriginal && inTranslation) ? "both"
                                        : inTranslation ? "translation"
                                        : "original",
                            IsRegex = _searchMode == SearchMode.Regex,
                        });
                    }
                }
            }
        }

        RepopulateDisplay();
        UpdateStatusText();
        UpdateFilterPanelVisibility();
    }

    private void RunSpellCheck()
    {
        if (!SpellCheckService.IsAvailable)
        {
            StatusText.Text = "Словник недоступний";
            return;
        }

        _allResults.Clear();
        _searchWasRun = true;
        _currentFilter = SearchFilter.All;
        UpdateFilterButtonStyles();

        var tabs = _getTabsFunc();

        foreach (var tab in tabs)
        {
            if (tab.DataRows == null || tab.DataRows.Count == 0) continue;
            string fileName = string.IsNullOrEmpty(tab.FilePath)
                ? "Новий файл"
                : System.IO.Path.GetFileName(tab.FilePath);

            foreach (var row in tab.DataRows)
            {
                var errorsInTranslation = SpellCheckService.GetMisspelledWords(row.Translation ?? "").ToList();
                if (errorsInTranslation.Count == 0) continue;

                string previewText = row.Translation ?? "";

                _allResults.Add(new GlobalSearchResultItem
                {
                    Tab = tab,
                    FileName = fileName,
                    RowNumber = row.Index,
                    ColumnLabel = "У перекладі",
                    Term = row.ID ?? "",
                    ShowTerm = true,
                    Preview = BuildSpellCheckPreview(previewText, errorsInTranslation),
                    SearchQuery = "",
                    Comparison = StringComparison.Ordinal,
                    TermIndex = row.Index,
                    Number = row.Index,
                    ColumnType = "translation",
                    IsRegex = false,
                    MisspelledWords = errorsInTranslation.AsReadOnly(),
                });
            }
        }

        RepopulateDisplay();
        UpdateStatusText();
        UpdateFilterPanelVisibility();
    }

    private static string BuildSpellCheckPreview(string text, List<string> misspelledWords)
    {
        const int maxLength = 200;
        if (text.Length <= maxLength) return text;

        int firstErrorIndex = text.Length;
        foreach (var word in misspelledWords)
        {
            int idx = text.IndexOf(word, StringComparison.Ordinal);
            if (idx >= 0 && idx < firstErrorIndex)
                firstErrorIndex = idx;
        }

        if (firstErrorIndex == text.Length)
            return text[..maxLength] + "…";

        int start = Math.Max(0, firstErrorIndex - 40);
        int end = Math.Min(text.Length, start + maxLength);
        if (end == text.Length && end - start < maxLength)
            start = Math.Max(0, end - maxLength);

        string preview = text[start..end];
        if (start > 0) preview = "…" + preview;
        if (end < text.Length) preview += "…";
        return preview;
    }

    private void RepopulateDisplay()
    {
        ResultsList.ItemsSource = null;
        _displayResults.Clear();

        foreach (var item in _allResults)
        {
            if (_searchMode == SearchMode.ID)
            {
                _displayResults.Add(item);
            }
            else
            {
                bool show = _currentFilter switch
                {
                    SearchFilter.All => true,
                    SearchFilter.Both => item.ColumnType == "both",
                    SearchFilter.Original => item.ColumnType == "original",
                    SearchFilter.Translation => item.ColumnType == "translation",
                    _ => true
                };

                if (_searchMode == SearchMode.SpellCheck || show)
                    _displayResults.Add(item);
            }
        }

        ResultsList.ItemsSource = _displayResults;
    }

    private void TryAddResult(DataGridItem row, string columnLabel, string columnText, string query, bool wholeWord, bool exactMatch, string fileName, FileTabState tab)
    {
        if (!MatchesQuery(columnText, query, wholeWord, exactMatch)) return;

        _allResults.Add(new GlobalSearchResultItem
        {
            Tab = tab,
            FileName = fileName,
            RowNumber = row.Index,
            ColumnLabel = columnLabel,
            Term = row.ID ?? "",
            ShowTerm = false,
            Preview = BuildPreview(columnText, query, _lastComparison, _searchMode == SearchMode.Regex),
            SearchQuery = query,
            Comparison = _lastComparison,
            TermIndex = row.Index,
            Number = row.Index,
            ColumnType = "id",
            IsRegex = _searchMode == SearchMode.Regex,
        });
    }

    private bool MatchesQuery(string text, string query, bool wholeWord, bool exactMatch)
    {
        if (string.IsNullOrEmpty(text)) return false;

        if (_searchMode == SearchMode.Regex)
        {
            try
            {
                var options = _lastComparison == StringComparison.OrdinalIgnoreCase
                    ? RegexOptions.IgnoreCase
                    : RegexOptions.None;
                return Regex.IsMatch(text, query, options);
            }
            catch (ArgumentException) { return false; }
        }

        if (exactMatch) return text.Equals(query, _lastComparison);
        if (wholeWord) return IsWholeWordMatch(text, query, _lastComparison);
        return text.IndexOf(query, _lastComparison) >= 0;
    }

    private bool IsWholeWordMatch(string text, string searchText, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int index = 0;
        while ((index = text.IndexOf(searchText, index, comparison)) != -1)
        {
            bool isWordStart = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            bool isWordEnd = index + searchText.Length >= text.Length
                               || !char.IsLetterOrDigit(text[index + searchText.Length]);
            if (isWordStart && isWordEnd) return true;
            index++;
        }
        return false;
    }

    private static string BuildPreview(string text, string query, StringComparison comparison, bool isRegex = false)
    {
        const int maxLength = 200;
        int queryIndex = isRegex
            ? (Regex.Match(text, query) is { Success: true } m ? m.Index : -1)
            : text.IndexOf(query, comparison);

        if (queryIndex >= 0 && text.Length > maxLength)
        {
            int start = Math.Max(0, queryIndex - 30);
            int end = Math.Min(text.Length, start + maxLength);
            if (end == text.Length && end - start < maxLength)
                start = Math.Max(0, end - maxLength);

            string preview = text.Substring(start, end - start);
            if (start > 0) preview = "…" + preview;
            if (end < text.Length) preview += "…";
            return preview;
        }

        return text.Length > maxLength ? text.Substring(0, maxLength) + "…" : text;
    }

    private void UpdateStatusText()
    {
        int shown = _displayResults.Count;
        int total = _allResults.Count;

        if (!_searchWasRun)
        {
            StatusText.Text = "";
            return;
        }

        StatusText.Text = total == 0
            ? "Нічого не знайдено"
            : shown == total
                ? $"Знайдено: {total}"
                : $"{shown}/{total}";
    }

    private void UpdateFilterPanelVisibility()
    {
        bool isSpell = _searchMode == SearchMode.SpellCheck;
        bool isId = _searchMode == SearchMode.ID;

        bool enableFilters = !isSpell && !isId && _searchWasRun;

        FilterBothButton.IsEnabled = enableFilters;
        FilterOriginalButton.IsEnabled = enableFilters;
        FilterTranslationButton.IsEnabled = enableFilters;
        FilterResetButton.IsEnabled = enableFilters;
    }

    private void UpdateFilterButtonStyles()
    {
        FilterBothButton.FontWeight = FontWeights.Normal;
        FilterOriginalButton.FontWeight = FontWeights.Normal;
        FilterTranslationButton.FontWeight = FontWeights.Normal;
        FilterResetButton.FontWeight = FontWeights.Normal;

        switch (_currentFilter)
        {
            case SearchFilter.All: FilterResetButton.FontWeight = FontWeights.Bold; break;
            case SearchFilter.Both: FilterBothButton.FontWeight = FontWeights.Bold; break;
            case SearchFilter.Original: FilterOriginalButton.FontWeight = FontWeights.Bold; break;
            case SearchFilter.Translation: FilterTranslationButton.FontWeight = FontWeights.Bold; break;
        }
    }

    private void SearchComboBox_DropDownOpened(object sender, EventArgs e)
    {
        string currentText = SearchComboBox.Text;
        SearchComboBox.ItemsSource = null;
        SearchComboBox.ItemsSource = _recentSearchesManager.GetAll();
        SearchComboBox.Text = currentText;
    }
}