using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UELT.Hikaro;

public class FileTreeManager
{
    private readonly TreeView _treeView;
    public ObservableCollection<FileTreeNode> RootNodes { get; } = new();

    private readonly Dictionary<string, FileTreeNode> _fileNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileTreeNode> _folderNodes = new(StringComparer.OrdinalIgnoreCase);

    private string _pendingActiveFilePath;
    private FileTreeNode _currentActiveNode;

    public event Action<string> FileNodeClicked;
    public event Action<string> CloseFolder;
    public event Action<string> CloseFolderKeepOnly;
    public event Action<string> CloseFile;
    public event Action CloseAll;

    public FileTreeManager(TreeView treeView)
    {
        _treeView = treeView;
        _treeView.ItemsSource = RootNodes;
        _treeView.SelectedItemChanged += TreeView_SelectedItemChanged;
        _treeView.IsVisibleChanged += TreeView_IsVisibleChanged;
        _treeView.PreviewMouseRightButtonDown += TreeView_PreviewMouseRightButtonDown;
    }

    public void AddFile(string filePath, bool deferSort = false)
    {
        if (_fileNodes.ContainsKey(filePath)) return;

        string dirPath = Path.GetDirectoryName(filePath) ?? "";
        string fileName = Path.GetFileName(filePath);

        var folderNode = GetOrCreateFolderNode(dirPath);
        var fileNode = new FileTreeNode
        {
            Kind = FileTreeNode.NodeKind.File,
            FilePath = filePath,
            FileName = fileName,
            Parent = folderNode
        };

        _fileNodes[filePath] = fileNode;
        folderNode.Children.Add(fileNode);

        if (!deferSort)
            SortChildren(folderNode);
        else
            _foldersPendingSort.Add(folderNode);
    }

    private readonly HashSet<FileTreeNode> _foldersPendingSort = new();

    public void FinalizeBatchAdd()
    {
        foreach (var folder in _foldersPendingSort)
            SortChildren(folder);
        _foldersPendingSort.Clear();
    }

    public void RemoveFile(string filePath)
    {
        if (!_fileNodes.Remove(filePath, out var fileNode)) return;

        var parent = fileNode.Parent;
        parent?.Children.Remove(fileNode);

        if (parent != null && parent.Children.Count == 0)
        {
            _folderNodes.Remove(parent.FolderPath);
            RootNodes.Remove(parent);
        }
    }

    public void UpdateUnsavedFlag(string filePath, bool hasUnsavedChanges)
    {
        if (_fileNodes.TryGetValue(filePath, out var node))
            node.HasUnsavedChanges = hasUnsavedChanges;
    }

    public void SetActiveFile(string filePath, bool selectInTree = true)
    {
        _pendingActiveFilePath = filePath;

        if (string.IsNullOrEmpty(filePath) || !_fileNodes.TryGetValue(filePath, out var active))
        {
            if (_currentActiveNode != null)
            {
                _currentActiveNode.IsActive = false;
                _currentActiveNode = null;
            }
            return;
        }

        if (active == _currentActiveNode)
            return;

        if (_currentActiveNode != null)
            _currentActiveNode.IsActive = false;

        active.IsActive = true;
        _currentActiveNode = active;
        ExpandParents(active);

        if (selectInTree && _treeView.IsVisible)
            SelectNode(active);
    }

    private FileTreeNode _pendingSelectNode;

