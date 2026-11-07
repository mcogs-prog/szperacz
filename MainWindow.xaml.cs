using System.Net;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OGŚ;

public partial class MainWindow : Window
{
    public sealed class TabItemModel
    {
        public string Title { get; set; } = "";
        public string Path { get; set; } = "";
    }

    public sealed class BookmarkItem
    {
        public string Title { get; set; } = "";
        public string Path { get; set; } = "";
    }
    public sealed class FtpBookmark
    {
        public string Name { get; set; } = "Serwer FTP";
        public string Url { get; set; } = "ftp://";
        public string User { get; set; } = "";
        public string Pass { get; set; } = "";
    }

    public sealed class FtpSession
    {
        public string BaseUrl { get; set; } = "";
        public string CurrentUrl { get; set; } = "";
        public string User { get; set; } = "";
        public string Pass { get; set; } = "";
    }


    public sealed class AppConfig
    {
        public List<TabItemModel> LeftTabs { get; set; } = new();
        public List<TabItemModel> RightTabs { get; set; } = new();
        public int LeftTabIndex { get; set; }
        public int RightTabIndex { get; set; }
        public bool IsLeftActive { get; set; } = true;
        public List<BookmarkItem> Bookmarks { get; set; } = new();
        public List<FtpBookmark> FtpBookmarks { get; set; } = new();
        public double WindowWidth { get; set; } = 1420;
        public double WindowHeight { get; set; } = 900;
        public bool WindowMaximized { get; set; }
        public bool ShowFolderTree { get; set; }
    }

    private readonly List<TabItemModel> _leftTabs = new();
    private readonly List<TabItemModel> _rightTabs = new();
    private int _leftTabIndex = 0;
    private int _rightTabIndex = 0;

    private string _leftPathValue = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _rightPathValue = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private readonly List<FileEntry> _leftRawEntries = new();
    private readonly List<FileEntry> _rightRawEntries = new();

    private bool _isLeftActive = true;
    private bool _isBusy = false;
    private bool _showTree = false;

    private static readonly Dictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<BookmarkItem> _bookmarks = new();
    private readonly List<FtpBookmark> _ftpBookmarks = new();
    private FtpSession? _leftFtpSession = null;
    private FtpSession? _rightFtpSession = null;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LoadConfiguration();

            RefreshDrives();
            RenderTabs(true);
            RenderTabs(false);

            LoadPanel(true);
            LoadPanel(false);

            if (_showTree)
            {
                PopulateTree(true);
                PopulateTree(false);
            }
            ApplyTreeVisibility();

