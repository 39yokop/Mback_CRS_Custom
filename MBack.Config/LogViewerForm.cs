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
    
    private SplitContainer _splitMain = new();
    private TreeView _treeFolders = new();
    private DataGridView _gridFiles = new();
    private ContextMenuStrip _contextMenu = new();
    
    private Panel _loadingPanel = new();
    private Label _lblLoading = new();

    private string _dbPath;
    private List<BackupPair> _backupPairs = new(); 
    
    private List<LogEntry> _dailyLogs = new();
    private List<LogEntry> _currentGridLogs = new();

    public LogViewerForm()
    {
        // ★修正1：プロフェッショナルなタイトル
        this.Text = "MBack 履歴復元センター"; 
        this.Size = new Size(1300, 800); 
        this.StartPosition = FormStartPosition.CenterParent;

        _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MBack", "Database", "history.db");

        SetupLayout();
        InitializeDatabase();
        LoadSettings();
        LoadDateListFromDb(); 
    }

    // ★修正2：画面表示時にツリーとリストの比率を 3:7 (30%) に強制固定
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _splitMain.SplitterDistance = (int)(this.Width * 0.3);
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

        _splitMain.Dock = DockStyle.Fill;
        _splitMain.FixedPanel = FixedPanel.Panel1;

        _treeFolders.Dock = DockStyle.Fill;
        _treeFolders.ShowLines = true; // ★修正3：警告アイコンを撤去し、綺麗な階層線に変更
        _treeFolders.Font = new Font(this.Font.FontFamily, 10, FontStyle.Regular);
        _treeFolders.BeforeExpand += OnTreeBeforeExpand;
        _treeFolders.AfterSelect += OnTreeAfterSelect;
        _splitMain.Panel1.Controls.Add(_treeFolders);

        _gridFiles.Dock = DockStyle.Fill;
        _gridFiles.VirtualMode = true;
        _gridFiles.AllowUserToAddRows = false;
        _gridFiles.ReadOnly = true;
        _gridFiles.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridFiles.RowHeadersVisible = false;
        _gridFiles.BackgroundColor = Color.White;
        
        _gridFiles.Columns.Add("Name", "ファイル名"); _gridFiles.Columns[0].Width = 350;
        _gridFiles.Columns.Add("Type", "操作"); _gridFiles.Columns[1].Width = 80;
        _gridFiles.Columns.Add("Time", "時刻 (回数)"); _gridFiles.Columns[2].Width = 120;
        _gridFiles.Columns.Add("Size", "サイズ"); _gridFiles.Columns[3].Width = 90;
        _gridFiles.Columns.Add("User", "ユーザー"); _gridFiles.Columns[4].Width = 120;
        _gridFiles.Columns.Add("Message", "詳細"); _gridFiles.Columns[5].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        _gridFiles.CellValueNeeded += OnGridCellValueNeeded;
        _gridFiles.RowPrePaint += OnGridRowPrePaint;
        _splitMain.Panel2.Controls.Add(_gridFiles);

        _loadingPanel.BackColor = Color.FromArgb(150, 0, 0, 0); 
        _loadingPanel.Visible = false;
        _loadingPanel.Dock = DockStyle.Fill;
        
        // ★修正4：まともなローディングメッセージ
        _lblLoading.Text = "ログデータを読み込んでいます..."; 
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
        
        var result = await Task.Run(() => FetchLogsFromDb(dateStr, typeFilter));
        _dailyLogs = result.Entries;
        _lblSummary.Text = result.SummaryText;

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
                
                entry.DirectoryPath = Path.GetDirectoryName(entry.Path) ?? "";
                entry.FileName = Path.GetFileName(entry.Path);
                
                entries.Add(entry);
                if (entry.Type == "Copy") sizeTotal += entry.Size;
            }
        } catch { }

        return (entries, $"[対象] {entries.Count}件 (合計: {FormatSize(sizeTotal)})");
    }

    private void BuildTreeRoot()
    {
        if (_dailyLogs.Count == 0) return;

        var rootDirs = _dailyLogs.Select(l => GetRootPath(l.DirectoryPath)).Distinct().OrderBy(x => x).ToList();

        _treeFolders.BeginUpdate();
        foreach (var root in rootDirs) {
            var node = new TreeNode(root) { Tag = root };
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

    private void OnTreeBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node == null || e.Node.Nodes[0].Text != "Loading...") return;

        _treeFolders.BeginUpdate();
        e.Node.Nodes.Clear(); 
        string currentPath = e.Node.Tag?.ToString() ?? "";

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
            var node = new TreeNode(sub) { Tag = fullSubPath };
            node.Nodes.Add(new TreeNode("Loading..."));
            e.Node.Nodes.Add(node);
        }
        _treeFolders.EndUpdate();
    }

    private void OnTreeAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node == null) return;
        string selectedPath = e.Node.Tag?.ToString() ?? "";

        _currentGridLogs = _dailyLogs.Where(l => l.DirectoryPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase)).ToList();
        
        _gridFiles.RowCount = _currentGridLogs.Count;
        _gridFiles.Invalidate();
    }

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

    // ★復元サブフォームの呼び出し
    private void OnShowVersionsClick(object? sender, EventArgs e) 
    {
        if (_gridFiles.SelectedRows.Count == 0) return;
        int rowIndex = _gridFiles.SelectedRows[0].Index;
        if (rowIndex < 0 || rowIndex >= _currentGridLogs.Count) return;

        var log = _currentGridLogs[rowIndex];
        
        using var restoreForm = new VersionRestoreForm(log.Path, _backupPairs);
        restoreForm.ShowDialog();
    }

    private class LogEntry { 
        public string Type { get; set; } = ""; 
        public string Path { get; set; } = ""; 
        public string DirectoryPath { get; set; } = ""; 
        public string FileName { get; set; } = "";      
        public long Size { get; set; } 
        public string Message { get; set; } = ""; 
        public string User { get; set; } = ""; 
        public DateTime Time { get; set; } 
        public int UpdateCount { get; set; } 
    }

    // ==========================================
    // ★完全新規実装：復元センターサブフォーム（プランB）
    // ==========================================
    public class VersionRestoreForm : Form
    {
        private DataGridView _grid = new();
        private Button _btnOrig = new();
        private Button _btnOther = new();
        private string _sourcePath;

        public VersionRestoreForm(string sourcePath, List<BackupPair> pairs)
        {
            _sourcePath = sourcePath;
            this.Text = "世代選択と復元";
            this.Size = new Size(750, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Font = new Font("メイリオ", 9);

            var topLbl = new Label { Text = $"復元対象:\n{sourcePath}", Dock = DockStyle.Top, Padding = new Padding(10), AutoSize = true, Font = new Font("メイリオ", 9, FontStyle.Bold) };
            
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.ReadOnly = true;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.RowHeadersVisible = false;
            _grid.BackgroundColor = Color.WhiteSmoke;
            _grid.Columns.Add("Type", "状態 / 世代"); _grid.Columns[0].Width = 160;
            _grid.Columns.Add("Date", "バックアップ日時"); _grid.Columns[1].Width = 160;
            _grid.Columns.Add("Size", "サイズ"); _grid.Columns[2].Width = 100;
            _grid.Columns.Add("Path", "物理パス (隠し)"); _grid.Columns[3].Visible = false;

            var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            
            _btnOther.Text = "📁 別の場所に復元して確認"; _btnOther.Width = 220; _btnOther.Height = 35;
            _btnOrig.Text = "⚠️ 元の場所に上書き復元"; _btnOrig.Width = 200; _btnOrig.Height = 35; _btnOrig.BackColor = Color.MistyRose;

            _btnOther.Click += OnOtherClick;
            _btnOrig.Click += OnOrigClick;

            bottomPanel.Controls.Add(_btnOther);
            bottomPanel.Controls.Add(_btnOrig);

            this.Controls.Add(_grid);
            this.Controls.Add(topLbl);
            this.Controls.Add(bottomPanel);

            LoadVersions(pairs);
        }

        private void LoadVersions(List<BackupPair> pairs)
        {
            // パスから対応するバックアップ設定を探す
            var pair = pairs.FirstOrDefault(p => _sourcePath.StartsWith(p.Source, StringComparison.OrdinalIgnoreCase));
            if (pair == null) return;

            string relPath = Path.GetRelativePath(pair.Source, _sourcePath);
            string normalPath = Path.Combine(pair.Destination, relPath);
            string trashPath = Path.Combine(pair.Destination, "_TRASH_", relPath);

            // 通常のバックアップ領域を検索
            AddIfExist("現在のバックアップ (最新)", normalPath, Color.Black);
            for(int i = 1; i <= 50; i++) AddIfExist($"{i} 世代前", $"{normalPath}.v{i}", Color.Black);

            // ゴミ箱領域も検索（Deleteされたファイル用）
            AddIfExist("🗑️ ゴミ箱 (最新)", trashPath, Color.DarkRed);
            for(int i = 1; i <= 50; i++) AddIfExist($"🗑️ ゴミ箱 ({i} 世代前)", $"{trashPath}.v{i}", Color.DarkRed);

            if (_grid.RowCount == 0) {
                MessageBox.Show("NAS上にバックアップデータが見つかりませんでした。\n（既に削除されたか、NASに接続できていない可能性があります）", "情報", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AddIfExist(string label, string path, Color textColor)
        {
            if (File.Exists(path)) {
                var fi = new FileInfo(path);
                int rowIndex = _grid.Rows.Add(label, fi.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"), FormatSize(fi.Length), path);
                _grid.Rows[rowIndex].DefaultCellStyle.ForeColor = textColor;
            }
        }

        private string FormatSize(long b) { string[] s = { "B", "KB", "MB", "GB", "TB" }; double l = b; int i = 0; while (l >= 1024 && i < 4) { i++; l /= 1024; } return $"{l:0.##} {s[i]}"; }

        // ★ボタン1：元の場所に復元（上書き）
        private void OnOrigClick(object? sender, EventArgs e) {
            if (_grid.SelectedRows.Count == 0) return;
            // ↓ Valueの直後に「?」を追加して安全に（Null条件演算子）
            string target = _grid.SelectedRows[0].Cells[3].Value?.ToString() ?? "";
            
            if (MessageBox.Show($"本当に以下の場所に上書き復元しますか？\n（現在のファイルは失われます）\n\n{_sourcePath}", "最終確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                try {
                    string dir = Path.GetDirectoryName(_sourcePath) ?? "";
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.Copy(target, _sourcePath, true);
                    MessageBox.Show("元の場所に復元しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.Close();
                } catch (Exception ex) { MessageBox.Show("復元失敗: " + ex.Message); }
            }
        }

        // ★ボタン2：別の場所に復元して確認
        private void OnOtherClick(object? sender, EventArgs e) {
            if (_grid.SelectedRows.Count == 0) return;
            // ↓ こちらも同様に「?」を追加
            string target = _grid.SelectedRows[0].Cells[3].Value?.ToString() ?? "";

            using var dlg = new FolderBrowserDialog { Description = "復元先のフォルダを選択してください" };
            if (dlg.ShowDialog() == DialogResult.OK) {
                try {
                    string dest = Path.Combine(dlg.SelectedPath, Path.GetFileName(_sourcePath));
                    if (File.Exists(dest) && MessageBox.Show("指定先に同名ファイルがあります。上書きしますか？", "確認", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    
                    File.Copy(target, dest, true);
                    MessageBox.Show("指定したフォルダに復元しました。\nフォルダを開いて確認します。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    
                    Process.Start("explorer.exe", dlg.SelectedPath);
                    this.Close();
                } catch (Exception ex) { MessageBox.Show("復元失敗: " + ex.Message); }
            }
        }
    }
}