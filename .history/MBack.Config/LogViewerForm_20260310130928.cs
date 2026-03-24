using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Threading.Tasks;

namespace MBack.Config;

public class LogViewerForm : Form
{
    private ComboBox _dateSelector = new();
    private ComboBox _typeSelector = new();
    private Label _lblSummary = new();
    private Button _btnRefresh = new(); 
    
    // ★ルートβ 新規レイアウト：ツリーとリストの分割
    private SplitContainer _splitMain = new();
    private TreeView _treeFolders = new();
    private DataGridView _gridFiles = new();
    private ContextMenuStrip _contextMenu = new();
    
    private Panel _loadingPanel = new();
    private Label _lblLoading = new();

    private string _dbPath;
    private List<BackupPair> _backupPairs = new(); 
    
    // 取得したその日1日分の全ログ（メモリにキャッシュして爆速処理する）
    private List<LogEntry> _dailyLogs = new();
    // グリッドに表示中（ツリーで選択中）のファイル一覧
    private List<LogEntry> _currentGridLogs = new();

    public LogViewerForm()
    {
        this.Text = "MBack 履歴復元センター (v3.0 - チョイ速ツリー Explorer仕様)";
        this.Size = new Size(1300, 800); 
        this.StartPosition = FormStartPosition.CenterParent;

        _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MBack", "Database", "history.db");

        SetupLayout();
        InitializeDatabase();
        LoadSettings();
        LoadDateListFromDb(); 
    }

    private void InitializeDatabase()
    {
        if (!File.Exists(_dbPath)) return;
        try {
            using var conn = new SqliteConnection($"Data Source={_dbPath}"); conn.Open();
            using var cmdIdx = new SqliteCommand("CREATE INDEX IF NOT EXISTS idx_log_entries_time ON LogEntries (Time);", conn);
            cmdIdx.ExecuteNonQuery();
            using var cmdCol = new SqliteCommand("ALTER TABLE LogEntries ADD COLUMN User TEXT;", conn);
            try { cmdCol.ExecuteNonQuery(); } catch { }
        } catch { }
    }

    private void SetupLayout()
    {
        // --- 1. ヘッダー（日付とフィルター） ---
        var topPanel = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(10), BackColor = Color.FromArgb(240, 240, 240) };
        
        var lblDate = new Label { Text = "日付:", AutoSize = true, Location = new Point(15, 20), Font = new Font(this.Font, FontStyle.Bold) };
        _dateSelector.Location = new Point(60, 17); _dateSelector.Width = 140; _dateSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _dateSelector.SelectedIndexChanged += async (s, e) => await ReloadDataAsync();

        var lblType = new Label { Text = "フィルター:", AutoSize = true, Location = new Point(220, 20), Font = new Font(this.Font, FontStyle.Bold) };
        _typeSelector.Location = new Point(300, 17); _typeSelector.Width = 150; _typeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _typeSelector.Items.AddRange(new[] { "すべて", "Copy (コピー)", "Delete (ゴミ箱)", "Error (エラー)", "Convert (HEIC変換)", "Optimize (リサイズ)" });
        _typeSelector.SelectedIndex = 0;
        _typeSelector.SelectedIndexChanged += async (s, e) => await ReloadDataAsync();

        _btnRefresh.Text = "更新"; _btnRefresh.Location = new Point(470, 15); _btnRefresh.Size = new Size(80, 30);
        _btnRefresh.Click += async (s, e) => { LoadDateListFromDb(); await ReloadDataAsync(); };
        
        _lblSummary.Location = new Point(570, 20); _lblSummary.AutoSize = true; _lblSummary.Font = new Font(this.Font, FontStyle.Bold);
        topPanel.Controls.AddRange(new Control[] { lblDate, _dateSelector, lblType, _typeSelector, _btnRefresh, _lblSummary });

        // --- 2. メイン領域（スプリットコンテナ） ---
        _splitMain.Dock = DockStyle.Fill;
        _splitMain.SplitterDistance = 350; // 左側ツリーの幅