    private void SelectNode(FileTreeNode node)
    {
        _pendingSelectNode = node;

        _treeView.Dispatcher.BeginInvoke(() =>
        {
            if (_pendingSelectNode != node) return;

            var container = FindContainer(_treeView, node);
            if (container == null) return;
            if (!container.IsSelected)
                container.IsSelected = true;
            container.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static TreeViewItem FindContainer(ItemsControl parent, FileTreeNode target)
    {
        foreach (var item in parent.Items)
        {
            var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (container == null) continue;

            if (container.DataContext == target)
                return container;

            if (container.Items.Count > 0)
            {
                var found = FindContainer(container, target);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static void ExpandParents(FileTreeNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            parent.IsExpanded = true;
            parent = parent.Parent;
        }
    }

    private FileTreeNode GetOrCreateFolderNode(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            throw new ArgumentException("File must have a parent directory.");

        if (_folderNodes.TryGetValue(folderPath, out var existing))
            return existing;

        string folderName = Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(folderName))
            folderName = folderPath;

        var folderNode = new FileTreeNode
        {
            Kind = FileTreeNode.NodeKind.Folder,
            FolderPath = folderPath,
            FolderName = folderName,
            IsExpanded = true
        };

        _folderNodes[folderPath] = folderNode;
        RootNodes.Insert(FindFolderInsertIndex(folderName), folderNode);
        return folderNode;
    }

    private int FindFolderInsertIndex(string newFolderName)
    {
        for (int i = 0; i < RootNodes.Count; i++)
        {
            if (RootNodes[i].FolderPath == "_loose_")
                return i;

            if (string.Compare(RootNodes[i].FolderName, newFolderName, StringComparison.OrdinalIgnoreCase) > 0)
                return i;
        }
        return RootNodes.Count;
    }

    private static void SortChildren(FileTreeNode node)
    {
        var children = node.Children;
        var sorted = children.OrderBy(c => c.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        bool alreadySorted = true;

        for (int i = 0; i < sorted.Count; i++)
        {
            if (!ReferenceEquals(children[i], sorted[i])) { alreadySorted = false; break; }
        }
        if (alreadySorted) return;

        for (int i = 0; i < sorted.Count; i++)
            children[i] = sorted[i];
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileTreeNode { Kind: FileTreeNode.NodeKind.File } node)
            FileNodeClicked?.Invoke(node.FilePath);
    }

    private void TreeView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_treeView.IsVisible && _pendingActiveFilePath != null)
            SetActiveFile(_pendingActiveFilePath, selectInTree: true);
    }

    private void TreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = GetTreeViewItemFromPoint(_treeView, e.GetPosition(_treeView));
        if (item?.DataContext is not FileTreeNode node) return;

        e.Handled = true;

        ContextMenu menu;

        if (node.Kind == FileTreeNode.NodeKind.Folder)
        {
            menu = BuildFolderContextMenu(node);
        }
        else
        {
            menu = BuildFileContextMenu(node);
        }

        menu.IsOpen = true;
    }

    private ContextMenu BuildFolderContextMenu(FileTreeNode folder)
    {
        var itemStyle = Application.Current.TryFindResource("ContextMenuItemStyle") as Style;

        ContextMenu menu = new();

        MenuItem MakeItem(string header, Action action)
        {
            var item = new MenuItem { Header = header, Style = itemStyle };
            item.Click += (_, _) => action();
            return item;
        }

        menu.Items.Add(MakeItem("Закрити теку", () => CloseFolder?.Invoke(folder.FolderPath)));
        menu.Items.Add(MakeItem("Закрити всі теки, окрім цієї", () => CloseFolderKeepOnly?.Invoke(folder.FolderPath)));
        menu.Items.Add(MakeItem("Закрити всі теки", () => CloseAll?.Invoke()));

        return menu;
    }

    private ContextMenu BuildFileContextMenu(FileTreeNode file)
    {
        var style = Application.Current.TryFindResource("ContextMenuItemStyle") as Style;
        var menu = new ContextMenu();

        var closeFile = new MenuItem { Header = "Закрити файл", Style = style };
        closeFile.Click += (_, _) => CloseFile?.Invoke(file.FilePath);

        menu.Items.Add(closeFile);
        return menu;
    }

    private static TreeViewItem GetTreeViewItemFromPoint(TreeView treeView, Point point)
    {
        var hit = treeView.InputHitTest(point) as DependencyObject;
        while (hit != null)
        {
            if (hit is TreeViewItem tvi) return tvi;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }
}