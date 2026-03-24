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
    // ★ _dateSelector を廃止。日付はツリーで管理する。
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

    // ★ 日付ごとにキャッシュ。同じ日付を再展開しても再クエリしない。
    private Dictionary<string, List<LogEntry>> _logCache = new();

    // ★ 現在グリッドに表示中のログ（右ペイン用）
    private List<LogEntry> _currentGridLogs = new();

    // ★ ツリーノードの種別を Tag で識別するためのマーカー
    private const string TAG_DATE_PREFIX   = "DATE:";
    private const string TAG_FOLDER_PREFIX = "FOLDER:";

    public LogViewerForm()
    {
        this.Text = "MBack 履歴復元センター";
        this.Size = new Size(1300, 800);
        this.StartPosition = FormStartPosition.CenterParent;

        _dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MBack", "Database", "history.db");

        SetupLayout();
        InitializeDatabase();
        LoadSettings();

        // ★ 起動時に日付ノードのみをツリーに一括構築
        BuildDateTree();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _splitMain.SplitterDistance = (int)(this.Width * 0.3);
    }

    // ==========================================
    // DB 初期化（変更なし）
    // ==========================================
    private void InitializeDatabase()
    {
        if (!File.Exists(_dbPath)) return;
        try {
            using var conn = new SqliteConnection($"Data Source={_dbPath}"); conn.Open();
            using var cmdIdx = new SqliteCommand(
                "CREATE INDEX IF NOT EXISTS idx_log_entries_time ON LogEntries (Time);", conn);
            cmdIdx.ExecuteNonQuery();
            using var cmdCol = new SqliteCommand(
                "ALTER TABLE LogEntries ADD COLUMN User TEXT;", conn);
            try { cmdCol.ExecuteNonQuery(); } catch { }
        } catch { }
    }

    // ==========================================
    // レイアウト構築
    // ==========================================
    private void SetupLayout()
    {
        // ★ 上部ツールバー：日付ComboBoxを削除し、フィルターと更新ボタンのみに
        var topPanel = new Panel {
            Dock = DockStyle.Top, Height = 60,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(240, 240, 240)
        };

        var lblType = new Label {
            Text = "フィルター:", AutoSize = true,
            Location = new Point(15, 20),
            Font = new Font(this.Font, FontStyle.Bold)
        };
        _typeSelector.Location = new Point(95, 17);
        _typeSelector.Width = 160;
        _typeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _typeSelector.Items.AddRange(new[] {
            "すべて", "Copy (コピー)", "Delete (ゴミ箱)",
            "Error (エラー)", "Convert (HEIC変換)", "Optimize (リサイズ)"
        });
        _typeSelector.SelectedIndex = 0;
        // ★ フィルター変更時は現在選択中のノードで再描画
        _typeSelector.SelectedIndexChanged += (s, e) => RefreshGridFromCurrentNode();

        _btnRefresh.Text = "更新";
        _btnRefresh.Location = new Point(275, 15);
        _btnRefresh.Size = new Size(80, 30);
        // ★ 更新時はキャッシュをクリアしてツリーを再構築
        _btnRefresh.Click += (s, e) => {
            _logCache.Clear();
            BuildDateTree();
        };

        _lblSummary.Location = new Point(375, 20);
        _lblSummary.AutoSize = true;
        _lblSummary.Font = new Font(this.Font, FontStyle.Bold);

        topPanel.Controls.AddRange(new Control[] {
            lblType, _typeSelector, _btnRefresh, _lblSummary
        });

        // ★ SplitContainer（左：ツリー / 右：グリッド）
        _splitMain.Dock = DockStyle.Fill;
        _splitMain.FixedPanel = FixedPanel.Panel1;

        // ★ 左ペイン：TreeView
        _treeFolders.Dock = DockStyle.Fill;
        _treeFolders.ShowLines = true;
        _treeFolders.Font = new Font(this.Font.FontFamily, 10, FontStyle.Regular);
        _treeFolders.BeforeExpand += OnTreeBeforeExpand;
        _treeFolders.AfterSelect  += OnTreeAfterSelect;
        _splitMain.Panel1.Controls.Add(_treeFolders);

        // ★ 右ペイン：DataGridView（仮想モード維持）
        _gridFiles.Dock = DockStyle.Fill;
        _gridFiles.VirtualMode = true;
        _gridFiles.AllowUserToAddRows = false;
        _gridFiles.ReadOnly = true;
        _gridFiles.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridFiles.RowHeadersVisible = false;
        _gridFiles.BackgroundColor = Color.White;

        _gridFiles.Columns.Add("Name",    "ファイル名");  _gridFiles.Columns[0].Width = 350;
        _gridFiles.Columns.Add("Type",    "操作");        _gridFiles.Columns[1].Width = 80;
        _gridFiles.Columns.Add("Time",    "時刻 (回数)"); _gridFiles.Columns[2].Width = 120;
        _gridFiles.Columns.Add("Size",    "サイズ");      _gridFiles.Columns[3].Width = 90;
        _gridFiles.Columns.Add("User",    "ユーザー");    _gridFiles.Columns[4].Width = 120;
        _gridFiles.Columns.Add("Message", "詳細");
        _gridFiles.Columns[5].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        _gridFiles.CellValueNeeded += OnGridCellValueNeeded;
        _gridFiles.RowPrePaint     += OnGridRowPrePaint;
        _splitMain.Panel2.Controls.Add(_gridFiles);

        // ローディングオーバーレイ
        _loadingPanel.BackColor = Color.FromArgb(150, 0, 0, 0);
        _loadingPanel.Visible   = false;
        _loadingPanel.Dock      = DockStyle.Fill;

        _lblLoading.Text      = "ログデータを読み込んでいます...";
        _lblLoading.ForeColor = Color.White;
        _lblLoading.Font      = new Font(this.Font.FontFamily, 14, FontStyle.Bold);
        _lblLoading.AutoSize  = false;
        _lblLoading.TextAlign = ContentAlignment.MiddleCenter;
        _lblLoading.Dock      = DockStyle.Fill;
        _loadingPanel.Controls.Add(_lblLoading);

        var menuVersion = new ToolStripMenuItem(
            "★ 世代選択と復元...", null, OnShowVersionsClick) {
            Font = new Font(this.Font, FontStyle.Bold)
        };
        _contextMenu.Items.Add(menuVersion);
        _gridFiles.ContextMenuStrip = _contextMenu;

        this.Controls.Add(_loadingPanel);
        this.Controls.Add(_splitMain);
        this.Controls.Add(topPanel);
        _loadingPanel.BringToFront();
    }

    // ==========================================
    // ★ 新規：DB から日付一覧を取得してツリーのルートノードを構築
    // ==========================================
    private void BuildDateTree()
    {
        _treeFolders.Nodes.Clear();
        _currentGridLogs.Clear();
        _gridFiles.RowCount = 0;
        _lblSummary.Text = "";

        if (!File.Exists(_dbPath)) return;

        List<string> dates = new();
        try {
            using var conn = new SqliteConnection($"Data Source={_dbPath}"); conn.Open();
            string sql = @"SELECT DISTINCT strftime('%Y-%m-%d', Time) AS LogDate
                           FROM LogEntries
                           ORDER BY LogDate DESC
                           LIMIT 60";
            using var cmd    = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) dates.Add(reader.GetString(0));
        } catch { return; }

        _treeFolders.BeginUpdate();
        foreach (var date in dates) {
            // ★ Tag = "DATE:2025-06-15" のように識別子を付ける
            var node = new TreeNode(date) { Tag = TAG_DATE_PREFIX + date };
            // ★ ダミー子ノードを入れておき、▶ 展開矢印を表示させる
            node.Nodes.Add(new TreeNode("Loading..."));
            _treeFolders.Nodes.Add(node);
        }
        _treeFolders.EndUpdate();
    }

    // ==========================================
    // ★ ツリー展開イベント：日付ノード展開時にフォルダを遅延ロード
    // ==========================================
    private void OnTreeBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node == null) return;

        // ★ 日付ノードの展開（Loading... ダミーが残っている場合のみ処理）
        string tag = e.Node.Tag?.ToString() ?? "";
        if (tag.StartsWith(TAG_DATE_PREFIX) && e.Node.Nodes[0].Text == "Loading...") {
            string dateStr = tag.Substring(TAG_DATE_PREFIX.Length);
            ExpandDateNode(e.Node, dateStr);
            return;
        }

        // ★ フォルダノードの展開（既存ロジックをそのまま流用）
        if (tag.StartsWith(TAG_FOLDER_PREFIX) && e.Node.Nodes[0].Text == "Loading...") {
            string folderPath = tag.Substring(TAG_FOLDER_PREFIX.Length);

            // 親日付ノードを遡って取得
            string dateStr = GetDateFromNode(e.Node);
            if (dateStr == "") return;

            var logs = GetCachedLogs(dateStr);
            ExpandFolderNode(e.Node, folderPath, logs);
        }
    }

    // ★ 日付ノードを展開し、直下のルートフォルダ群を構築
    private void ExpandDateNode(TreeNode dateNode, string dateStr)
    {
        var logs = GetCachedLogs(dateStr);

        _treeFolders.BeginUpdate();
        dateNode.Nodes.Clear();

        var rootDirs = logs
            .Select(l => GetRootPath(l.DirectoryPath))
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        foreach (var root in rootDirs) {
            var node = new TreeNode(root) {
                Tag = TAG_FOLDER_PREFIX + root
            };
            // ★ 子フォルダが存在するか確認してからダミーを追加
            bool hasChildren = logs.Any(l =>
                l.DirectoryPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && l.DirectoryPath.Length > root.Length);
            if (hasChildren) node.Nodes.Add(new TreeNode("Loading..."));
            dateNode.Nodes.Add(node);
        }
        _treeFolders.EndUpdate();
    }

    // ★ フォルダノードを展開し、1段下のサブフォルダを構築
    private void ExpandFolderNode(TreeNode folderNode, string currentPath, List<LogEntry> logs)
    {
        _treeFolders.BeginUpdate();
        folderNode.Nodes.Clear();

        var subDirs = logs
            .Where(l =>
                l.DirectoryPath.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase)
                && l.DirectoryPath.Length > currentPath.Length)
            .Select(l => {
                string rel = l.DirectoryPath.Substring(currentPath.Length)
                               .TrimStart(Path.DirectorySeparatorChar);
                int nextSlash = rel.IndexOf(Path.DirectorySeparatorChar);
                return nextSlash < 0 ? rel : rel.Substring(0, nextSlash);
            })
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        foreach (var sub in subDirs) {
            string fullSubPath = Path.Combine(currentPath, sub);
            var node = new TreeNode(sub) {
                Tag = TAG_FOLDER_PREFIX + fullSubPath
            };
            bool hasChildren = logs.Any(l =>
                l.DirectoryPath.StartsWith(fullSubPath, StringComparison.OrdinalIgnoreCase)
                && l.DirectoryPath.Length > fullSubPath.Length);
            if (hasChildren) node.Nodes.Add(new TreeNode("Loading..."));
            folderNode.Nodes.Add(node);
        }
        _treeFolders.EndUpdate();
    }

    // ==========================================
    // ★ ツリー選択イベント：フォルダノード選択時にグリッドを更新
    // ==========================================
    private void OnTreeAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node == null) return;
        string tag = e.Node.Tag?.ToString() ?? "";

        if (tag.StartsWith(TAG_FOLDER_PREFIX)) {
            // ★ フォルダノードが選択された → 右ペインを更新
            string folderPath = tag.Substring(TAG_FOLDER_PREFIX.Length);
            string dateStr    = GetDateFromNode(e.Node);
            if (dateStr == "") return;

            var logs = GetCachedLogs(dateStr);
            UpdateGrid(logs, folderPath);
        } else {
            // 日付ノードを選択した場合はグリッドをクリア
            _currentGridLogs.Clear();
            _gridFiles.RowCount = 0;
            _lblSummary.Text = "";
        }
    }

    // ★ フィルター変更時：現在選択中のノードで再描画
    private void RefreshGridFromCurrentNode()
    {
        var node = _treeFolders.SelectedNode;
        if (node == null) return;
        string tag = node.Tag?.ToString() ?? "";
        if (!tag.StartsWith(TAG_FOLDER_PREFIX)) return;

        string folderPath = tag.Substring(TAG_FOLDER_PREFIX.Length);
        string dateStr    = GetDateFromNode(node);
        if (dateStr == "") return;

        var logs = GetCachedLogs(dateStr);
        UpdateGrid(logs, folderPath);
    }

    // ★ グリッドを指定フォルダのログで更新
    private void UpdateGrid(List<LogEntry> allLogs, string folderPath)
    {
        string typeFilter = _typeSelector.SelectedItem?.ToString() ?? "すべて";

        _currentGridLogs = allLogs
            .Where(l => l.DirectoryPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase))
            .Where(l => typeFilter == "すべて" || l.Type == ExtractType(typeFilter))
            .ToList();

        _gridFiles.RowCount = _currentGridLogs.Count;
        _gridFiles.Invalidate();

        long sizeTotal = _currentGridLogs.Where(l => l.Type == "Copy").Sum(l => l.Size);
        _lblSummary.Text = $"[対象] {_currentGridLogs.Count}件 (合計: {FormatSize(sizeTotal)})";
    }

    // ==========================================
    // ★ キャッシュ付きログ取得：同じ日付は再クエリしない
    // ==========================================
    private List<LogEntry> GetCachedLogs(string dateStr)
    {
        if (_logCache.TryGetValue(dateStr, out var cached)) return cached;

        var entries = FetchLogsFromDb(dateStr);
        _logCache[dateStr] = entries;
        return entries;
    }

    // ★ DB から指定日付のログを全件取得（typeフィルターなし・キャッシュ用）
    private List<LogEntry> FetchLogsFromDb(string dateStr)
    {
        var entries = new List<LogEntry>();
        string start = $"{dateStr} 00:00:00";
        string end   = $"{dateStr} 23:59:59";

        try {
            using var conn = new SqliteConnection($"Data Source={_dbPath}"); conn.Open();
            string sql = @"
                SELECT Type, Path, MAX(Size), MAX(Message), MAX(User), MAX(Time), COUNT(Id)
                FROM LogEntries
                WHERE Time BETWEEN @start AND @end
                GROUP BY Type, Path
                ORDER BY MAX(Time) ASC";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@start", start);
            cmd.Parameters.AddWithValue("@end",   end);

            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                var entry = new LogEntry {
                    Type        = reader.GetString(0),
                    Path        = reader.GetString(1),
                    Size        = reader.IsDBNull(2) ? 0    : reader.GetInt64(2),
                    Message     = reader.IsDBNull(3) ? ""   : reader.GetString(3),
                    User        = reader.IsDBNull(4) ? "System" : reader.GetString(4),
                    Time        = reader.GetDateTime(5),
                    UpdateCount = reader.GetInt32(6)
                };
                entry.DirectoryPath = Path.GetDirectoryName(entry.Path) ?? "";
                entry.FileName      = Path.GetFileName(entry.Path);
                entries.Add(entry);
            }
        } catch { }

        return entries;
    }

    // ==========================================
    // DataGridView 仮想モード（変更なし）
    // ==========================================
    private void OnGridCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _currentGridLogs.Count) return;
        var log = _currentGridLogs[e.RowIndex];
        switch (e.ColumnIndex) {
            case 0: e.Value = log.FileName; break;
            case 1: e.Value = log.Type;     break;
            case 2: e.Value = log.UpdateCount > 1
                        ? $"{log.Time:HH:mm:ss} ({log.UpdateCount})"
                        : $"{log.Time:HH:mm:ss}"; break;
            case 3: e.Value = FormatSize(log.Size); break;
            case 4: e.Value = log.User;    break;
            case 5: e.Value = log.Message; break;
        }
    }

    private void OnGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _currentGridLogs.Count) return;
        var type = _currentGridLogs[e.RowIndex].Type;
        Color fColor = type switch {
            "Copy"     => Color.DarkBlue,
            "Delete"   => Color.DarkRed,
            "Error"    => Color.Red,
            "Convert"  => Color.Teal,
            "Optimize" => Color.DarkGreen,
            _          => Color.Black
        };
        _gridFiles.Rows[e.RowIndex].DefaultCellStyle.ForeColor = fColor;
    }

    // ==========================================
    // ユーティリティ
    // ==========================================

    // ★ ノードを遡って所属する日付文字列を返す
    private string GetDateFromNode(TreeNode node)
    {
        var current = node;
        while (current != null) {
            string t = current.Tag?.ToString() ?? "";
            if (t.StartsWith(TAG_DATE_PREFIX))
                return t.Substring(TAG_DATE_PREFIX.Length);
            current = current.Parent;
        }
        return "";
    }

    // ★ フィルター文字列から Type 値を抽出 ("Copy (コピー)" → "Copy")
    private string ExtractType(string filter)
    {
        int idx = filter.IndexOf(' ');
        return idx > 0 ? filter.Substring(0, idx) : filter;
    }

    private string GetRootPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "Unknown";
        if (path.StartsWith(@"\\")) {
            var parts = path.Split(
                Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return $@"\\{parts[0]}\{parts[1]}";
        }
        return Path.GetPathRoot(path) ?? path;
    }

    private string FormatSize(long b)
    {
        string[] s = { "B", "KB", "MB", "GB", "TB" };
        double l = b; int i = 0;
        while (l >= 1024 && i < 4) { i++; l /= 1024; }
        return $"{l:0.##} {s[i]}";
    }

    private void LoadSettings()
    {
        try {
            string p = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MBack", "appsettings.json");
            if (File.Exists(p)) {
                var s = JsonSerializer.Deserialize<AppSettingsRaw>(File.ReadAllText(p));
                if (s?.BackupSettings != null) _backupPairs = s.BackupSettings;
            }
        } catch { }
    }

    // ==========================================
    // 復元コンテキストメニュー（変更なし）
    // ==========================================
    private void OnShowVersionsClick(object? sender, EventArgs e)
    {
        if (_gridFiles.SelectedRows.Count == 0) return;
        int rowIndex = _gridFiles.SelectedRows[0].Index;
        if (rowIndex < 0 || rowIndex >= _currentGridLogs.Count) return;

        var log = _currentGridLogs[rowIndex];
        using var restoreForm = new VersionRestoreForm(log.Path, _backupPairs);
        restoreForm.ShowDialog();
    }

    // ==========================================
    // データモデル
    // ==========================================
    private class LogEntry
    {
        public string   Type          { get; set; } = "";
        public string   Path          { get; set; } = "";
        public string   DirectoryPath { get; set; } = "";
        public string   FileName      { get; set; } = "";
        public long     Size          { get; set; }
        public string   Message       { get; set; } = "";
        public string   User          { get; set; } = "";
        public DateTime Time          { get; set; }
        public int      UpdateCount   { get; set; }
    }

    // ==========================================
    // VersionRestoreForm（変更なし）
    // ==========================================
    public class VersionRestoreForm : Form
    {
        private DataGridView _grid   = new();
        private Button _btnOrig  = new();
        private Button _btnOther = new();
        private string _sourcePath;

        public VersionRestoreForm(string sourcePath, List<BackupPair> pairs)
        {
            _sourcePath = sourcePath;
            this.Text = "世代選択と復元";
            this.Size = new Size(750, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Font = new Font("メイリオ", 9);

            var topLbl = new Label {
                Text = $"復元対象:\n{sourcePath}",
                Dock = DockStyle.Top,
                Padding = new Padding(10),
                AutoSize = true,
                Font = new Font("メイリオ", 9, FontStyle.Bold)
            };

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.ReadOnly = true;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.RowHeadersVisible = false;
            _grid.BackgroundColor = Color.WhiteSmoke;
            _grid.Columns.Add("Type", "状態 / 世代");        _grid.Columns[0].Width = 160;
            _grid.Columns.Add("Date", "バックアップ日時");  _grid.Columns[1].Width = 160;
            _grid.Columns.Add("Size", "サイズ");             _grid.Columns[2].Width = 100;
            _grid.Columns.Add("Path", "物理パス (隠し)");    _grid.Columns[3].Visible = false;

            var bottomPanel = new FlowLayoutPanel {
                Dock = DockStyle.Bottom, Height = 60,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            _btnOther.Text = "📁 別の場所に復元して確認"; _btnOther.Width = 220; _btnOther.Height = 35;
            _btnOrig.Text  = "⚠️ 元の場所に上書き復元";   _btnOrig.Width  = 200; _btnOrig.Height  = 35;
            _btnOrig.BackColor = Color.MistyRose;

            _btnOther.Click += OnOtherClick;
            _btnOrig.Click  += OnOrigClick;

            bottomPanel.Controls.Add(_btnOther);
            bottomPanel.Controls.Add(_btnOrig);

            this.Controls.Add(_grid);
            this.Controls.Add(topLbl);
            this.Controls.Add(bottomPanel);

            LoadVersions(pairs);
        }

        private void LoadVersions(List<BackupPair> pairs)
        {
            var pair = pairs.FirstOrDefault(p =>
                _sourcePath.StartsWith(p.Source, StringComparison.OrdinalIgnoreCase));
            if (pair == null) return;

            string relPath    = Path.GetRelativePath(pair.Source, _sourcePath);
            string normalPath = Path.Combine(pair.Destination, relPath);
            string trashPath  = Path.Combine(pair.Destination, "_TRASH_", relPath);

            AddIfExist("現在のバックアップ (最新)", normalPath, Color.Black);
            for (int i = 1; i <= 50; i++)
                AddIfExist($"{i} 世代前", $"{normalPath}.v{i}", Color.Black);

            AddIfExist("🗑️ ゴミ箱 (最新)", trashPath, Color.DarkRed);
            for (int i = 1; i <= 50; i++)
                AddIfExist($"🗑️ ゴミ箱 ({i} 世代前)", $"{trashPath}.v{i}", Color.DarkRed);

            if (_grid.RowCount == 0) {
                MessageBox.Show(
                    "NAS上にバックアップデータが見つかりませんでした。\n" +
                    "（既に削除されたか、NASに接続できていない可能性があります）",
                    "情報", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AddIfExist(string label, string path, Color textColor)
        {
            if (File.Exists(path)) {
                var fi = new FileInfo(path);
                int rowIndex = _grid.Rows.Add(
                    label,
                    fi.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"),
                    FormatSize(fi.Length),
                    path);
                _grid.Rows[rowIndex].DefaultCellStyle.ForeColor = textColor;
            }
        }

        private string FormatSize(long b)
        {
            string[] s = { "B", "KB", "MB", "GB", "TB" };
            double l = b; int i = 0;
            while (l >= 1024 && i < 4) { i++; l /= 1024; }
            return $"{l:0.##} {s[i]}";
        }

        private void OnOrigClick(object? sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0) return;
            string target = _grid.SelectedRows[0].Cells[3].Value?.ToString() ?? "";

            if (MessageBox.Show(
                    $"本当に以下の場所に上書き復元しますか？\n（現在のファイルは失われます）\n\n{_sourcePath}",
                    "最終確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                try {
                    string dir = Path.GetDirectoryName(_sourcePath) ?? "";
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.Copy(target, _sourcePath, true);
                    MessageBox.Show("元の場所に復元しました。", "完了",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.Close();
                } catch (Exception ex) {
                    MessageBox.Show("復元失敗: " + ex.Message);
                }
            }
        }

        private void OnOtherClick(object? sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0) return;
            string target = _grid.SelectedRows[0].Cells[3].Value?.ToString() ?? "";

            using var dlg = new FolderBrowserDialog {
                Description = "復元先のフォルダを選択してください"
            };
            if (dlg.ShowDialog() == DialogResult.OK) {
                try {
                    string dest = Path.Combine(dlg.SelectedPath, Path.GetFileName(_sourcePath));
                    if (File.Exists(dest) &&
                        MessageBox.Show("指定先に同名ファイルがあります。上書きしますか？",
                            "確認", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

                    File.Copy(target, dest, true);
                    MessageBox.Show(
                        "指定したフォルダに復元しました。\nフォルダを開いて確認します。",
                        "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Process.Start("explorer.exe", dlg.SelectedPath);
                    this.Close();
                } catch (Exception ex) {
                    MessageBox.Show("復元失敗: " + ex.Message);
                }
            }
        }
    }
}
