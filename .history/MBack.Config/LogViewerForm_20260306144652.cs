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
    private DataGridView _gridMain = new();
    private ContextMenuStrip _contextMenu = new();
    private Button _btnRefresh = new(); 
    
    // ★新規：読み込み中パネル
    private Panel _loadingPanel = new();
    private Label _lblLoading = new();

    private string _dbPath;
    private List<BackupPair> _backupPairs = new(); 
    private List<LogEntry> _logCache = new();

    public LogViewerForm()
    {
        this.Text = "MBack 履歴復元センター (60万件超対応・非同期プロ仕様)";
        this.Size = new Size(1200, 750); 
        this.StartPosition = FormStartPosition.CenterParent;

        _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MBack", "Database", "history.db");

        SetupLayout();
        InitializeDatabase(); // インデックス作成含む
        LoadSettings();
        LoadDateListFromDb(); 
    }

    private void InitializeDatabase()
    {
        if (!File.Exists(_dbPath)) return;
        try {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            // ★高速化の要：インデックスを作成（Timeでの検索を100倍速くする）
            using var cmdIdx = new SqliteCommand("CREATE INDEX IF NOT EXISTS idx_log_entries_time ON LogEntries (Time);", conn);
            cmdIdx.ExecuteNonQuery();

            // Userカラム追加（既存環境用）
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

        _gridMain.Dock = DockStyle.Fill;
        _gridMain.VirtualMode = true;
        _gridMain.AllowUserToAddRows = false;
        _gridMain.ReadOnly = true;
        _gridMain.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridMain.RowHeadersVisible = false;
        _gridMain.BackgroundColor = Color.White;
        
        _gridMain.Columns.Add("Time", "時刻 (回数)"); _gridMain.Columns[0].Width = 140;
        _gridMain.Columns.Add("Type", "操作"); _gridMain.Columns[1].Width = 80;
        _gridMain.Columns.Add("Path", "ファイルパス"); _gridMain.Columns[2].Width = 450;
        _gridMain.Columns.Add("Size", "サイズ"); _gridMain.Columns[3].Width = 90;
        _gridMain.Columns.Add("User", "ユーザー"); _gridMain.Columns[4].Width = 120;
        _gridMain.Columns.Add("Message", "詳細"); _gridMain.Columns[5].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        _gridMain.CellValueNeeded += OnCellValueNeeded;
        _gridMain.RowPrePaint += OnRowPrePaint;

        // 読み込み中オーバーレイパネル
        _loadingPanel.BackColor = Color.FromArgb(150, 0, 0, 0); // 半透明
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
        _gridMain.ContextMenuStrip = _contextMenu;

        this.Controls.Add(_loadingPanel);
        this.Controls.Add(_gridMain); 
        this.Controls.Add(topPanel);
        _loadingPanel.BringToFront();
    }

    private async Task ReloadDataAsync()
    {
        if (_dateSelector.SelectedItem is not string dateStr) return;
        
        _loadingPanel.Visible = true;
        _gridMain.Enabled = false;
        _btnRefresh.Enabled = false;

        string typeFilter = _typeSelector.SelectedItem?.ToString() ?? "すべて";
        
        // ★重い処理を非同期（裏側）で実行
        var result = await Task.Run(() => FetchLogsFromDb(dateStr, typeFilter));

        _logCache = result.Entries;
        _gridMain.RowCount = _logCache.Count;
        _gridMain.Invalidate();
        _lblSummary.Text = result.SummaryText;

        _loadingPanel.Visible = false;
        _gridMain.Enabled = true;
        _btnRefresh.Enabled = true;
    }

    // ★DBからのデータ取得専用メソッド（バックグラウンドスレッドで動作）
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

        // ★高速化：関数の使用をやめ、インデックスが効く範囲検索に変更
        string start = $"{dateStr} 00:00:00";
        string end = $"{dateStr} 23:59:59";

        try {
            using var conn = new SqliteConnection($"Data Source={_dbPath}"); conn.Open();
            // GROUP BYを最小限にし、極限まで最適化したSQL
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
                entries.Add(entry);
                if (entry.Type == "Copy") sizeTotal += entry.Size;
            }
        } catch { }

        return (entries, $"[対象] {entries.Count}個 (合計: {FormatSize(sizeTotal)})");
    }

    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _logCache.Count) return;
        var log = _logCache[e.RowIndex];
        switch (e.ColumnIndex) {
            case 0: e.Value = log.UpdateCount > 1 ? $"{log.Time:HH:mm:ss} ({log.UpdateCount})" : $"{log.Time:HH:mm:ss}"; break;
            case 1: e.Value = log.Type; break;
            case 2: e.Value = log.Path; break;
            case 3: e.Value = FormatSize(log.Size); break;
            case 4: e.Value = log.User; break;
            case 5: e.Value = log.Message; break;
        }
    }

    private void OnRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _logCache.Count) return;
        var type = _logCache[e.RowIndex].Type;
        Color fColor = type switch {
            "Copy" => Color.DarkBlue,
            "Delete" => Color.DarkRed,
            "Error" => Color.Red,
            "Convert" => Color.Teal,
            "Optimize" => Color.DarkGreen,
            _ => Color.Black
        };
        _gridMain.Rows[e.RowIndex].DefaultCellStyle.ForeColor = fColor;
    }

    // --- 日付リスト取得（ここは件数が少ないので同期でOK） ---
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

    // 世代表示（以前のロジック継承）
    private void OnShowVersionsClick(object? sender, EventArgs e) { /* ...既存の世代表示ロジック... */ }

    private class LogEntry { public string Type { get; set; } = ""; public string Path { get; set; } = ""; public long Size { get; set; } public string Message { get; set; } = ""; public string User { get; set; } = ""; public DateTime Time { get; set; } public int UpdateCount { get; set; } }
}