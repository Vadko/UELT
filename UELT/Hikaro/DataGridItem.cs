using System.ComponentModel;
using System.Windows.Media;

namespace UELT.Hikaro;

public class DataGridItem : INotifyPropertyChanged
{
    private string _id = "";
    private string _text = "";
    private string _translation = "";
    private int _index;
    public int DisplayIndex => _index + 1;
    private bool _isModified = false;
    private bool _isNew = false;
    private RowStatus _status = RowStatus.None;
    private bool _hasGlossaryMatch = false;
    private bool _hasTranslationMemoryMatch = false;

    public List<string> OriginalStringData { get; set; }

    public string ID
    {
        get => _id;
        set { _id = value; OnPropertyChanged(nameof(ID)); }
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(DisplayText));
            OnPropertyChanged(nameof(OriginalWordCount));
        }
    }

    public int OriginalWordCount => CountWords(_text);
    public int TranslationWordCount => CountWords(_translation);

    private static int CountWords(string text)
        => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static RowStatus DeriveStatusOnTranslationChange(RowStatus current, bool isTranslated) => current switch
    {
        RowStatus.None => isTranslated ? RowStatus.NeedsReview : RowStatus.None,
        RowStatus.NeedsReview => isTranslated ? RowStatus.NeedsReview : RowStatus.None,
        RowStatus.Approved => isTranslated ? RowStatus.Approved : RowStatus.None,
        _ => current
    };

    public string Translation
    {
        get => _translation;
        set
        {
            if (_translation != value)
            {
                _translation = value;
                IsModified = _translation != _text;
                Status = DeriveStatusOnTranslationChange(_status, _translation != _text);

                OnPropertyChanged(nameof(Translation));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(TranslationWordCount));
            }
        }
    }

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(nameof(Index)); OnPropertyChanged(nameof(DisplayIndex)); }
    }

    public bool IsModified
    {
        get => _isModified;
        set { _isModified = value; OnPropertyChanged(nameof(IsModified)); }
    }

    public bool IsNew
    {
        get => _isNew;
        set
        {
            if (_isNew != value)
            {
                _isNew = value;
                OnPropertyChanged(nameof(IsNew));
            }
        }
    }

    public bool HasGlossaryMatch
    {
        get => _hasGlossaryMatch;
        set
        {
            if (_hasGlossaryMatch != value)
            {
                _hasGlossaryMatch = value;
                OnPropertyChanged(nameof(HasGlossaryMatch));
            }
        }
    }

    public bool HasTranslationMemoryMatch
    {
        get => _hasTranslationMemoryMatch;
        set
        {
            if (_hasTranslationMemoryMatch != value)
            {
                _hasTranslationMemoryMatch = value;
                OnPropertyChanged(nameof(HasTranslationMemoryMatch));
            }
        }
    }

    public RowStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusSymbol));
            }
        }
    }

    public Geometry StatusSymbol => _status switch
    {
        RowStatus.NeedsReview => (Geometry)App.Current.Resources["NeedsReviewIcon"],
        RowStatus.Approved => (Geometry)App.Current.Resources["ApprovedIcon"],
        _ => null
    };

    public string DisplayText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_translation) && _translation != _text)
                return _translation;
            return _text ?? "";
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class LocresDataGridItem : DataGridItem
{
    public Core.locres.HashTable HashTable { get; set; }

    public Core.locres.StringTable StringTableEntry { get; set; }
}