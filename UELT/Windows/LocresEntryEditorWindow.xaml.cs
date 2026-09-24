using System.Windows;
using UELT.Core;
using UELT.Core.locres;
using UELT.Hikaro;

namespace UELT;

public partial class LocresEntryEditorWindow : Window
{
    public string NameSpace { get; set; }
    public string Key { get; set; }
    public string Value { get; set; }
    public LocresFile Asset { get; set; }
    public bool IsEditMode { get; set; }

    public HashTable HashTable
    {
        get
        {
            var hash = new HashTable()
            {
                NameHash = uint.TryParse(txtNameSpaceHash.Text, out uint nsHash) ? nsHash : 0,
                KeyHash = uint.TryParse(txtKeyHash.Text, out uint kHash) ? kHash : 0,
                ValueHash = uint.TryParse(txtValueHash.Text, out uint vHash) ? vHash : 0,
            };

            if (txtExternID.IsEnabled && uint.TryParse(txtExternID.Text, out uint externID))
                hash.ExternID = externID;

            return hash;
        }
    }

    public LocresEntryEditorWindow(LocresFile asset)
    {
        InitializeComponent();
        Asset = asset;
        IsEditMode = false;
        Title = "Додавання нового рядка";
        btnApply.Content = "Додати";

        bool isV4 = Asset?.Version == LocresVersion.Optimized_CityHash64_ExternID_UTF16;
        txtExternID.IsEnabled = isV4;
        btnGenExternID.IsEnabled = isV4;
    }

    public LocresEntryEditorWindow(LocresDataGridItem item, LocresFile asset)
    {
        InitializeComponent();
        Asset = asset;
        IsEditMode = true;
        Title = "Редагування рядка";

        var parts = item.ID.Split(new[] { "::" }, StringSplitOptions.None);
        if (parts.Length == 2)
        {
            NameSpace = parts[0];
            Key = parts[1];
        }
        else
        {
            NameSpace = "";
            Key = parts[0];
        }

        Value = item.Translation;

        txtNameSpace.Text = NameSpace;
        txtKey.Text = Key;
        txtValue.Text = item.Translation;

        if (item.HashTable != null)
        {
            txtNameSpaceHash.Text = item.HashTable.NameHash.ToString();
            txtKeyHash.Text = item.HashTable.KeyHash.ToString();
            txtValueHash.Text = item.HashTable.ValueHash.ToString();

            if (item.HashTable.ExternID != 0)
                txtExternID.Text = item.HashTable.ExternID.ToString();
        }

        bool isV4 = Asset?.Version == LocresVersion.Optimized_CityHash64_ExternID_UTF16;
        txtExternID.IsEnabled = isV4;
        btnGenExternID.IsEnabled = isV4;
    }

    private void BtnGenNameSpace_Click(object sender, RoutedEventArgs e)
    {
        if (Asset != null)
            txtNameSpaceHash.Text = Asset.CalcHash(txtNameSpace.Text).ToString();
    }

    private void BtnGenKey_Click(object sender, RoutedEventArgs e)
    {
        if (Asset != null)
            txtKeyHash.Text = Asset.CalcHash(txtKey.Text).ToString();
    }

    private void BtnGenValue_Click(object sender, RoutedEventArgs e)
    {
        if (IsEditMode)
        {
            var result = MessageBox.Show(this, "Ви дійсно хочете згенерувати новий хеш тексту?\nЦе може порушити відображення тексту рядка в грі, якщо генеруєте на основі перекладеного тексту.", "Підтвердження", MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes)
                return;
        }

        string decoded = AssetHelper.ReplaceBreaklines(txtValue.Text, Back: true);
        txtValueHash.Text = decoded.StrCrc32().ToString();
    }

    private void BtnGenExternID_Click(object sender, RoutedEventArgs e)
    {
        if (Asset?.Version == LocresVersion.Optimized_CityHash64_ExternID_UTF16)
        {
            var allExternIDs = Asset.SelectMany(ns => ns).Select(st => st.ExternID);
            uint newExternID = (allExternIDs.Any() ? allExternIDs.Max() : 0) + 1;
            txtExternID.Text = newExternID.ToString();
        }
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtKey.Text))
        {
            MessageBox.Show(this, "ID не може бути порожнім!", "Помилка", MessageBoxButton.OK);
            return;
        }

        if ((!string.IsNullOrWhiteSpace(txtNameSpaceHash.Text) && !uint.TryParse(txtNameSpaceHash.Text, out _)) ||
            (!string.IsNullOrWhiteSpace(txtKeyHash.Text) && !uint.TryParse(txtKeyHash.Text, out _)) ||
            (!string.IsNullOrWhiteSpace(txtValueHash.Text) && !uint.TryParse(txtValueHash.Text, out _)))
        {
            MessageBox.Show(this, "Хеші повинні бути цілими числами!", "Помилка", MessageBoxButton.OK);
            return;
        }

        NameSpace = txtNameSpace.Text;
        Key = txtKey.Text;
        Value = AssetHelper.ReplaceBreaklines(txtValue.Text);

        DialogResult = true;
        Close();
    }
}