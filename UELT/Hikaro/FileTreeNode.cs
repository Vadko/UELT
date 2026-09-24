using System.Collections.ObjectModel;
using System.ComponentModel;

namespace UELT.Hikaro;

public class FileTreeNode : INotifyPropertyChanged
{
    public enum NodeKind { Folder, File }
    public NodeKind Kind { get; set; }
    public string FolderPath { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";

    private bool _isExpanded = true;
    private bool _isActive;
    private bool _hasUnsavedChanges;

    public string DisplayName
    {
        get
        {
            if (Kind == NodeKind.File)
                return HasUnsavedChanges ? $"*{FileName}" : FileName;
            return FolderName;
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
    }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(DisplayName)); }
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set { _hasUnsavedChanges = value; OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(DisplayName)); }
    }

    public ObservableCollection<FileTreeNode> Children { get; } = new();

    public FileTreeNode Parent { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}