using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UELT.Hikaro;

namespace UELT;

public partial class ReplaceWindow : Window
{
    private RecentItemsManager<string> _recentSearchesManager;

    public string FindText => TxtFind.Text;
    public string ReplaceText => TxtReplace.Text;
    public bool WholeWord => ChkWholeWord.IsChecked == true;
    public bool CaseSensitive => ChkCaseSensitive.IsChecked == true;
    public bool ExactMatch => ChkExactMatch.IsChecked == true;
    public bool AllTabs => ChkAllTabs.IsChecked == true && ChkAllTabs.Visibility == Visibility.Visible;

    public event EventHandler ReplaceRequested;

    public ReplaceWindow()
    {
        InitializeComponent();
    }

    public void Initialize(RecentItemsManager<string> recentSearchesManager, string lastFindText = null, string lastReplaceText = null)
    {
        _recentSearchesManager = recentSearchesManager;
        RefreshDropdown();

        if (!string.IsNullOrEmpty(lastFindText) && string.IsNullOrEmpty(TxtFind.Text))
            TxtFind.Text = lastFindText;

        if (!string.IsNullOrEmpty(lastReplaceText) && string.IsNullOrEmpty(TxtReplace.Text))
            TxtReplace.Text = lastReplaceText;

        FocusFindTextBox();
    }

    private void RefreshDropdown()
    {
        if (_recentSearchesManager == null) return;
        string saved = TxtFind.Text;
        TxtFind.ItemsSource = null;
        TxtFind.ItemsSource = _recentSearchesManager.GetAll();
        TxtFind.Text = saved;
    }

    private void FocusFindTextBox()
    {
        TxtFind.Focus();
        if (TxtFind.Template.FindName("PART_EditableTextBox", TxtFind) is TextBox editBox)
        {
            editBox.CaretIndex = editBox.Text.Length;
            editBox.Focus();
        }
    }

    public void SetMultiTabMode(bool hasMultipleTabs)
    {
        ChkAllTabs.Visibility = hasMultipleTabs ? Visibility.Visible : Visibility.Collapsed;
        if (!hasMultipleTabs) ChkAllTabs.IsChecked = false;
    }

    private void ChkMutualExclusive_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkWholeWord.IsChecked == true)
            ChkExactMatch.IsEnabled = false;
        else if (ChkExactMatch.IsChecked == true)
            ChkWholeWord.IsEnabled = false;
        else
        {
            ChkWholeWord.IsEnabled = true;
            ChkExactMatch.IsEnabled = true;
        }
    }

    private void TxtClear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is TextBox tb)
        {
            tb.Clear();
            tb.Focus();
        }
    }

    private void BtnSwap_Click(object sender, RoutedEventArgs e)
    {
        (TxtFind.Text, TxtReplace.Text) = (TxtReplace.Text, TxtFind.Text);
    }

    private void BtnReplace_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FindText))
        {
            ShowStatus("Введіть текст для пошуку");
            FocusFindTextBox();
            return;
        }

        ReplaceRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (TxtFind.IsDropDownOpen)
            {
                TxtFind.IsDropDownOpen = false;
                e.Handled = true;
            }
            else
            {
                Close();
            }
        }
    }

    public void ShowStatus(string message)
    {
        LblStatus.Text = message;
    }
}