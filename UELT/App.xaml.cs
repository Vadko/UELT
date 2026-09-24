using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using UELT.Hikaro;

namespace UELT;

public partial class App : Application
{
    public App()
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }

    public static List<string> StartupFilePaths { get; private set; } = new();
    public static List<string> StartupFolderPaths { get; private set; } = new();
    public static HashSet<string> StartupDirectFilePaths { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, ex) =>
        {
            MessageBox.Show($"{ex.Exception.GetType().Name}: {ex.Exception.Message}\n\n{ex.Exception.StackTrace}", "Crash");
            ex.Handled = true;
            Shutdown();
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        LoadTheme();
        SettingsManager.Load();

        if (e.Args.Length > 0)
        {
            var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".locres", ".uasset", ".umap" };

            foreach (var arg in e.Args)
            {
                if (Directory.Exists(arg))
                {
                    var folderFiles = Directory.GetFiles(arg, "*.*", SearchOption.AllDirectories)
                        .Where(f => validExtensions.Contains(Path.GetExtension(f)))
                        .OrderBy(f => f);
                    StartupFilePaths.AddRange(folderFiles);
                    StartupFolderPaths.Add(arg);
                }
                else if (validExtensions.Contains(Path.GetExtension(arg)) && File.Exists(arg))
                {
                    StartupFilePaths.Add(arg);
                    StartupDirectFilePaths.Add(arg);
                }
            }

            StartupFilePaths = StartupFilePaths
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (StartupFilePaths.Count == 0)
                MessageBox.Show("Підтримуються лише locres, uasset та umap.", "Непідтримуваний формат", MessageBoxButton.OK);
        }

        ShutdownMode = ShutdownMode.OnLastWindowClose;
        new MainWindow().Show();
    }

    public static void ApplyTheme(bool isDark)
    {
        var resources = Current.Resources;
        var colors = isDark ? new
        {
            Window = "#14181E",                        
            Text = "#E2E8F0",                          
            Border = "#2D3748",                        
            DataGrid = "#1A202C",                      
            Selection = "#2B6CB0",                     
            SelectionFg = "#FFFFFF",                   
            NewRowBg = "#2F855A",                     
            ModifiedCellBg = "#5594C9FF",            
            TextBox = "#1A202C",                       
            Button = "#2D3748",                        
            ButtonHover = "#4A5568",                  
            MenuDisabledItemFg = "#FF888888",         
            ScrollBg = "#14181E",                      
            ScrollThumb = "#4A5568",                  
            ScrollThumbHover = "#718096",             
            ScrollThumbPressed = "#A0AEC0",
            ScrollLineHover = "#2D3748",
            ScrollBarLineButtonPressed = "#4A5568",    
            TreeItemSelectedBrush = "#3182CE",        
            TreeItemSelectedInactiveBrush = "#2B6CB040", 
            SearchResultSelection = "#1F3A5D",         
            SearchResultNotSelecting = "#2D3748",      
            UnsavedChangesForegroudBrush = "#FFA500",
            StatusApprovedIconFg = "#2196F3",
            GlossaryIconFg = "#B794F6",               
            TranslationMemoryIconFg = "#6FCF73",       
            HyperlinkFg = "#63B3ED",                 
            TagVariableFg = "#F6AD55",                
            TagVariableBg = "#33F6AD55",           
            SpellErrorIcon = "#FC8181",      
            SpellErrorText = "#FC8181",      
            CommonErrorIcon = "#F6AD55",  
            CommonErrorText = "#F6AD55",  
        } : new
        {
            Window = "#f6f8fa",
            Text = "#000000",
            Border = "#C2C3C9",
            DataGrid = "#FFFFFF",
            Selection = "#3399FF",
            SelectionFg = "#FFFFFF",
            NewRowBg = "#4C98FB98",
            ModifiedCellBg = "#8894C9FF",                
            TextBox = "#FFFFFF",
            Button = "#E9ECEF",                          
            ButtonHover = "#DEE2E6",                     
            MenuDisabledItemFg = "#FF888888",            
            ScrollBg = "#F0F0F0",
            ScrollThumb = "#CDCDCD",
            ScrollThumbHover = "#B4B4B4",
            ScrollThumbPressed = "#A0A0A0",
            ScrollLineHover = "#E6E6E6",
            ScrollBarLineButtonPressed = "#B4B4B4",     
            TreeItemSelectedBrush = "#33000000",         
            TreeItemSelectedInactiveBrush = "#22000000", 
            SearchResultSelection = "#dcecf6",           
            SearchResultNotSelecting = "#e5e5e5",        
            UnsavedChangesForegroudBrush = "#FFA500",
            StatusApprovedIconFg = "#2196F3",
            GlossaryIconFg = "#9C64D8",                  
            TranslationMemoryIconFg = "#008000",         
            HyperlinkFg = "#59A1D5",                     
            TagVariableFg = "#C05621",                   
            TagVariableBg = "#26C05621",                 
            SpellErrorIcon = "#E53E3E",        
            SpellErrorText = "#E53E3E",
            CommonErrorIcon = "#DD6B20",
            CommonErrorText = "#DD6B20",
        };

        resources["WindowBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Window));
        resources["TextForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Text));
        resources["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Border));
        resources["DataGridBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.DataGrid));
        resources["DataGridSelectionBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Selection));
        resources["DataGridSelectionForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.SelectionFg));
        resources["NewRowBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.NewRowBg));
        resources["ModifiedCellBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.ModifiedCellBg));
        resources["MenuBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Window));
        resources["MenuForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Text));
        resources["TextBoxBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.TextBox));
        resources["TextBoxForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Text));
        resources["ButtonBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Button));
        resources["ButtonForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Text));
        resources["ButtonHoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.ButtonHover));
        resources["ButtonPressedBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Button));
        resources["MenuDisabledItemForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.MenuDisabledItemFg));
        resources["ScrollBarBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.ScrollBg));
        resources["ScrollBarThumbBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.ScrollThumb));
        resources["ScrollBarThumbHoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.ScrollThumbHover));
        resources["ScrollBarThumbPressedBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.ScrollThumbPressed));
        resources["ScrollBarLineButtonHoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.ScrollLineHover));
        resources["ScrollBarLineButtonPressedBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.ScrollBarLineButtonPressed));
        resources["TreeItemSelectedBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.TreeItemSelectedBrush));
        resources["TreeItemSelectedInactiveBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.TreeItemSelectedInactiveBrush));
        resources["ListBoxSearchResultSelection"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.SearchResultSelection));
        resources["ListBoxSearchResultNotSelecting"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.SearchResultNotSelecting));
        resources["FileTreeUnsavedChangesForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.UnsavedChangesForegroudBrush));
        resources["StatusApprovedIconForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.StatusApprovedIconFg));
        resources["GlossaryIconForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.GlossaryIconFg));
        resources["TranslationMemoryIconForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.TranslationMemoryIconFg));
        resources["HyperlinkForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.HyperlinkFg));
        resources["TagVariableHighlightBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.TagVariableFg));
        resources["TagVariableHighlightBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.TagVariableBg));
        resources["SpellErrorIconBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.SpellErrorIcon));
        resources["SpellErrorTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.SpellErrorText));
        resources["CommonErrorIconBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.CommonErrorIcon));
        resources["CommonErrorTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.CommonErrorText));

        resources[SystemColors.WindowBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Window));
        resources[SystemColors.ControlBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.DataGrid));
        resources[SystemColors.ControlTextBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Text));
        resources[SystemColors.WindowTextBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Text));
        resources[SystemColors.HighlightBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Selection));
        resources[SystemColors.HighlightTextBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.SelectionFg));
        resources[SystemColors.InactiveSelectionHighlightBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.DataGrid));
        resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Text));

        SaveTheme(isDark);
    }

    private static void LoadTheme()
    {
        string settingsPath = GetSettingsPath();
        bool isDark = File.Exists(settingsPath) && File.ReadAllText(settingsPath).Trim() == "Dark";
        ApplyTheme(isDark);
    }

    private static void SaveTheme(bool isDark)
    {
        File.WriteAllText(GetSettingsPath(), isDark ? "Dark" : "Light");
    }

    private static string GetSettingsPath()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hikaro", "UELT");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "theme.txt");
    }

    public static bool IsDarkTheme()
    {
        var bg = Current.Resources["WindowBackgroundBrush"] as SolidColorBrush;
        return bg?.Color.R < 128;
    }

    public static void ForceExit()
    {
        try
        {
            if (Current.MainWindow != null)
            {
                Current.MainWindow.Close();
            }

            Task.Run(() =>
            {
                Thread.Sleep(1000);
                try
                {
                    Environment.Exit(0);
                }
                catch { }
            });
        }
        catch
        {
            Environment.Exit(0);
        }
    }
}