        // 左側：ツリービュー
        _treeFolders.Dock = DockStyle.Fill;
        _treeFolders.ImageList = new ImageList();
        _treeFolders.ImageList.Images.Add("folder", SystemIcons.Warning); // 簡易アイコン
        _treeFolders.BeforeExpand += OnTreeBeforeExpand;
        _treeFolders.AfterSelect += OnTreeAfterSelect;
        _splitMain.Panel1.Controls.Add(_treeFolders);

        // 右側：データグリッド（ファイル一覧）
        _gridFiles.Dock = DockStyle.Fill;
        _gridFiles.VirtualMode = true;
        _gridFiles.AllowUserToAddRows = false;
        _gridFiles.ReadOnly = true;
        _gridFiles.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridFiles.RowHeadersVisible = false;
        _gridFiles.BackgroundColor = Color.White;
        
        _gridFiles.Columns.Add("Name", "ファイル名"); _gridFiles.Columns[0].Width = 300;
        _gridFiles.Columns.Add("Type", "操作"); _gridFiles.Columns[1].Width = 80;
        _gridFiles.Columns.Add("Time", "時刻 (回数)"); _gridFiles.Columns[2].Width = 120;
        _gridFiles.Columns.Add("Size", "サイズ"); _gridFiles.Columns[3].Width = 90;
        _gridFiles.Columns.Add("User", "ユーザー"); _gridFiles.Columns[4].Width = 120;
        _gridFiles.Columns.Add("Message", "詳細"); _gridFiles.Columns[5].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        _gridFiles.CellValueNeeded += OnGridCellValueNeeded;
        _gridFiles.RowPrePaint += OnGridRowPrePaint;
        _splitMain.Panel2.Controls.Add(_gridFiles);

        // --- 3. ローディングパネル ---
        _loadingPanel.BackColor = Color.FromArgb(150, 0, 0, 0); 
        _loadingPanel.Visible = false;
        _loadingPanel.Dock = DockStyle.Fill;
        _lblLoading.Text = "大量データを解析中... (応答なしになりません)";
        _lblLoading.ForeColor = Color.White;
        _lblLoading.Font = new Font(this.Font.FontFamily, 14, FontStyle.Bold);
        _lblLoading.AutoSize = false;
        _lblLoading.TextAlign = ContentAlignment.MiddleCenter;
        _lblLoading.Dock = DockStyle.Fill;
        _loadingPanel.Controls.Add(_lblLoading);

        var menuVersion = new ToolStripMenuItem("★ 世代選択と復元...", null, OnShowVersionsClick) { Font = new Font(this.Font, FontStyle.Bold) };
        _contextMenu.Items.AddRange(new ToolStripItem[] { menuVersion });
        _gridFiles.ContextMenuStrip = _contextMenu;