            UpdateCliPrefix();
            if (_isLeftActive) LeftGrid.Focus(); else RightGrid.Focus();
        };
    }

    public sealed class FileEntry
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string Kind { get; init; } = "";
        public string SizeText { get; init; } = "";
        public long SizeBytes { get; init; }
        public string ModifiedText { get; init; } = "";
        public DateTime ModifiedDate { get; init; }
        public bool IsDirectory { get; init; }
        public ImageSource? IconImage { get; set; }
    }

    private void SetStatus(string msg) => Dispatcher.Invoke(() => StatusText.Text = msg);

    private void SetProgress(bool visible, int percent = 0)
    {
        Dispatcher.Invoke(() =>
        {
            OperationProgress.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            OperationProgress.Value = Math.Clamp(percent, 0, 100);
        });
    }

    private string ActivePath => _isLeftActive ? _leftPathValue : _rightPathValue;

    private void UpdateCliPrefix()
    {
        Dispatcher.Invoke(() =>
        {
            string p = ActivePath;
            if (p.Length > 35) p = p.Substring(0, 12) + "..." + p.Substring(p.Length - 20);
            CliPathPrefix.Text = $"{p}>";
        });
    }

    #region Konfiguracja i Zapis Sesji (Persistence)
    private static string GetConfigPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "OGS", "Szperacz");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "config.json");
    }

    private void LoadConfiguration()
    {
        try
        {
            string path = GetConfigPath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    if (config.LeftTabs.Count > 0)
                    {
                        _leftTabs.Clear();
                        _leftTabs.AddRange(config.LeftTabs.Where(t => Directory.Exists(t.Path)));
                    }
                    if (config.RightTabs.Count > 0)
                    {
                        _rightTabs.Clear();
                        _rightTabs.AddRange(config.RightTabs.Where(t => Directory.Exists(t.Path)));
                    }

                    _leftTabIndex = Math.Clamp(config.LeftTabIndex, 0, Math.Max(0, _leftTabs.Count - 1));
                    _rightTabIndex = Math.Clamp(config.RightTabIndex, 0, Math.Max(0, _rightTabs.Count - 1));
                    _isLeftActive = config.IsLeftActive;
                    _showTree = config.ShowFolderTree;

                    if (config.Bookmarks.Count > 0)
                    {
                        _bookmarks.Clear();
                        _bookmarks.AddRange(config.Bookmarks);
                    }
                    if (config.FtpBookmarks != null && config.FtpBookmarks.Count > 0)
                    {
                        _ftpBookmarks.Clear();
                        _ftpBookmarks.AddRange(config.FtpBookmarks);
                    }

                    if (config.WindowWidth >= 900) Width = config.WindowWidth;
                    if (config.WindowHeight >= 600) Height = config.WindowHeight;
                    if (config.WindowMaximized) WindowState = WindowState.Maximized;
                }
            }
        }
        catch { /* Użyj wartości domyślnych w razie błędu odczytu */ }

        // Fallback gdy brak zakładek
        if (_leftTabs.Count == 0)
            _leftTabs.Add(new TabItemModel { Title = "Profil", Path = _leftPathValue });
        if (_rightTabs.Count == 0)
            _rightTabs.Add(new TabItemModel { Title = "Dokumenty", Path = _rightPathValue });

        if (_ftpBookmarks.Count == 0)
        {
            _ftpBookmarks.Add(new FtpBookmark { Name = "Rebex Public Test", Url = "ftp://test.rebex.net/", User = "demo", Pass = "password" });
        }

        if (_bookmarks.Count == 0)
        {
            _bookmarks.Add(new BookmarkItem { Title = "Pulpit", Path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) });
            _bookmarks.Add(new BookmarkItem { Title = "Pobrane", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") });
            _bookmarks.Add(new BookmarkItem { Title = "Dokumenty", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) });
            _bookmarks.Add(new BookmarkItem { Title = "Dysk C:\\", Path = "C:\\" });
        }

        _leftPathValue = _leftTabs[_leftTabIndex].Path;
        _rightPathValue = _rightTabs[_rightTabIndex].Path;
    }

    private void SaveConfiguration()
    {
        try
        {
            var config = new AppConfig
            {
                LeftTabs = _leftTabs,
                RightTabs = _rightTabs,
                LeftTabIndex = _leftTabIndex,
                RightTabIndex = _rightTabIndex,
                IsLeftActive = _isLeftActive,
                Bookmarks = _bookmarks,
                FtpBookmarks = _ftpBookmarks,
                WindowWidth = ActualWidth,
                WindowHeight = ActualHeight,
                WindowMaximized = (WindowState == WindowState.Maximized),
                ShowFolderTree = _showTree
            };

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetConfigPath(), json);
        }
        catch { /* Ciche ignorowanie ewentualnych błędów zapisu przy zamykaniu */ }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveConfiguration();
    }
    #endregion

    #region Drzewko katalogów (Folder Tree - Alt+T)
    private void ToggleTree_Click(object sender, RoutedEventArgs e)
    {
        _showTree = !_showTree;
        ApplyTreeVisibility();
        SetStatus(_showTree ? "Włączono drzewko katalogów." : "Wyłączono drzewko katalogów.");
    }

    private void ApplyTreeVisibility()
    {
        double colWidth = _showTree ? 220 : 0;
        LeftTreeCol.Width = new GridLength(colWidth);
        RightTreeCol.Width = new GridLength(colWidth);
    }

    private void PopulateTree(bool left)
    {
        var tree = left ? LeftTree : RightTree;
        tree.Items.Clear();

        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var node = new TreeViewItem
                {
                    Header = drive.Name,
                    Tag = drive.RootDirectory.FullName
                };
                node.Items.Add("Wczytywanie...");
                node.Expanded += TreeItem_Expanded;
                tree.Items.Add(node);
            }
        }
        catch { }
    }

    private void TreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem item && item.Items.Count == 1 && item.Items[0] is string && (string)item.Items[0] == "Wczytywanie...")
        {
            item.Items.Clear();
            string path = item.Tag?.ToString() ?? "";
            try
            {
                var opt = new EnumerationOptions { IgnoreInaccessible = true };
                foreach (var dir in Directory.EnumerateDirectories(path, "*", opt))
                {
                    var info = new DirectoryInfo(dir);
                    var subNode = new TreeViewItem
                    {
                        Header = info.Name,
                        Tag = dir
                    };
                    subNode.Items.Add("Wczytywanie...");
                    subNode.Expanded += TreeItem_Expanded;
                    item.Items.Add(subNode);
                }
            }
            catch { }
        }
    }

    private void LeftTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (LeftTree.SelectedItem is TreeViewItem item && item.Tag is string path)
            Navigate(true, path);
    }

    private void RightTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (RightTree.SelectedItem is TreeViewItem item && item.Tag is string path)
            Navigate(false, path);
    }
    #endregion

    #region Klient FTP / SFTP (Ctrl+F)
    private void FtpManager_Click(object sender, RoutedEventArgs e)
    {
        var win = new Window
        {
            Title = "Menedżer połączeń FTP / SFTP (Ctrl+F) — Szperacz 1.6.1 PRO",
            Width = 620, Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(16, 23, 34))
        };

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topLbl = new TextBlock { Text = "Zapisane serwery FTP (wybierz lub skonfiguruj nowe połączenie):", Foreground = Brushes.White, Margin = new Thickness(0,0,0,8) };
        Grid.SetRow(topLbl, 0); grid.Children.Add(topLbl);

        var list = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 35, 51)),
            Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Margin = new Thickness(0,0,0,10)
        };
        foreach (var b in _ftpBookmarks) list.Items.Add($"{b.Name}  →  {b.Url} ({b.User})");
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        Grid.SetRow(list, 1); grid.Children.Add(list);

        var formGrid = new Grid { Margin = new Thickness(0,0,0,10) };
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var l1 = new TextBlock { Text = "Serwer:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,2,4,2) };
        var txtServer = new TextBox { Text = "ftp://test.rebex.net/", Margin = new Thickness(0,2,8,2) };
        var l2 = new TextBlock { Text = "Nazwa:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,2,4,2) };
        var txtName = new TextBox { Text = "Rebex Public Test", Margin = new Thickness(0,2,0,2) };

        var l3 = new TextBlock { Text = "Użytkownik:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,2,4,2) };
        var txtUser = new TextBox { Text = "demo", Margin = new Thickness(0,2,8,2) };
        var l4 = new TextBlock { Text = "Hasło:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,2,4,2) };
        var txtPass = new PasswordBox { Password = "password", Margin = new Thickness(0,2,0,2) };

        Grid.SetRow(l1, 0); Grid.SetColumn(l1, 0); Grid.SetRow(txtServer, 0); Grid.SetColumn(txtServer, 1);
        Grid.SetRow(l2, 0); Grid.SetColumn(l2, 2); Grid.SetRow(txtName, 0); Grid.SetColumn(txtName, 3);
        Grid.SetRow(l3, 1); Grid.SetColumn(l3, 0); Grid.SetRow(txtUser, 1); Grid.SetColumn(txtUser, 1);
        Grid.SetRow(l4, 1); Grid.SetColumn(l4, 2); Grid.SetRow(txtPass, 1); Grid.SetColumn(txtPass, 3);

        formGrid.Children.Add(l1); formGrid.Children.Add(txtServer); formGrid.Children.Add(l2); formGrid.Children.Add(txtName);
        formGrid.Children.Add(l3); formGrid.Children.Add(txtUser); formGrid.Children.Add(l4); formGrid.Children.Add(txtPass);
        Grid.SetRow(formGrid, 2); grid.Children.Add(formGrid);

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex >= 0 && list.SelectedIndex < _ftpBookmarks.Count)
            {
                var bm = _ftpBookmarks[list.SelectedIndex];
                txtServer.Text = bm.Url;
                txtName.Text = bm.Name;
                txtUser.Text = bm.User;
                txtPass.Password = bm.Pass;
            }
        };

        var btnBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnSave = new Button { Content = "Zapisz serwer", Padding = new Thickness(8,4,8,4), Margin = new Thickness(0,0,6,0) };
        var btnConnect = new Button { Content = "🌐 Otwórz w aktywnym panelu", FontWeight = FontWeights.Bold, Padding = new Thickness(12,4,12,4), Margin = new Thickness(0,0,6,0) };
        var btnClose = new Button { Content = "Zamknij", Padding = new Thickness(8,4,8,4) };

        btnBar.Children.Add(btnSave); btnBar.Children.Add(btnConnect); btnBar.Children.Add(btnClose);
        Grid.SetRow(btnBar, 3); grid.Children.Add(btnBar);

        btnSave.Click += (_, _) =>
        {
            string url = txtServer.Text.Trim();
            string name = txtName.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = url;
            var existing = _ftpBookmarks.FirstOrDefault(b => b.Url == url);
            if (existing != null) { existing.Name = name; existing.User = txtUser.Text.Trim(); existing.Pass = txtPass.Password; }
            else { _ftpBookmarks.Add(new FtpBookmark { Name = name, Url = url, User = txtUser.Text.Trim(), Pass = txtPass.Password }); }

            list.Items.Clear();
            foreach (var b in _ftpBookmarks) list.Items.Add($"{b.Name}  →  {b.Url} ({b.User})");
            SetStatus($"Zapisano serwer FTP: {name}");
        };

        btnConnect.Click += (_, _) =>
        {
            string url = txtServer.Text.Trim();
            if (!url.EndsWith("/")) url += "/";

            var session = new FtpSession
            {
                BaseUrl = url,
                CurrentUrl = url,
                User = txtUser.Text.Trim(),
                Pass = txtPass.Password
            };

            if (_isLeftActive)
            {
                _leftFtpSession = session;
                _leftPathValue = url;
            }
            else
            {
                _rightFtpSession = session;
                _rightPathValue = url;
            }

            LoadPanel(_isLeftActive);
            win.Close();
        };

        btnClose.Click += (_, _) => win.Close();

        win.Content = grid;
        win.ShowDialog();
    }
    #endregion

    #region Shell Icons
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static ImageSource? GetSystemIcon(string path, bool isDirectory)
    {
        string ext = isDirectory ? ":directory:" : Path.GetExtension(path).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ":no_ext:";

        if (_iconCache.TryGetValue(ext, out var cached))
            return cached;

        var shinfo = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
        uint attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

        IntPtr res = SHGetFileInfo(path, attrs, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
        if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
        {
            try
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(shinfo.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                _iconCache[ext] = bs;
                return bs;
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }
        return null;
    }
    #endregion

    #region Obsługa dysków
    private void RefreshDrives()
    {
        LeftDrivesPanel.Children.Clear();
        RightDrivesPanel.Children.Clear();

        // Przycisk szybkiego wywołania menedżera FTP na pasku dysków
        var btnFtpL = new Button
        {
            Content = "🌐 FTP",
            Margin = new Thickness(0, 0, 5, 2),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(35, 60, 90)),
            Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(84, 182, 167))
        };
        btnFtpL.Click += (_, _) => FtpManager_Click(this, new RoutedEventArgs());
        LeftDrivesPanel.Children.Add(btnFtpL);

        var btnFtpR = new Button
        {
            Content = "🌐 FTP",
            Margin = new Thickness(0, 0, 5, 2),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(35, 60, 90)),
            Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(84, 182, 167))
        };
        btnFtpR.Click += (_, _) => FtpManager_Click(this, new RoutedEventArgs());
        RightDrivesPanel.Children.Add(btnFtpR);

        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable) && d.IsReady)
                .ToList();

            foreach (var d in drives)
            {
                var lbl = $"{d.Name.TrimEnd('\\')} [{FormatSize(d.AvailableFreeSpace)}]";
                LeftDrivesPanel.Children.Add(CreateDriveBtn(lbl, d.RootDirectory.FullName, true));
                RightDrivesPanel.Children.Add(CreateDriveBtn(lbl, d.RootDirectory.FullName, false));
            }
        }
        catch (Exception ex) { SetStatus("Błąd dysków: " + ex.Message); }
    }

    private Button CreateDriveBtn(string label, string path, bool isLeft)
    {
        var b = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 5, 2),
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(32, 46, 64)),
            Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(53, 71, 92)),
            Tag = path
        };
        b.Click += (_, _) => Navigate(isLeft, path);
        return b;
    }
    #endregion

    #region Obsługa Zakładek (Tabs)
    private void RenderTabs(bool left)
    {
        var panel = left ? LeftTabsPanel : RightTabsPanel;
        var tabs = left ? _leftTabs : _rightTabs;
        int activeIdx = left ? _leftTabIndex : _rightTabIndex;

        panel.Children.Clear();
        for (int i = 0; i < tabs.Count; i++)
        {
            int idx = i;
            var tab = tabs[i];
            bool isActive = (idx == activeIdx);

            var tabBtn = new Button
            {
                Content = $"{tab.Title} ✕",
                Margin = new Thickness(0, 0, 4, 2),
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 11,
                Background = isActive ? new SolidColorBrush(Color.FromRgb(84, 182, 167)) : new SolidColorBrush(Color.FromRgb(24, 35, 51)),
                Foreground = isActive ? new SolidColorBrush(Color.FromRgb(16, 23, 34)) : new SolidColorBrush(Color.FromRgb(200, 210, 220)),
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal
            };

            tabBtn.Click += (_, _) =>
            {
                if (left) _leftTabIndex = idx; else _rightTabIndex = idx;
                Navigate(left, tab.Path, keepTab: true);
                RenderTabs(left);
            };

            panel.Children.Add(tabBtn);
        }

        var addBtn = new Button
        {
            Content = "+",
            Width = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 2),
            Background = new SolidColorBrush(Color.FromRgb(32, 46, 64)),
            Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248))
        };
        addBtn.Click += (_, _) => AddNewTab(left);
        panel.Children.Add(addBtn);
    }

    private void AddNewTab(bool left)
    {
        var currentPath = left ? _leftPathValue : _rightPathValue;
        var tabs = left ? _leftTabs : _rightTabs;
        var name = Path.GetFileName(currentPath.TrimEnd('\\'));
        if (string.IsNullOrEmpty(name)) name = currentPath.TrimEnd('\\');

        tabs.Add(new TabItemModel { Title = name, Path = currentPath });
        if (left) _leftTabIndex = tabs.Count - 1; else _rightTabIndex = tabs.Count - 1;
        RenderTabs(left);
        LoadPanel(left);
    }

    private void CloseCurrentTab(bool left)
    {
        var tabs = left ? _leftTabs : _rightTabs;
        if (tabs.Count <= 1) return;

        int activeIdx = left ? _leftTabIndex : _rightTabIndex;
        tabs.RemoveAt(activeIdx);
        if (activeIdx >= tabs.Count) activeIdx = tabs.Count - 1;

        if (left) _leftTabIndex = activeIdx; else _rightTabIndex = activeIdx;
        Navigate(left, tabs[activeIdx].Path, keepTab: true);
        RenderTabs(left);
    }
    #endregion

    #region Bezpieczne Wczytywanie Paneli (Odporność na brak uprawnień)
    private void LoadPanel(bool left)
    {
        var path = left ? _leftPathValue : _rightPathValue;
        if (path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
        {
            _ = LoadFtpDirectoryAsync(left, path);
            return;
        }

        if (left) _leftFtpSession = null; else _rightFtpSession = null;

        var box = left ? LeftPath : RightPath;
        var rawList = left ? _leftRawEntries : _rightRawEntries;
        var summary = left ? LeftSummaryText : RightSummaryText;

        box.Text = path;
        rawList.Clear();

        if (Directory.GetParent(path) != null)
        {
            rawList.Add(new FileEntry
            {
                Name = "..",
                FullPath = Directory.GetParent(path)!.FullName,
                Kind = "<W GÓRĘ>",
                SizeText = "",
                ModifiedText = "",
                IsDirectory = true,
                IconImage = GetSystemIcon(path, true)
            });
        }

        try
        {
            var opt = new EnumerationOptions { IgnoreInaccessible = true, ReturnSpecialDirectories = false };

            foreach (var d in Directory.EnumerateDirectories(path, "*", opt).OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    var info = new DirectoryInfo(d);
                    rawList.Add(new FileEntry
                    {
                        Name = info.Name,
                        FullPath = d,
                        Kind = "<KATALOG>",
                        SizeText = "—",
                        ModifiedDate = info.LastWriteTime,
                        ModifiedText = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        IsDirectory = true,
                        IconImage = GetSystemIcon(d, true)
                    });
                }
                catch { }
            }

            foreach (var f in Directory.EnumerateFiles(path, "*", opt).OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    var info = new FileInfo(f);
                    rawList.Add(new FileEntry
                    {
                        Name = info.Name,
                        FullPath = f,
                        Kind = info.Extension.TrimStart('.').ToUpperInvariant(),
                        SizeBytes = info.Length,
                        SizeText = FormatSize(info.Length),
                        ModifiedDate = info.LastWriteTime,
                        ModifiedText = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        IconImage = GetSystemIcon(f, false)
                    });
                }
                catch { }
            }

            ApplyFilter(left);
            UpdateTabTitle(left);
            UpdateCliPrefix();
            SetStatus($"Katalog: {path}");
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus($"[Brak uprawnień] Odmowa dostępu do katalogu: {path}");
            summary.Text = "Odmowa dostępu do zawartości tego folderu.";
        }
        catch (Exception ex)
        {
            SetStatus("Błąd odczytu: " + ex.Message);
        }
    }

    private static (string name, bool isDir, long size, DateTime date) ParseFtpLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line)) return ("", false, 0, DateTime.MinValue);

        if (line.StartsWith("d") || line.StartsWith("-") || line.StartsWith("l"))
        {
            bool isDir = line.StartsWith("d");
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 9)
            {
                long.TryParse(parts[4], out long sz);
                string name = string.Join(" ", parts.Skip(8));
                return (name, isDir, sz, DateTime.Now);
            }
        }

        if (line.Contains("<DIR>"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int idx = Array.IndexOf(parts, "<DIR>");
            if (idx >= 0 && idx + 1 < parts.Length)
                return (string.Join(" ", parts.Skip(idx + 1)), true, 0, DateTime.Now);
        }
        else
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && long.TryParse(parts[2], out long sz))
                return (string.Join(" ", parts.Skip(3)), false, sz, DateTime.Now);
        }

        return (line, false, 0, DateTime.Now);
    }

    private static string GetParentFtpUrl(string url)
    {
        url = url.TrimEnd('/');
        int lastSlash = url.LastIndexOf('/');
        if (lastSlash > "ftp://".Length)
            return url.Substring(0, lastSlash) + "/";
        return url + "/";
    }

    private async Task LoadFtpDirectoryAsync(bool left, string url)
    {
        var session = left ? _leftFtpSession : _rightFtpSession;
        if (session == null)
        {
            // Próba odnalezienia sesji w zakładkach
            var found = _ftpBookmarks.FirstOrDefault(b => url.StartsWith(b.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                session = new FtpSession { BaseUrl = found.Url, CurrentUrl = url, User = found.User, Pass = found.Pass };
                if (left) _leftFtpSession = session; else _rightFtpSession = session;
            }
            else return;
        }

        var rawList = left ? _leftRawEntries : _rightRawEntries;
        var box = left ? LeftPath : RightPath;
        var summary = left ? LeftSummaryText : RightSummaryText;

        box.Text = url;
        rawList.Clear();

        string parentUrl = GetParentFtpUrl(url);
        if (parentUrl != url && parentUrl != "ftp://")
        {
            rawList.Add(new FileEntry
            {
                Name = "..",
                FullPath = parentUrl,
                Kind = "<W GÓRĘ>",
                SizeText = "",
                ModifiedText = "",
                IsDirectory = true,
                IconImage = GetSystemIcon("folder", true)
            });
        }

        SetStatus($"Wczytywanie serwera FTP: {url}...");
        try
        {
            var entries = await Task.Run(() =>
            {
                var list = new List<FileEntry>();
                var request = (FtpWebRequest)WebRequest.Create(url);
                request.Method = WebRequestMethods.Ftp.ListDirectoryDetails;
                request.Credentials = new NetworkCredential(session.User, session.Pass);

                using var response = (FtpWebResponse)request.GetResponse();
                using var reader = new StreamReader(response.GetResponseStream());

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var (name, isDir, size, date) = ParseFtpLine(line);
                    if (string.IsNullOrEmpty(name) || name == "." || name == "..") continue;

                    string full = url.TrimEnd('/') + "/" + name + (isDir ? "/" : "");
                    list.Add(new FileEntry
                    {
                        Name = name,
                        FullPath = full,
                        Kind = isDir ? "<KATALOG>" : Path.GetExtension(name).TrimStart('.').ToUpperInvariant(),
                        SizeBytes = size,
                        SizeText = isDir ? "—" : FormatSize(size),
                        ModifiedDate = date,
                        ModifiedText = date.ToString("yyyy-MM-dd HH:mm"),
                        IsDirectory = isDir,
                        IconImage = GetSystemIcon(name, isDir)
                    });
                }
                return list;
            });

            foreach (var e in entries.OrderBy(x => !x.IsDirectory).ThenBy(x => x.Name))
                rawList.Add(e);

            ApplyFilter(left);
            UpdateTabTitle(left);
            UpdateCliPrefix();
            SetStatus($"Połączono z FTP: {url}");
        }
        catch (Exception ex)
        {
            SetStatus($"Błąd FTP: {ex.Message}");
            summary.Text = "Błąd połączenia ze zdalnym serwerem FTP.";
        }
    }

    private void UpdateTabTitle(bool left)
    {
        var path = left ? _leftPathValue : _rightPathValue;
        var tabs = left ? _leftTabs : _rightTabs;
        int activeIdx = left ? _leftTabIndex : _rightTabIndex;
        if (activeIdx >= 0 && activeIdx < tabs.Count)
        {
            var name = Path.GetFileName(path.TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(name)) name = path.TrimEnd('/', '\\');
            tabs[activeIdx].Title = name;
            tabs[activeIdx].Path = path;
            RenderTabs(left);
        }
    }

    private void ApplyFilter(bool left)
    {
        var rawList = left ? _leftRawEntries : _rightRawEntries;
        var grid = left ? LeftGrid : RightGrid;
        var filterBox = left ? LeftFilterBox : RightFilterBox;
        var summary = left ? LeftSummaryText : RightSummaryText;

        string filter = filterBox.Text.Trim();
        IEnumerable<FileEntry> items = rawList;

        if (!string.IsNullOrEmpty(filter))
        {
            string pattern = "^" + Regex.Escape(filter).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            items = rawList.Where(x => x.Name == ".." || regex.IsMatch(x.Name));
        }

        var visible = items.ToList();
        grid.ItemsSource = visible;

        int dirs = visible.Count(x => x.IsDirectory && x.Name != "..");
        int files = visible.Count(x => !x.IsDirectory);
        long totalBytes = visible.Where(x => !x.IsDirectory).Sum(x => x.SizeBytes);

        summary.Text = $"Widoczne: {dirs} folderów, {files} plików ({FormatSize(totalBytes)})" +
                       (string.IsNullOrEmpty(filter) ? "" : $" [filtr: {filter}]");
    }

    private void Navigate(bool left, string path, bool keepTab = false)
    {
        try
        {
            if (path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
            {
                if (left) _leftPathValue = path; else _rightPathValue = path;
                LoadPanel(left);
                return;
            }

            if (!Directory.Exists(path))
            {
                SetStatus($"Katalog nie istnieje: {path}");
                return;
            }
            if (left) _leftPathValue = path; else _rightPathValue = path;
            LoadPanel(left);
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private static string FormatSize(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.##} {u[i]}";
    }

    private void OpenSelected(bool left)
    {
        var grid = left ? LeftGrid : RightGrid;
        if (grid.SelectedItem is not FileEntry item) return;
        if (item.IsDirectory) Navigate(left, item.FullPath);
        else
        {
            if (item.FullPath.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show($"Plik zdalny na serwerze:\n{item.FullPath}\n\nNaciśnij F5, aby pobrać go na dysk lokalny.", "Szperacz FTP");
                return;
            }

            try { Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true }); }
            catch (Exception ex) { SetStatus(ex.Message); }
        }
    }

    private void LeftGrid_DoubleClick(object s, MouseButtonEventArgs e) => OpenSelected(true);
    private void RightGrid_DoubleClick(object s, MouseButtonEventArgs e) => OpenSelected(false);
    private void LeftUp_Click(object s, RoutedEventArgs e) { var p = Directory.GetParent(_leftPathValue); if (p != null) Navigate(true, p.FullName); }
    private void RightUp_Click(object s, RoutedEventArgs e) { var p = Directory.GetParent(_rightPathValue); if (p != null) Navigate(false, p.FullName); }
    private void Refresh_Click(object s, RoutedEventArgs e) { RefreshDrives(); LoadPanel(true); LoadPanel(false); }
    private void LeftPath_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(true, LeftPath.Text); }
    private void RightPath_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(false, RightPath.Text); }
    private void Grid_GotFocus(object s, RoutedEventArgs e)
    {
        _isLeftActive = (s == LeftGrid);
        UpdateCliPrefix();
    }

    private void LeftFilterBox_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter(true);
    private void RightFilterBox_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter(false);
    private void LeftFilterClear_Click(object s, RoutedEventArgs e) { LeftFilterBox.Text = ""; LeftFilterBar.Visibility = Visibility.Collapsed; }
    private void RightFilterClear_Click(object s, RoutedEventArgs e) { RightFilterBox.Text = ""; RightFilterBar.Visibility = Visibility.Collapsed; }
    #endregion

    #region Klawiszologia (Insert, Spacja, Num +/-, Tab, F2-F8, Ctrl+M, Ctrl+D, Ctrl+~, Ctrl+Q)
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox tb && tb != LeftPath && tb != RightPath)
        {
            if (e.Key == Key.Escape)
            {
                if (tb == LeftFilterBox) LeftFilterClear_Click(this, new RoutedEventArgs());
                if (tb == RightFilterBox) RightFilterClear_Click(this, new RoutedEventArgs());
                (_isLeftActive ? LeftGrid : RightGrid).Focus();
                e.Handled = true;
            }
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            if (e.SystemKey == Key.T || e.Key == Key.T) { e.Handled = true; ToggleTree_Click(this, new RoutedEventArgs()); return; }
            if (e.SystemKey == Key.F7 || e.Key == Key.F7) { e.Handled = true; Search_Click(this, new RoutedEventArgs()); return; }
            if (e.SystemKey == Key.F5 || e.Key == Key.F5) { e.Handled = true; PackZip_Click(this, new RoutedEventArgs()); return; }
            if (e.SystemKey == Key.F9 || e.Key == Key.F9) { e.Handled = true; UnpackZip_Click(this, new RoutedEventArgs()); return; }
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.F) { e.Handled = true; FtpManager_Click(this, new RoutedEventArgs()); return; }
            if (e.Key == Key.OemTilde || e.Key == Key.OemQuotes) { e.Handled = true; OpenTerminal_Click(this, new RoutedEventArgs()); return; }
            if (e.Key == Key.Q) { e.Handled = true; View_Click(this, new RoutedEventArgs()); return; }
            if (e.Key == Key.M) { e.Handled = true; MultiRename_Click(this, new RoutedEventArgs()); return; }
            if (e.Key == Key.D) { e.Handled = true; Bookmarks_Click(this, new RoutedEventArgs()); return; }
            if (e.Key == Key.T) { e.Handled = true; AddNewTab(_isLeftActive); return; }
            if (e.Key == Key.W) { e.Handled = true; CloseCurrentTab(_isLeftActive); return; }
            if (e.Key == Key.R) { e.Handled = true; Refresh_Click(this, new RoutedEventArgs()); return; }
            if (e.Key == Key.F12) { e.Handled = true; ToggleFilterBar(_isLeftActive); return; }
            if (e.Key == Key.A) { e.Handled = true; SelectAll(true); return; }
        }

        switch (e.Key)
        {
            case Key.Tab:
                e.Handled = true;
                _isLeftActive = !_isLeftActive;
                (_isLeftActive ? LeftGrid : RightGrid).Focus();
                UpdateCliPrefix();
                break;

            case Key.Space:
            case Key.Insert:
                e.Handled = true;
                ToggleCurrentSelection();
                break;

            case Key.Add:
            case Key.OemPlus:
                e.Handled = true;
                PromptSelectPattern(select: true);
                break;

            case Key.Subtract:
            case Key.OemMinus:
                e.Handled = true;
                PromptSelectPattern(select: false);
                break;

            case Key.F2:
                e.Handled = true;
                Rename_Click(this, new RoutedEventArgs());
                break;
            case Key.F3:
                e.Handled = true;
                View_Click(this, new RoutedEventArgs());
                break;
            case Key.F4:
                e.Handled = true;
                Edit_Click(this, new RoutedEventArgs());
                break;
            case Key.F5:
                e.Handled = true;
                Copy_Click(this, new RoutedEventArgs());
                break;
            case Key.F6:
                e.Handled = true;
                Move_Click(this, new RoutedEventArgs());
                break;
            case Key.F7:
                e.Handled = true;
                NewFolder_Click(this, new RoutedEventArgs());
                break;
            case Key.F8:
            case Key.Delete:
                e.Handled = true;
                Delete_Click(this, new RoutedEventArgs());
                break;
        }
    }

    private void ToggleFilterBar(bool left)
    {
        var bar = left ? LeftFilterBar : RightFilterBar;
        var box = left ? LeftFilterBox : RightFilterBox;
        bar.Visibility = (bar.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        if (bar.Visibility == Visibility.Visible) box.Focus();
    }

    private void ToggleCurrentSelection()
    {
        var grid = _isLeftActive ? LeftGrid : RightGrid;
        if (grid.SelectedItem is not FileEntry current || current.Name == "..") return;

        int idx = grid.SelectedIndex;
        if (grid.SelectedItems.Contains(current))
            grid.SelectedItems.Remove(current);
        else
            grid.SelectedItems.Add(current);

        if (idx + 1 < grid.Items.Count)
        {
            grid.SelectedIndex = idx + 1;
            grid.ScrollIntoView(grid.Items[idx + 1]);
        }
    }

    private void SelectAll(bool select)
    {
        var grid = _isLeftActive ? LeftGrid : RightGrid;
        if (select) grid.SelectAll(); else grid.UnselectAll();
    }

    private void PromptSelectPattern(bool select)
    {
        var action = select ? "Zaznacz według wzorca (np. *.txt, *.png)" : "Odznacz według wzorca";
        var pattern = AskText(action, "*.*");
        if (string.IsNullOrWhiteSpace(pattern)) return;

        var grid = _isLeftActive ? LeftGrid : RightGrid;
        var regexStr = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        var regex = new Regex(regexStr, RegexOptions.IgnoreCase);

        foreach (var item in grid.Items.OfType<FileEntry>())
        {
            if (item.Name == "..") continue;
            if (regex.IsMatch(item.Name))
            {
                if (select) { if (!grid.SelectedItems.Contains(item)) grid.SelectedItems.Add(item); }
                else { if (grid.SelectedItems.Contains(item)) grid.SelectedItems.Remove(item); }
            }
        }
    }

    private FileEntry[] GetActiveSelected()
    {
        var grid = _isLeftActive ? LeftGrid : RightGrid;
        var sel = grid.SelectedItems.Cast<FileEntry>().Where(x => x.Name != "..").ToArray();
        if (sel.Length == 0 && grid.SelectedItem is FileEntry single && single.Name != "..")
            return [single];
        return sel;
    }

    private FileEntry? GetActiveCurrent()
    {
        var grid = _isLeftActive ? LeftGrid : RightGrid;
        return grid.SelectedItem as FileEntry;
    }
    #endregion

    #region Wiersz poleceń CLI (Command Line Bar)
    private void CliExecute_Click(object sender, RoutedEventArgs e) => ExecuteCliCommand();

    private void CliInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteCliCommand();
            e.Handled = true;
        }
    }

    private void ExecuteCliCommand()
    {
        string cmd = CliInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(cmd)) return;

        string workDir = ActivePath;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k {cmd}",
                WorkingDirectory = workDir,
                UseShellExecute = true
            });
            SetStatus($"Wykonano polecenie: {cmd}");
            CliInputBox.Text = "";
            ( _isLeftActive ? LeftGrid : RightGrid ).Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Błąd uruchomienia polecenia:\n" + ex.Message, "Wiersz poleceń");
        }
    }

    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        string workDir = ActivePath;
        try
        {
            if (GetCommandPath("wt.exe") != null)
            {
                Process.Start(new ProcessStartInfo { FileName = "wt.exe", Arguments = $"-d \"{workDir}\"", UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo { FileName = "powershell.exe", WorkingDirectory = workDir, UseShellExecute = true });
            }
            SetStatus($"Otwarto terminal w: {workDir}");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Błąd otwierania terminala:\n" + ex.Message, "Terminal");
        }
    }

    private void OpenVSCode_Click(object sender, RoutedEventArgs e)
    {
        string workDir = ActivePath;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c code \"{workDir}\"", CreateNoWindow = true, UseShellExecute = false });
            SetStatus($"Uruchomiono VS Code w: {workDir}");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Nie można uruchomić VS Code (upewnij się, że polecenie 'code' jest w PATH):\n" + ex.Message, "VS Code");
        }
    }

    private static string? GetCommandPath(string cmd)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var p in paths)
        {
            var full = Path.Combine(p, cmd);
            if (File.Exists(full)) return full;
        }
        return null;
    }
    #endregion

    #region Zaawansowany Lister / Quick View (F3 / Ctrl+Q z grafiką i Hex)
    private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".webp"
    };

    private static readonly HashSet<string> _binaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bin", ".dat", ".iso", ".sys", ".obj", ".class", ".so"
    };

    private void View_Click(object s, RoutedEventArgs e)
    {
        var item = GetActiveCurrent();
        if (item == null || item.IsDirectory || item.Name == "..") return;

        string ext = Path.GetExtension(item.FullPath).ToLowerInvariant();

        if (_imageExtensions.Contains(ext))
        {
            ShowImageViewer(item);
        }
        else if (_binaryExtensions.Contains(ext))
        {
            ShowHexViewer(item);
        }
        else
        {
            ShowTextViewer(item);
        }
    }

    private void ShowImageViewer(FileEntry item)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(item.FullPath);
            bitmap.EndInit();
            bitmap.Freeze();

            var win = new Window
            {
                Title = $"Podgląd obrazu (F3) — {item.Name} [{bitmap.PixelWidth}x{bitmap.PixelHeight} px]",
                Width = 900, Height = 650,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(16, 23, 34))
            };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var img = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            scroll.Content = img;
            Grid.SetRow(scroll, 0);
            grid.Children.Add(scroll);

            var info = new TextBlock
            {
                Text = $"{item.FullPath}  •  {bitmap.PixelWidth} × {bitmap.PixelHeight} px  •  {item.SizeText}",
                Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0,6,0,0)
            };
            Grid.SetRow(info, 1);
            grid.Children.Add(info);

            win.Content = grid;
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Nie można wczytać grafiki:\n" + ex.Message, "Lister");
        }
    }

    private void ShowHexViewer(FileEntry item)
    {
        try
        {
            byte[] bytes;
            using (var fs = File.OpenRead(item.FullPath))
            {
                int toRead = (int)Math.Min(fs.Length, 16384);
                bytes = new byte[toRead];
                fs.Read(bytes, 0, toRead);
            }

            var sb = new StringBuilder();
            sb.AppendLine("OFFSET    00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F   ASCII");
            sb.AppendLine(new string('-', 72));

            for (int i = 0; i < bytes.Length; i += 16)
            {
                sb.Append($"{i:X8}  ");
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < bytes.Length)
                        sb.Append($"{bytes[i + j]:X2} ");
                    else
                        sb.Append("   ");
                    if (j == 7) sb.Append(" ");
                }
                sb.Append(" ");
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < bytes.Length)
                    {
                        byte b = bytes[i + j];
                        sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
                    }
                }
                sb.AppendLine();
            }

            if (item.SizeBytes > 16384)
                sb.AppendLine($"\n[... Obcięto widok HEX do pierwszych 16 KB z {item.SizeText} ...]");

            ShowListerWindow($"Podgląd HEX (F3) — {item.Name}", sb.ToString());
        }
        catch (Exception ex)
        {
            MessageBox.Show("Błąd odczytu binarnego:\n" + ex.Message, "Lister HEX");
        }
    }

    private void ShowTextViewer(FileEntry item)
    {
        try
        {
            var fi = new FileInfo(item.FullPath);
            string content;
            if (fi.Length > 2 * 1024 * 1024)
            {
                using var stream = fi.OpenRead();
                using var r = new StreamReader(stream);
                char[] b = new char[8192];
                int read = r.Read(b, 0, b.Length);
                content = new string(b, 0, read) + "\n\n[... Obcięto podgląd pliku powyżej 2 MB ...]";
            }
            else content = File.ReadAllText(item.FullPath);

            ShowListerWindow($"Podgląd tekstu (F3) — {item.Name} ({FormatSize(fi.Length)})", content);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Błąd podglądu:\n" + ex.Message, "Szperacz");
        }
    }

    private void ShowListerWindow(string title, string text)
    {
        var win = new Window
        {
            Title = title,
            Width = 920, Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(16, 23, 34))
        };
        win.Content = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)),
            Background = new SolidColorBrush(Color.FromRgb(24, 35, 51)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12)
        };
        win.ShowDialog();
    }
    #endregion

    #region Menu kontekstowe
    private void ContextOpen_Click(object s, RoutedEventArgs e) => OpenSelected(_isLeftActive);

    private void ContextCopyPath_Click(object s, RoutedEventArgs e)
    {
        var item = GetActiveCurrent();
        if (item != null && item.Name != "..")
        {
            Clipboard.SetText(item.FullPath);
            SetStatus($"Skopiowano ścieżkę: {item.FullPath}");
        }
    }

    private void ContextOpenExplorer_Click(object s, RoutedEventArgs e)
    {
        var item = GetActiveCurrent();
        if (item != null && item.Name != "..")
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true });
        }
        else
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ActivePath}\"") { UseShellExecute = true });
        }
    }
    #endregion

    #region F4 Edycja
    private void Edit_Click(object s, RoutedEventArgs e)
    {
        var item = GetActiveCurrent();
        if (item == null || item.IsDirectory || item.Name == "..") return;
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{item.FullPath}\"") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Błąd edytora:\n" + ex.Message, "Szperacz"); }
    }
    #endregion

    #region F5 Kopiowanie i F6 Przenoszenie (Asynchroniczne)
    private void Copy_Click(object s, RoutedEventArgs e) => Transfer(move: false);
    private void Move_Click(object s, RoutedEventArgs e) => Transfer(move: true);

    private async void Transfer(bool move)
    {
        if (_isBusy) return;
        var selected = GetActiveSelected();
        if (selected.Length == 0) { SetStatus("Zaznacz elementy do transferu."); return; }

        string srcPath = _isLeftActive ? _leftPathValue : _rightPathValue;
        string dstPath = _isLeftActive ? _rightPathValue : _leftPathValue;

        bool srcIsFtp = srcPath.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase);
        bool dstIsFtp = dstPath.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase);

        var act = move ? "Przenoszenie" : "Kopiowanie";
        if (MessageBox.Show($"{act} {selected.Length} elementów do:\n{dstPath}\n\nKontynuować?", act, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        _isBusy = true;
        SetProgress(true, 0);
        SetStatus($"{act} w toku...");
        var progress = new Progress<int>(p => SetProgress(true, p));

        try
        {
            if (!srcIsFtp && dstIsFtp)
            {
                // UPLOAD NA FTP
                var session = _isLeftActive ? _rightFtpSession : _leftFtpSession;
                if (session == null) { MessageBox.Show("Brak aktywnej sesji FTP w panelu docelowym!", "Błąd FTP"); return; }

                await Task.Run(() =>
                {
                    for (int i = 0; i < selected.Length; i++)
                    {
                        var item = selected[i];
                        if (item.IsDirectory) continue;

                        string uploadUrl = dstPath.TrimEnd('/') + "/" + item.Name;
                        var req = (FtpWebRequest)WebRequest.Create(uploadUrl);
                        req.Method = WebRequestMethods.Ftp.UploadFile;
                        req.Credentials = new NetworkCredential(session.User, session.Pass);
                        req.UseBinary = true;

                        byte[] fileBytes = File.ReadAllBytes(item.FullPath);
                        req.ContentLength = fileBytes.Length;
                        using (var reqStream = req.GetRequestStream())
                        {
                            reqStream.Write(fileBytes, 0, fileBytes.Length);
                        }

                        if (move) File.Delete(item.FullPath);
                        ((IProgress<int>)progress).Report((int)((i + 1) * 100.0 / selected.Length));
                    }
                });
            }
            else if (srcIsFtp && !dstIsFtp)
            {
                // DOWNLOAD Z FTP
                var session = _isLeftActive ? _leftFtpSession : _rightFtpSession;
                if (session == null) { MessageBox.Show("Brak aktywnej sesji FTP w panelu źródłowym!", "Błąd FTP"); return; }

                await Task.Run(() =>
                {
                    for (int i = 0; i < selected.Length; i++)
                    {
                        var item = selected[i];
                        if (item.IsDirectory) continue;

                        string targetFile = Path.Combine(dstPath, item.Name);
                        var req = (FtpWebRequest)WebRequest.Create(item.FullPath);
                        req.Method = WebRequestMethods.Ftp.DownloadFile;
                        req.Credentials = new NetworkCredential(session.User, session.Pass);
                        req.UseBinary = true;

                        using (var resp = (FtpWebResponse)req.GetResponse())
                        using (var s = resp.GetResponseStream())
                        using (var fs = new FileStream(targetFile, FileMode.Create))
                        {
                            s.CopyTo(fs);
                        }
                        ((IProgress<int>)progress).Report((int)((i + 1) * 100.0 / selected.Length));
                    }
                });
            }
            else
            {
                // ZWYKŁY TRANSFER LOKALNY
                if (string.Equals(Path.GetFullPath(srcPath), Path.GetFullPath(dstPath), StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Ścieżka źródłowa i docelowa są identyczne!", "Szperacz");
                    return;
                }

                await Task.Run(() =>
                {
                    for (int i = 0; i < selected.Length; i++)
                    {
                        var item = selected[i];
                        var target = Path.Combine(dstPath, item.Name);
                        if (item.IsDirectory)
                        {
                            if (move) Directory.Move(item.FullPath, target);
                            else CopyDirectory(item.FullPath, target);
                        }
                        else
                        {
                            if (move) File.Move(item.FullPath, target);
                            else File.Copy(item.FullPath, target, overwrite: true);
                        }
                        ((IProgress<int>)progress).Report((int)((i + 1) * 100.0 / selected.Length));
                    }
                });
            }

            LoadPanel(true);
            LoadPanel(false);
            SetStatus($"Zakończono: {act} {selected.Length} elementów.");
        }
        catch (Exception ex) { MessageBox.Show("Błąd transferu:\n" + ex.Message, "Szperacz"); }
        finally { _isBusy = false; SetProgress(false); }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(source))
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(source))
            CopyDirectory(d, Path.Combine(dest, Path.GetFileName(d)));
    }
    #endregion

    #region F8 Bezpieczne usuwanie do Kosza
    private void Delete_Click(object s, RoutedEventArgs e)
    {
        var sel = GetActiveSelected();
        if (sel.Length == 0) return;

        if (MessageBox.Show($"Przenieść {sel.Length} elementów do Kosza?", "Szperacz", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            foreach (var item in sel)
            {
                if (item.IsDirectory)
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        item.FullPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        item.FullPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            }
            LoadPanel(_isLeftActive);
            SetStatus($"Usunięto do Kosza: {sel.Length} elementów.");
        }
        catch (Exception ex) { MessageBox.Show("Błąd usuwania: " + ex.Message, "Szperacz"); }
    }
    #endregion

    #region Nowy folder (F7) i zmiana nazwy (F2)
    private void NewFolder_Click(object s, RoutedEventArgs e)
    {
        var p = _isLeftActive ? _leftPathValue : _rightPathValue;
        var name = AskText("Wpisz nazwę nowego folderu:", "Nowy folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            Directory.CreateDirectory(Path.Combine(p, name));
            LoadPanel(_isLeftActive);
            SetStatus($"Utworzono folder: {name}");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Szperacz"); }
    }

    private void Rename_Click(object s, RoutedEventArgs e)
    {
        var item = GetActiveCurrent();
        if (item == null || item.Name == "..") return;
        var name = AskText("Nowa nazwa:", item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;

        try
        {
            var target = Path.Combine(Path.GetDirectoryName(item.FullPath)!, name);
            if (item.IsDirectory) Directory.Move(item.FullPath, target);
            else File.Move(item.FullPath, target);
            LoadPanel(_isLeftActive);
            SetStatus($"Zmieniono nazwę na: {name}");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Szperacz"); }
    }
    #endregion

    #region Obsługa ZIP (Alt+F5 / Alt+F9)
    private async void PackZip_Click(object s, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var sel = GetActiveSelected();
        if (sel.Length == 0) { SetStatus("Zaznacz elementy do spakowania (Alt+F5)."); return; }

        string targetDir = _isLeftActive ? _rightPathValue : _leftPathValue;
        string defaultName = (sel.Length == 1 ? Path.GetFileNameWithoutExtension(sel[0].Name) : "Archiwum") + ".zip";
        var zipName = AskText("Nazwa pliku archiwum ZIP:", defaultName);
        if (string.IsNullOrWhiteSpace(zipName)) return;

        string zipPath = Path.Combine(targetDir, zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? zipName : zipName + ".zip");
        if (File.Exists(zipPath) && MessageBox.Show("Plik archiwum już istnieje. Nadpisać?", "Szperacz", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        _isBusy = true;
        SetProgress(true, 0);
        SetStatus("Tworzenie archiwum ZIP...");

        try
        {
            await Task.Run(() =>
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                for (int i = 0; i < sel.Length; i++)
                {
                    var item = sel[i];
                    if (item.IsDirectory) AddDirectoryToZip(archive, item.FullPath, item.Name);
                    else archive.CreateEntryFromFile(item.FullPath, item.Name, CompressionLevel.Optimal);
                }
            });

            LoadPanel(true);
            LoadPanel(false);

            if (_showTree)
            {
                PopulateTree(true);
                PopulateTree(false);
            }
            ApplyTreeVisibility();
            SetStatus($"Utworzono archiwum: {zipName}");
        }
        catch (Exception ex) { MessageBox.Show("Błąd pakowania ZIP:\n" + ex.Message, "Szperacz"); }
        finally { _isBusy = false; SetProgress(false); }
    }

    private static void AddDirectoryToZip(ZipArchive archive, string dirPath, string rootEntry)
    {
        foreach (var file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(dirPath, file);
            string entryName = Path.Combine(rootEntry, rel).Replace('\\', '/');
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
    }

    private async void UnpackZip_Click(object s, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var item = GetActiveCurrent();
        if (item == null || !item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Zaznacz plik *.zip do rozpakowania (Alt+F9).");
            return;
        }

        string destDir = _isLeftActive ? _rightPathValue : _leftPathValue;
        if (MessageBox.Show($"Rozpakować {item.Name} do:\n{destDir}?", "Rozpakowywanie ZIP", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        _isBusy = true;
        SetProgress(true, 0);
        SetStatus("Rozpakowywanie archiwum...");

        try
        {
            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(item.FullPath, destDir, overwriteFiles: true);
            });
            LoadPanel(true);
            LoadPanel(false);

            if (_showTree)
            {
                PopulateTree(true);
                PopulateTree(false);
            }
            ApplyTreeVisibility();
            SetStatus($"Rozpakowano: {item.Name}");
        }
        catch (Exception ex) { MessageBox.Show("Błąd rozpakowywania:\n" + ex.Message, "Szperacz"); }
        finally { _isBusy = false; SetProgress(false); }
    }
    #endregion

    #region Zaawansowane szukanie plików (Alt+F7)
    private void Search_Click(object s, RoutedEventArgs e)
    {
        string startPath = ActivePath;

        var searchWin = new Window
        {
            Title = "Szukaj plików (Alt+F7) — OGŚ Szperacz",
            Width = 720, Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(16, 23, 34))
        };

        var mainPanel = new Grid { Margin = new Thickness(12) };
        mainPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lblName = new TextBlock { Text = "Nazwa pliku:", Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,4,0,4) };
        var txtName = new TextBox { Text = "*.*", Margin = new Thickness(0,4,0,4) };
        Grid.SetRow(lblName, 0); Grid.SetColumn(lblName, 0);
        Grid.SetRow(txtName, 0); Grid.SetColumn(txtName, 1);

        var lblPath = new TextBlock { Text = "Szukaj w:", Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,4,0,4) };
        var txtPath = new TextBox { Text = startPath, Margin = new Thickness(0,4,0,4) };
        Grid.SetRow(lblPath, 1); Grid.SetColumn(lblPath, 0);
        Grid.SetRow(txtPath, 1); Grid.SetColumn(txtPath, 1);

        var lblText = new TextBlock { Text = "Zawiera tekst:", Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,4,0,4) };
        var txtText = new TextBox { Text = "", Margin = new Thickness(0,4,0,4) };
        Grid.SetRow(lblText, 2); Grid.SetColumn(lblText, 0);
        Grid.SetRow(txtText, 2); Grid.SetColumn(txtText, 1);

        topGrid.Children.Add(lblName); topGrid.Children.Add(txtName);
        topGrid.Children.Add(lblPath); topGrid.Children.Add(txtPath);
        topGrid.Children.Add(lblText); topGrid.Children.Add(txtText);

        Grid.SetRow(topGrid, 0);
        mainPanel.Children.Add(topGrid);

        var btnBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 8) };
        var btnStart = new Button { Content = "Rozpocznij szukanie", MinWidth = 130, Margin = new Thickness(0,0,8,0) };
        var btnGo = new Button { Content = "Przejdź do pliku", MinWidth = 110 };
        btnBar.Children.Add(btnStart);
        btnBar.Children.Add(btnGo);
        Grid.SetRow(btnBar, 1);
        mainPanel.Children.Add(btnBar);

        var resultList = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 35, 51)),
            Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)),
            FontFamily = new FontFamily("Consolas, monospace"),
            FontSize = 12
        };
        Grid.SetRow(resultList, 2);
        mainPanel.Children.Add(resultList);

        var statusLbl = new TextBlock { Text = "Wpisz kryteria i kliknij Rozpocznij.", Foreground = new SolidColorBrush(Color.FromRgb(158, 173, 189)), Margin = new Thickness(0,6,0,0) };
        Grid.SetRow(statusLbl, 3);
        mainPanel.Children.Add(statusLbl);

        btnStart.Click += async (_, _) =>
        {
            btnStart.IsEnabled = false;
            resultList.Items.Clear();
            statusLbl.Text = "Przeszukiwanie...";

            string root = txtPath.Text.Trim();
            string pattern = txtName.Text.Trim();
            string contentQuery = txtText.Text.Trim();

            if (!Directory.Exists(root))
            {
                statusLbl.Text = "Katalog nie istnieje!";
                btnStart.IsEnabled = true;
                return;
            }

            int count = 0;
            await Task.Run(() =>
            {
                try
                {
                    var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                    foreach (var file in Directory.EnumerateFiles(root, pattern, opt))
                    {
                        if (!string.IsNullOrEmpty(contentQuery))
                        {
                            try
                            {
                                string text = File.ReadAllText(file);
                                if (!text.Contains(contentQuery, StringComparison.OrdinalIgnoreCase))
                                    continue;
                            }
                            catch { continue; }
                        }

                        count++;
                        Dispatcher.Invoke(() => resultList.Items.Add(file));
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => statusLbl.Text = "Błąd: " + ex.Message);
                }
            });

            statusLbl.Text = $"Znaleziono {count} elementów.";
            btnStart.IsEnabled = true;
        };

        btnGo.Click += (_, _) =>
        {
            if (resultList.SelectedItem is string chosenFile && File.Exists(chosenFile))
            {
                string dir = Path.GetDirectoryName(chosenFile)!;
                Navigate(_isLeftActive, dir);
                searchWin.Close();
            }
        };

        searchWin.Content = mainPanel;
        searchWin.ShowDialog();
    }
    #endregion

    #region Narzędzie grupowej zmiany nazw (Multi-Rename Tool / Ctrl+M)
    private sealed class RenamePreviewItem
    {
        public string OriginalName { get; set; } = "";
        public string NewName { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
    }

    private void MultiRename_Click(object s, RoutedEventArgs e)
    {
        var sel = GetActiveSelected();
        if (sel.Length == 0)
        {
            SetStatus("Zaznacz pliki do grupowej zmiany nazwy (Ctrl+M).");
            return;
        }

        var win = new Window
        {
            Title = $"Narzędzie wielokrotnej zmiany nazw (Ctrl+M) — {sel.Length} elementów",
            Width = 860, Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(16, 23, 34))
        };

        var rootGrid = new Grid { Margin = new Thickness(14) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var rulesGrid = new Grid { Margin = new Thickness(0,0,0,10) };
        rulesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        rulesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rulesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        rulesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        rulesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rulesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rulesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lblMask = new TextBlock { Text = "Maska nazwy:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,4,6,4) };
        var txtMask = new TextBox { Text = "[N]_[C]", Margin = new Thickness(0,4,10,4) };
        var lblFind = new TextBlock { Text = "Znajdź:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,4,6,4) };
        var txtFind = new TextBox { Text = "", Margin = new Thickness(0,4,0,4) };

        var lblExt = new TextBlock { Text = "Maska rozsz.:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,4,6,4) };
        var txtExt = new TextBox { Text = "[E]", Margin = new Thickness(0,4,10,4) };
        var lblReplace = new TextBlock { Text = "Zamień na:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,4,6,4) };
        var txtReplace = new TextBox { Text = "", Margin = new Thickness(0,4,0,4) };

        var lblCase = new TextBlock { Text = "Wielkość liter:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,4,6,4) };
        var cmbCase = new ComboBox { Margin = new Thickness(0,4,10,4), SelectedIndex = 0 };
        cmbCase.Items.Add("Bez zmian");
        cmbCase.Items.Add("Wszystko małe");
        cmbCase.Items.Add("WSZYSTKO DUŻE");
        cmbCase.Items.Add("Jak W Tytule");

        var lblCounter = new TextBlock { Text = "Licznik start / krok:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,4,6,4) };
        var txtCounter = new TextBox { Text = "1", Margin = new Thickness(0,4,0,4) };

        Grid.SetRow(lblMask, 0); Grid.SetColumn(lblMask, 0);
        Grid.SetRow(txtMask, 0); Grid.SetColumn(txtMask, 1);
        Grid.SetRow(lblFind, 0); Grid.SetColumn(lblFind, 2);
        Grid.SetRow(txtFind, 0); Grid.SetColumn(txtFind, 3);

        Grid.SetRow(lblExt, 1); Grid.SetColumn(lblExt, 0);
        Grid.SetRow(txtExt, 1); Grid.SetColumn(txtExt, 1);
        Grid.SetRow(lblReplace, 1); Grid.SetColumn(lblReplace, 2);
        Grid.SetRow(txtReplace, 1); Grid.SetColumn(txtReplace, 3);

        Grid.SetRow(lblCase, 2); Grid.SetColumn(lblCase, 0);
        Grid.SetRow(cmbCase, 2); Grid.SetColumn(cmbCase, 1);
        Grid.SetRow(lblCounter, 2); Grid.SetColumn(lblCounter, 2);
        Grid.SetRow(txtCounter, 2); Grid.SetColumn(txtCounter, 3);

        rulesGrid.Children.Add(lblMask); rulesGrid.Children.Add(txtMask);
        rulesGrid.Children.Add(lblFind); rulesGrid.Children.Add(txtFind);
        rulesGrid.Children.Add(lblExt); rulesGrid.Children.Add(txtExt);
        rulesGrid.Children.Add(lblReplace); rulesGrid.Children.Add(txtReplace);
        rulesGrid.Children.Add(lblCase); rulesGrid.Children.Add(cmbCase);
        rulesGrid.Children.Add(lblCounter); rulesGrid.Children.Add(txtCounter);

        Grid.SetRow(rulesGrid, 0);
        rootGrid.Children.Add(rulesGrid);

        var previewGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            Background = new SolidColorBrush(Color.FromRgb(24, 35, 51)),
            Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)),
            RowBackground = new SolidColorBrush(Color.FromRgb(24, 37, 54)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(28, 43, 61)),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(42, 58, 77))
        };
        previewGrid.Columns.Add(new DataGridTextColumn { Header = "Stara nazwa", Binding = new System.Windows.Data.Binding("OriginalName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        previewGrid.Columns.Add(new DataGridTextColumn { Header = "Nowa nazwa", Binding = new System.Windows.Data.Binding("NewName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        Grid.SetRow(previewGrid, 1);
        rootGrid.Children.Add(previewGrid);

        var bottomBar = new DockPanel { Margin = new Thickness(0,10,0,0) };
        var btnExecute = new Button { Content = "Zastosuj zmiany", MinWidth = 140, Padding = new Thickness(12,6,12,6), FontWeight = FontWeights.Bold };
        var btnCancel = new Button { Content = "Zamknij", MinWidth = 90, Padding = new Thickness(10,6,10,6), Margin = new Thickness(8,0,0,0) };
        var hintLbl = new TextBlock { Text = "Znaczniki: [N] stara nazwa, [E] rozszerzenie, [C] licznik, [YMD] data", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };

        DockPanel.SetDock(btnCancel, Dock.Right);
        DockPanel.SetDock(btnExecute, Dock.Right);
        bottomBar.Children.Add(btnCancel);
        bottomBar.Children.Add(btnExecute);
        bottomBar.Children.Add(hintLbl);

        Grid.SetRow(bottomBar, 2);
        rootGrid.Children.Add(bottomBar);

        List<RenamePreviewItem> previewItems = new();

        Action updatePreview = () =>
        {
            previewItems.Clear();
            int startCounter = int.TryParse(txtCounter.Text.Trim(), out var c) ? c : 1;
            string maskName = txtMask.Text;
            string maskExt = txtExt.Text;
            string findStr = txtFind.Text;
            string repStr = txtReplace.Text;
            int caseOption = cmbCase.SelectedIndex;

            for (int i = 0; i < sel.Length; i++)
            {
                var f = sel[i];
                string origBaseName = f.IsDirectory ? f.Name : Path.GetFileNameWithoutExtension(f.Name);
                string origExt = f.IsDirectory ? "" : Path.GetExtension(f.Name).TrimStart('.');

                string builtName = maskName
                    .Replace("[N]", origBaseName)
                    .Replace("[C]", (startCounter + i).ToString("D2"))
                    .Replace("[YMD]", f.ModifiedDate.ToString("yyyyMMdd"));

                string builtExt = maskExt
                    .Replace("[E]", origExt)
                    .Replace("[YMD]", f.ModifiedDate.ToString("yyyyMMdd"));

                string finalCandidate = f.IsDirectory || string.IsNullOrWhiteSpace(builtExt)
                    ? builtName
                    : $"{builtName}.{builtExt}";

                if (!string.IsNullOrEmpty(findStr))
                    finalCandidate = finalCandidate.Replace(findStr, repStr);

                switch (caseOption)
                {
                    case 1: finalCandidate = finalCandidate.ToLowerInvariant(); break;
                    case 2: finalCandidate = finalCandidate.ToUpperInvariant(); break;
                    case 3: finalCandidate = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(finalCandidate); break;
                }

                previewItems.Add(new RenamePreviewItem
                {
                    OriginalName = f.Name,
                    NewName = finalCandidate,
                    FullPath = f.FullPath,
                    IsDirectory = f.IsDirectory
                });
            }

            previewGrid.ItemsSource = null;
            previewGrid.ItemsSource = previewItems;
        };

        txtMask.TextChanged += (_, _) => updatePreview();
        txtExt.TextChanged += (_, _) => updatePreview();
        txtFind.TextChanged += (_, _) => updatePreview();
        txtReplace.TextChanged += (_, _) => updatePreview();
        txtCounter.TextChanged += (_, _) => updatePreview();
        cmbCase.SelectionChanged += (_, _) => updatePreview();

        updatePreview();

        btnExecute.Click += (_, _) =>
        {
            try
            {
                int changed = 0;
                foreach (var item in previewItems)
                {
                    if (item.OriginalName == item.NewName) continue;
                    string parent = Path.GetDirectoryName(item.FullPath)!;
                    string target = Path.Combine(parent, item.NewName);

                    if (item.IsDirectory)
                        Directory.Move(item.FullPath, target);
                    else
                        File.Move(item.FullPath, target);
                    changed++;
                }

                LoadPanel(_isLeftActive);
                SetStatus($"Zmieniono nazwy {changed} elementów.");
                win.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd zmiany nazwy:\n" + ex.Message, "Multi-Rename");
            }
        };

        btnCancel.Click += (_, _) => win.Close();

        win.Content = rootGrid;
        win.ShowDialog();
    }
    #endregion

    #region Ulubione katalogi (Bookmarks / Ctrl+D)
    private void Bookmarks_Click(object s, RoutedEventArgs e)
    {
        var win = new Window
        {
            Title = "Ulubione katalogi (Ctrl+D) — OGŚ Szperacz",
            Width = 460, Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(16, 23, 34))
        };

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topLbl = new TextBlock
        {
            Text = "Wybierz katalog lub dodaj bieżący folder do ulubionych:",
            Foreground = Brushes.White, Margin = new Thickness(0,0,0,10), TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(topLbl, 0);
        grid.Children.Add(topLbl);

        var list = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 35, 51)),
            Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13
        };
        foreach (var b in _bookmarks)
            list.Items.Add($"{b.Title}  →  {b.Path}");

        Grid.SetRow(list, 1);
        grid.Children.Add(list);

        var btnBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,10,0,0) };
        var btnAddCurrent = new Button { Content = "Dodaj bieżący folder", Margin = new Thickness(0,0,8,0), Padding = new Thickness(8,4,8,4) };
        var btnOpen = new Button { Content = "Otwórz w panelu", Margin = new Thickness(0,0,8,0), Padding = new Thickness(10,4,10,4), FontWeight = FontWeights.Bold };
        var btnClose = new Button { Content = "Zamknij", Padding = new Thickness(8,4,8,4) };

        btnBar.Children.Add(btnAddCurrent);
        btnBar.Children.Add(btnOpen);
        btnBar.Children.Add(btnClose);

        Grid.SetRow(btnBar, 2);
        grid.Children.Add(btnBar);

        btnOpen.Click += (_, _) =>
        {
            if (list.SelectedIndex >= 0 && list.SelectedIndex < _bookmarks.Count)
            {
                Navigate(_isLeftActive, _bookmarks[list.SelectedIndex].Path);
                win.Close();
            }
        };

        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedIndex >= 0 && list.SelectedIndex < _bookmarks.Count)
            {
                Navigate(_isLeftActive, _bookmarks[list.SelectedIndex].Path);
                win.Close();
            }
        };

        btnAddCurrent.Click += (_, _) =>
        {
            string curr = ActivePath;
            string title = Path.GetFileName(curr.TrimEnd('\\'));
            if (string.IsNullOrEmpty(title)) title = curr;

            _bookmarks.Add(new BookmarkItem { Title = title, Path = curr });
            list.Items.Add($"{title}  →  {curr}");
            SetStatus($"Dodano do ulubionych: {title}");
        };

        btnClose.Click += (_, _) => win.Close();

        win.Content = grid;
        win.ShowDialog();
    }
    #endregion

    #region Porównywanie katalogów i sumy kontrolne SHA-256
    private void ComparePanels_Click(object s, RoutedEventArgs e)
    {
        LeftGrid.UnselectAll();
        RightGrid.UnselectAll();

        var leftDict = _leftRawEntries.Where(x => x.Name != "..").ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var rightDict = _rightRawEntries.Where(x => x.Name != "..").ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

        int leftDiff = 0;
        int rightDiff = 0;

        foreach (var leftItem in LeftGrid.Items.OfType<FileEntry>())
        {
            if (leftItem.Name == "..") continue;
            if (!rightDict.TryGetValue(leftItem.Name, out var rightItem) ||
                leftItem.SizeBytes != rightItem.SizeBytes ||
                leftItem.ModifiedDate > rightItem.ModifiedDate)
            {
                LeftGrid.SelectedItems.Add(leftItem);
                leftDiff++;
            }
        }

        foreach (var rightItem in RightGrid.Items.OfType<FileEntry>())
        {
            if (rightItem.Name == "..") continue;
            if (!leftDict.TryGetValue(rightItem.Name, out var leftItem) ||
                rightItem.SizeBytes != leftItem.SizeBytes ||
                rightItem.ModifiedDate > leftItem.ModifiedDate)
            {
                RightGrid.SelectedItems.Add(rightItem);
                rightDiff++;
            }
        }

        SetStatus($"Porównano panele: zaznaczono {leftDiff} unikalnych/nowszych po lewej oraz {rightDiff} po prawej.");
    }

    private async void Checksum_Click(object s, RoutedEventArgs e)
    {
        var item = GetActiveCurrent();
        if (item == null || item.IsDirectory || item.Name == "..")
        {
            SetStatus("Zaznacz plik, aby obliczyć sumę kontrolną.");
            return;
        }

        SetStatus($"Obliczanie SHA-256 dla {item.Name}...");
        SetProgress(true, 50);

        try
        {
            string hash = await Task.Run(() =>
            {
                using var sha = SHA256.Create();
                using var fs = File.OpenRead(item.FullPath);
                byte[] bytes = sha.ComputeHash(fs);
                return Convert.ToHexString(bytes).ToLowerInvariant();
            });

            Clipboard.SetText(hash);
            SetStatus($"SHA-256 skopiowano do schowka: {hash}");
            MessageBox.Show($"Plik: {item.Name}\n\nSHA-256:\n{hash}\n\n(Suma została automatycznie skopiowana do schowka Windows)", "Suma kontrolna SHA-256");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Błąd obliczania sumy:\n" + ex.Message, "Błąd SHA-256");
        }
        finally
        {
            SetProgress(false);
        }
    }
    #endregion

    private static string? AskText(string prompt, string initial)
    {
        var dialog = new Window
        {
            Title = "Szperacz", Width = 400, Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(24, 35, 51))
        };
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = prompt, Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)), Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { Text = initial, Padding = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(16, 23, 34)), Foreground = new SolidColorBrush(Color.FromRgb(237, 243, 248)), BorderBrush = new SolidColorBrush(Color.FromRgb(53, 71, 92)) };
        input.SelectAll();
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 75, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Anuluj", IsCancel = true, MinWidth = 75 };
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        dialog.Content = panel;
        input.Focus();
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
}
