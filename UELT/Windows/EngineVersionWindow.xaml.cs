using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using UAssetAPI.UnrealTypes;
using UELT.Hikaro;

namespace UELT;

public partial class EngineVersionWindow : Window
{
    public EngineVersion SelectedVersion { get; private set; }
    public bool UseAutoDetect { get; private set; }
    public bool UseMappings { get; private set; }
    public string MappingsPath { get; private set; }
    public MappingType SelectedMappingType { get; private set; }

    private readonly RecentItemsManager<string> _recentMappingsManager = RecentManagers.ForMappings();

    public EngineVersionWindow()
    {
        InitializeComponent();
        UseMappings = false;
        MappingsPath = "";
        SelectedMappingType = MappingType.None;
        LoadRecentMappings();
    }

    private void LoadRecentMappings()
    {
        RecentMappingsComboBox.Items.Clear();

        foreach (string path in _recentMappingsManager.Items)
        {
            string displayName;

            if (path.EndsWith(".jmap.gz", StringComparison.OrdinalIgnoreCase))
            {
                string baseName = Path.GetFileName(path).Replace(".jmap.gz", "", StringComparison.OrdinalIgnoreCase);
                displayName = baseName + " [jmap.gz]";
            }
            else if (path.EndsWith(".jmap", StringComparison.OrdinalIgnoreCase))
                displayName = Path.GetFileNameWithoutExtension(path) + " [jmap]";
            else if (path.EndsWith(".usmap", StringComparison.OrdinalIgnoreCase))
                displayName = Path.GetFileNameWithoutExtension(path) + " [usmap]";
            else
                displayName = Path.GetFileName(path);

            RecentMappingsComboBox.Items.Add(new ComboBoxItem
            {
                Content = displayName,
                Tag = path,
                ToolTip = path
            });
        }

        if (RecentMappingsComboBox.Items.Count > 0)
            RecentMappingsComboBox.SelectedIndex = 0;
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
        bool isValid = files?.Length == 1 &&
                       (files[0].EndsWith(".usmap", StringComparison.OrdinalIgnoreCase) ||
                        files[0].EndsWith(".jmap", StringComparison.OrdinalIgnoreCase) ||
                        files[0].EndsWith(".jmap.gz", StringComparison.OrdinalIgnoreCase));

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

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var mappingFile = files.FirstOrDefault(f => f.EndsWith(".usmap", StringComparison.OrdinalIgnoreCase) ||
                                                    f.EndsWith(".jmap", StringComparison.OrdinalIgnoreCase) ||
                                                    f.EndsWith(".jmap.gz", StringComparison.OrdinalIgnoreCase));
        if (mappingFile != null)
        {
            MappingsPathTextBox.Text = mappingFile;
            MappingsPath = mappingFile;
            UseMappingsCheckBox.IsChecked = true;
        }
        e.Handled = true;
    }

    private void UseMappingsCheckBox_Checked(object sender, RoutedEventArgs e)
    {
    }

    private void UseMappingsCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        MappingsPathTextBox.Text = "";
        MappingsPath = "";
    }

    private void BrowseMappingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog ofd = new()
        {
            Filter = "Mapping файли|*.usmap;*.jmap;*.jmap.gz|USMAP файли|*.usmap|JMAP файли|*.jmap;*.jmap.gz",
            Title = "Виберіть usmap/jmap/jmap.gz файл"
        };

        if (ofd.ShowDialog() == true)
        {
            MappingsPathTextBox.Text = ofd.FileName;
            MappingsPath = ofd.FileName;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (VersionComboBox.SelectedItem is ComboBoxItem item)
        {
            string tag = item.Tag.ToString();

            if (tag == "auto")
            {
                UseAutoDetect = true;
            }
            else
            {
                UseAutoDetect = false;
                SelectedVersion = tag switch
                {
                    "0" => EngineVersion.VER_UE4_0,
                    "1" => EngineVersion.VER_UE4_1,
                    "2" => EngineVersion.VER_UE4_2,
                    "3" => EngineVersion.VER_UE4_3,
                    "4" => EngineVersion.VER_UE4_4,
                    "5" => EngineVersion.VER_UE4_5,
                    "6" => EngineVersion.VER_UE4_6,
                    "7" => EngineVersion.VER_UE4_7,
                    "8" => EngineVersion.VER_UE4_8,
                    "9" => EngineVersion.VER_UE4_9,
                    "10" => EngineVersion.VER_UE4_10,
                    "11" => EngineVersion.VER_UE4_11,
                    "12" => EngineVersion.VER_UE4_12,
                    "13" => EngineVersion.VER_UE4_13,
                    "14" => EngineVersion.VER_UE4_14,
                    "15" => EngineVersion.VER_UE4_15,
                    "16" => EngineVersion.VER_UE4_16,
                    "17" => EngineVersion.VER_UE4_17,
                    "18" => EngineVersion.VER_UE4_18,
                    "19" => EngineVersion.VER_UE4_19,
                    "20" => EngineVersion.VER_UE4_20,
                    "21" => EngineVersion.VER_UE4_21,
                    "22" => EngineVersion.VER_UE4_22,
                    "23" => EngineVersion.VER_UE4_23,
                    "24" => EngineVersion.VER_UE4_24,
                    "25" => EngineVersion.VER_UE4_25,
                    "26" => EngineVersion.VER_UE4_26,
                    "27" => EngineVersion.VER_UE4_27,
                    "500EA" => EngineVersion.VER_UE5_0EA,
                    "500" => EngineVersion.VER_UE5_0,
                    "501" => EngineVersion.VER_UE5_1,
                    "502" => EngineVersion.VER_UE5_2,
                    "503" => EngineVersion.VER_UE5_3,
                    "504" => EngineVersion.VER_UE5_4,
                    "505" => EngineVersion.VER_UE5_5,
                    "506" => EngineVersion.VER_UE5_6,
                    "507" => EngineVersion.VER_UE5_7,
                    "508" => EngineVersion.VER_UE5_8,
                    _ => EngineVersion.VER_UE4_27
                };
            }
        }

        UseMappings = UseMappingsCheckBox.IsChecked == true;

        if (UseMappings)
        {
            string browsePath = MappingsPathTextBox.Text.Trim();
            string comboPath = (RecentMappingsComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

            string selectedPath;
            if (!string.IsNullOrEmpty(browsePath))
            {
                selectedPath = browsePath;
            }
            else if (!string.IsNullOrEmpty(comboPath))
            {
                selectedPath = comboPath;
            }
            else
            {
                MessageBox.Show(this, "Виберіть usmap/jmap файл.", "Попередження", MessageBoxButton.OK);
                return;
            }

            MappingsPath = selectedPath;

            if (MappingsPath.EndsWith(".jmap.gz", StringComparison.OrdinalIgnoreCase))
                SelectedMappingType = MappingType.JmapGz;
            else if (MappingsPath.EndsWith(".jmap", StringComparison.OrdinalIgnoreCase))
                SelectedMappingType = MappingType.Jmap;
            else if (MappingsPath.EndsWith(".usmap", StringComparison.OrdinalIgnoreCase))
                SelectedMappingType = MappingType.Usmap;
            else
                SelectedMappingType = MappingType.Unknown;

            _recentMappingsManager.Add(MappingsPath);
        }
        else
        {
            SelectedMappingType = MappingType.None;
        }

        DialogResult = true;
        Close();
    }
}