using System.Collections.ObjectModel;
using System.ComponentModel;

namespace UELT.Hikaro;

public class SearchResultItem
{
    public int Number { get; set; }
    public int DisplayNumber => Number + 1;
    public string Term { get; set; } = "";
    public string Preview { get; set; } = "";
    public int TermIndex { get; set; }
    public string SearchQuery { get; set; } = "";
    public StringComparison Comparison { get; set; }
    public string ColumnType { get; set; } = "";
    public bool ShowTerm { get; set; } = true;
    public bool IsRegex { get; set; } = false;
}

public class SearchManager : INotifyPropertyChanged
{
    private string _searchQuery = "";
    private SearchMode _searchMode = SearchMode.Text;
    private bool _isSearchPanelVisible = false;
    private bool _isIdMode = false;
    private StringComparison _searchComparison = StringComparison.OrdinalIgnoreCase;
    public bool WasWholeWord { get; set; }
    public bool WasExactMatch { get; set; }

    public ObservableCollection<SearchResultItem> SearchResults { get; set; } = new();

    public int[] OriginalMatchIndices { get; set; } = [];
    public int[] MatchIndices { get; set; } = [];
    public int CurrentMatch { get; set; } = -1;
    public SearchFilter CurrentSearchFilter { get; set; } = SearchFilter.All;
    public Dictionary<int, string> OriginalMatchColumnTypes { get; set; } = new();

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            _searchQuery = value;
            OnPropertyChanged(nameof(SearchQuery));
        }
    }

    public SearchMode SearchMode
    {
        get => _searchMode;
        set
        {
            _searchMode = value;
            OnPropertyChanged(nameof(SearchMode));
        }
    }

    public bool IsSearchPanelVisible
    {
        get => _isSearchPanelVisible;
        set
        {
            _isSearchPanelVisible = value;
            OnPropertyChanged(nameof(IsSearchPanelVisible));
        }
    }

    public bool IsIdMode
    {
        get => _isIdMode;
        set
        {
            _isIdMode = value;
            SearchMode = value ? SearchMode.ID : SearchMode.Text;
            OnPropertyChanged(nameof(IsIdMode));
        }
    }

    public StringComparison SearchComparison
    {
        get => _searchComparison;
        set
        {
            _searchComparison = value;
            OnPropertyChanged(nameof(SearchComparison));
        }
    }

    public int SearchResultsCount => SearchResults.Count;

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void ClearSearch()
    {
        SearchQuery = "";
        SearchResults.Clear();
        OriginalMatchIndices = Array.Empty<int>();
        MatchIndices = Array.Empty<int>();
        CurrentMatch = -1;
        OriginalMatchColumnTypes.Clear();
    }
}

public enum SearchMode
{
    Text,
    ID,
    Regex,
    SpellCheck
}

public enum SearchFilter
{
    All,
    Both,
    Original,
    Translation
}