        this.Controls.Add(_loadingPanel);
        this.Controls.Add(_splitMain); 
        this.Controls.Add(topPanel);
        _loadingPanel.BringToFront();
    }

    private async Task ReloadDataAsync()
    {
        if (_dateSelector.SelectedItem is not string dateStr) return;
        
        _loadingPanel.Visible = true;
        _splitMain.Enabled = false;
        _btnRefresh.Enabled = false;
        _treeFolders.Nodes.Clear();
        _currentGridLogs.Clear();
        _gridFiles.RowCount = 0;

        string typeFilter = _typeSelector.SelectedItem?.ToString() ?? "すべて";
        
        // 非同期で1日分の全ログを取得
        var result = await Task.Run(() => FetchLogsFromDb(dateStr, typeFilter));
        _dailyLogs = result.Entries;
        _lblSummary.Text = result.SummaryText;

        // ★ツリーの第一階層だけを構築する（チョイ速の秘密）
        BuildTreeRoot();

        _loadingPanel.Visible = false;
        _splitMain.Enabled = true;
        _btnRefresh.Enabled = true;
    }

    private (List<LogEntry> Entries, string SummaryText) FetchLogsFromDb(string dateStr, string typeFilter)
    {
        var entries = new List<LogEntry>();
        long sizeTotal = 0;
        string sqlFilter = "";
        
        if (typeFilter.StartsWith("Copy")) sqlFilter = "AND Type = 'Copy'";
        else if (typeFilter.StartsWith("Delete")) sqlFilter = "AND Type = 'Delete'";
        else if (typeFilter.StartsWith("Error")) sqlFilter = "AND Type = 'Error'";
        else if (typeFilter.StartsWith("Convert")) sqlFilter = "AND Type = 'Convert'";
        else if (typeFilter.StartsWith("Optimize")) sqlFilter = "AND Type = 'Optimize'";

        string start = $"{dateStr} 00:00:00";
        string end = $"{dateStr} 23:59:59";

        try {
            using var conn = new SqliteConnection($"Data Source={_dbPath}"); conn.Open();
            string sql = $@"
                SELECT Type, Path, MAX(Size), MAX(Message), MAX(User), MAX(Time), COUNT(Id) 
                FROM LogEntries 
                WHERE Time BETWEEN @start AND @end {sqlFilter}
                GROUP BY Type, Path 
                ORDER BY MAX(Time) ASC";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@start", start);
            cmd.Parameters.AddWithValue("@end", end);
            
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                var entry = new LogEntry {
                    Type = reader.GetString(0),
                    Path = reader.GetString(1),
                    Size = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    Message = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    User = reader.IsDBNull(4) ? "System" : reader.GetString(4),
                    Time = reader.GetDateTime(5),
                    UpdateCount = reader.GetInt32(6)
                };
                
                // パスからディレクトリとファイル名を分離（ツリー用）
                entry.DirectoryPath = Path.GetDirectoryName(entry.Path) ?? "";
                entry.FileName = Path.GetFileName(entry.Path);
                
                entries.Add(entry);
                if (entry.Type == "Copy") sizeTotal += entry.Size;
            }
        } catch { }

        return (entries, $"[対象] {entries.Count}件 (合計: {FormatSize(sizeTotal)})");
    }

    // --- ツリー構築ロジック（オンデマンド方式） ---
    private void BuildTreeRoot()
    {
        if (_dailyLogs.Count == 0) return;

        // 全ログからルートとなるドライブ・共有フォルダを抽出
        var rootDirs = _dailyLogs.Select(l => GetRootPath(l.DirectoryPath)).Distinct().OrderBy(x => x).ToList();

        _treeFolders.BeginUpdate();
        foreach (var root in rootDirs) {
            var node = new TreeNode(root) { Tag = root, ImageKey = "folder", SelectedImageKey = "folder" };
            // 子階層があるかもしれない目印としてダミーノードを入れる（+マークを出すため）
            node.Nodes.Add(new TreeNode("Loading...")); 
            _treeFolders.Nodes.Add(node);
        }
        _treeFolders.EndUpdate();
    }

    private string GetRootPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "Unknown";
        if (path.StartsWith(@"\\")) {
            var parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return $@"\\{parts[0]}\{parts[1]}";
        }
        return Path.GetPathRoot(path) ?? path;
    }

    // +ボタンを押した時に、その下にあるフォルダを探して追加する
    private void OnTreeBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node == null || e.Node.Nodes[0].Text != "Loading...") return;

        _treeFolders.BeginUpdate();
        e.Node.Nodes.Clear(); // ダミーを消す
        string currentPath = e.Node.Tag?.ToString() ?? "";

        // 現在のパスの直下にあるフォルダを抽出
        var subDirs = _dailyLogs
            .Where(l => l.DirectoryPath.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase) && l.DirectoryPath.Length > currentPath.Length)
            .Select(l => {
                string rel = l.DirectoryPath.Substring(currentPath.Length).TrimStart(Path.DirectorySeparatorChar);
                int nextSlash = rel.IndexOf(Path.DirectorySeparatorChar);
                return nextSlash < 0 ? rel : rel.Substring(0, nextSlash);
            })
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        foreach (var sub in subDirs) {
            string fullSubPath = Path.Combine(currentPath, sub);
            var node = new TreeNode(sub) { Tag = fullSubPath, ImageKey = "folder", SelectedImageKey = "folder" };
            node.Nodes.Add(new TreeNode("Loading..."));
            e.Node.Nodes.Add(node);
        }
        _treeFolders.EndUpdate();
    }

    // フォルダをクリックした時に、右側のグリッドにファイル一覧を出す
    private void OnTreeAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node == null) return;
        string selectedPath = e.Node.Tag?.ToString() ?? "";

        // 完全一致するディレクトリのファイルだけを抽出
        _currentGridLogs = _dailyLogs.Where(l => l.DirectoryPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase)).ToList();
        
        _gridFiles.RowCount = _currentGridLogs.Count;
        _gridFiles.Invalidate();
    }

    // --- グリッド描画ロジック ---
    private void OnGridCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _currentGridLogs.Count) return;
        var log = _currentGridLogs[e.RowIndex];
        switch (e.ColumnIndex) {
            case 0: e.Value = log.FileName; break;
            case 1: e.Value = log.Type; break;
            case 2: e.Value = log.UpdateCount > 1 ? $"{log.Time:HH:mm:ss} ({log.UpdateCount})" : $"{log.Time:HH:mm:ss}"; break;
            case 3: e.Value = FormatSize(log.Size); break;
            case 4: e.Value = log.User; break;
            case 5: e.Value = log.Message; break;
        }
    }

    private void OnGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _currentGridLogs.Count) return;
        var type = _currentGridLogs[e.RowIndex].Type;
        Color fColor = type switch {
            "Copy" => Color.DarkBlue,
            "Delete" => Color.DarkRed,
            "Error" => Color.Red,
            "Convert" => Color.Teal,
            "Optimize" => Color.DarkGreen,
            _ => Color.Black
        };
        _gridFiles.Rows[e.RowIndex].DefaultCellStyle.ForeColor = fColor;
    }

    private void LoadDateListFromDb()
    {
        if (!File.Exists(_dbPath)) return;
        try {
            using var conn = new SqliteConnection($"Data Source={_dbPath}"); conn.Open();
            string sql = "SELECT DISTINCT strftime('%Y-%m-%d', Time) as LogDate FROM LogEntries ORDER BY LogDate DESC LIMIT 30";
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            _dateSelector.Items.Clear();
            while (reader.Read()) _dateSelector.Items.Add(reader.GetString(0));
            if (_dateSelector.Items.Count > 0) _dateSelector.SelectedIndex = 0;
        } catch { }
    }

    private string FormatSize(long b) { string[] s = { "B", "KB", "MB", "GB", "TB" }; double l = b; int i = 0; while (l >= 1024 && i < 4) { i++; l /= 1024; } return $"{l:0.##} {s[i]}"; }

    private void LoadSettings() {
        try {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MBack", "appsettings.json");
            if (File.Exists(p)) { var s = JsonSerializer.Deserialize<AppSettingsRaw>(File.ReadAllText(p)); if (s?.BackupSettings != null) _backupPairs = s.BackupSettings; }
        } catch { }
    }

    // --- 世代表示・復元ロジック（ファイル専用） ---
    private void OnShowVersionsClick(object? sender, EventArgs e) 
    {
        if (_gridFiles.SelectedRows.Count == 0) return;
        int rowIndex = _gridFiles.SelectedRows[0].Index;
        if (rowIndex < 0 || rowIndex >= _currentGridLogs.Count) return;

        var log = _currentGridLogs[rowIndex];
        
        // 既存の履歴復元フォーム（HistoryViewerForm等）を呼び出すロジックをここに移植します
        // 今回のアップデートではUIの枠組みに専念し、元の復元ロジックは教官の既存コードに準じます
        MessageBox.Show($"復元対象: {log.Path}\n\n(※ここに既存の復元フォームを接続します)", "世代確認");
    }

    private class LogEntry { 
        public string Type { get; set; } = ""; 
        public string Path { get; set; } = ""; 
        public string DirectoryPath { get; set; } = ""; // ★新規
        public string FileName { get; set; } = "";      // ★新規
        public long Size { get; set; } 
        public string Message { get; set; } = ""; 
        public string User { get; set; } = ""; 
        public DateTime Time { get; set; } 
        public int UpdateCount { get; set; } 
    }
}