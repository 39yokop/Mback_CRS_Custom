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

namespace MBack.Config;

public class LogViewerForm : Form
{
    private ComboBox _dateSelector = new();
    private ComboBox _typeSelector = new(); // ★新規：ログ種別フィルター
    private Label _lblSummary = new();
    private DataGridView _gridMain = new(); // ★新規：超高速仮想モードグリッド
    private ContextMenuStrip _contextMenu = new();
    private Button _btnRefresh = new(); 

    private string _dbPath;
    private List<BackupPair> _backupPairs = new(); 
    
    // 仮想モード用のデータキャッシュ
    private List<LogEntry> _logCache = new();

    public LogViewerForm()
    {
        this.Text = "MBack 履歴復元センター (超高速 DataGridView 仮想モード版)";
        this.Size = new Size(1200, 750); 
        this.StartPosition = FormStartPosition.CenterParent;

        _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MBack", "Database", "history.db");

        EnsureDatabaseSchema();
        LoadSettings();
        SetupLayout();
        LoadDateListFromDb(); 
    }

    private void EnsureDatabaseSchema()
    {
        if (!File.Exists(_dbPath)) return;
        try {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = new SqliteCommand("ALTER TABLE LogEntries ADD COLUMN User TEXT;", conn);
            cmd.ExecuteNonQuery();
        } catch { }
    }

    private void SetupLayout()
    {
        var topPanel = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(10), BackColor = Color.FromArgb(240, 240, 240) };
        
        var lblDate = new Label { Text = "日付選択:", AutoSize = true, Location = new Point(15, 20), Font = new Font(this.Font, FontStyle.Bold) };
        _dateSelector.Location = new Point(100, 17); _dateSelector.Width = 150; _dateSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _dateSelector.SelectedIndexChanged += OnFilterChanged;

        var lblType = new Label { Text = "種別フィルター:", AutoSize = true, Location = new Point(270, 20), Font = new Font(this.Font, FontStyle.Bold) };
        _typeSelector.Location = new Point(390, 17); _typeSelector.Width = 150; _typeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _typeSelector.Items.AddRange(new[] { "すべて", "Copy (コピー)", "Delete (ゴミ箱)", "Error (エラー)", "Convert (HEIC変換)", "Optimize (リサイズ)" });
        _typeSelector.SelectedIndex = 0;
        _typeSelector.SelectedIndexChanged += OnFilterChanged;

        _btnRefresh.Text = "最新の状態に更新"; _btnRefresh.Location = new Point(560, 15); _btnRefresh.Size = new Size(120, 30);
        _btnRefresh.Click += (s, e) => {
            LoadDateListFromDb(); 
            ReloadData();
        };
        
        _lblSummary.Location = new Point(700, 20); _lblSummary.AutoSize = true; _lblSummary.Font = new Font(this.Font, FontStyle.Bold);
        topPanel.Controls.AddRange(new Control[] { lblDate, _dateSelector, lblType, _typeSelector, _btnRefresh, _lblSummary });

        // ★ DataGridView の仮想モード設定
        _gridMain.Dock = DockStyle.Fill;
        _gridMain.VirtualMode = true; // 超高速化の鍵
        _gridMain.AllowUserToAddRows = false;
        _gridMain.AllowUserToDeleteRows = false;
        _gridMain.ReadOnly = true;
        _gridMain.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridMain.RowHeadersVisible = false;
        _gridMain.BackgroundColor = Color.White;
        _gridMain.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        
        // カラム定義
        _gridMain.Columns.Add("Time", "最新時刻 (更新回数)"); _gridMain.Columns[0].Width = 160;
        _gridMain.Columns.Add("Type", "操作種別"); _gridMain.Columns[1].Width = 100;
        _gridMain.Columns.Add("Path", "ファイルパス"); _gridMain.Columns[2].Width = 450;
        _gridMain.Columns.Add("Size", "サイズ"); _gridMain.Columns[3].Width = 100;
        _gridMain.Columns.Add("User", "ユーザー"); _gridMain.Columns[4].Width = 120;
        _gridMain.Columns.Add("Message", "詳細メッセージ"); _gridMain.Columns[5].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        // 仮想モードのデータバインドイベント
        _gridMain.CellValueNeeded += OnCellValueNeeded;
        // 色付けイベント
        _gridMain.RowPrePaint += OnRowPrePaint;

        // コンテキストメニュー設定
        var menuVersion = new ToolStripMenuItem("★ 世代選択と復元...", null, OnShowVersionsClick) { Font = new Font(this.Font, FontStyle.Bold) };
        var menuRestore = new ToolStripMenuItem("最新から即時復元", null, (s, e) => RestoreFile(null));
        var menuOpen = new ToolStripMenuItem("バックアップ先を開く", null, OnOpenBackupFolderClick);
        _contextMenu.Items.AddRange(new ToolStripItem[] { menuVersion, new ToolStripSeparator(), menuRestore, menuOpen });
        _gridMain.ContextMenuStrip = _contextMenu;

        this.Controls.Add(_gridMain); 
        this.Controls.Add(topPanel);
    }

    private void LoadDateListFromDb()
    {
        if (!File.Exists(_dbPath)) return;
        try {
            using var conn = new SqliteConnection($"Data Source={_dbPath}"); conn.Open();
            string sql = "SELECT DISTINCT strftime('%Y-%m-%d', Time) as LogDate FROM LogEntries ORDER BY LogDate DESC";
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            string? sel = _dateSelector.SelectedItem?.ToString();
            
            _dateSelector.SelectedIndexChanged -= OnFilterChanged; // 一時解除
            _dateSelector.Items.Clear();
            while (reader.Read()) _dateSelector.Items.Add(reader.GetString(0));
            
            if (_dateSelector.Items.Count > 0) {
                int idx = sel != null ? _dateSelector.FindStringExact(sel) : 0;
                _dateSelector.SelectedIndex = idx >= 0 ? idx : 0;
            }
            _dateSelector.SelectedIndexChanged += OnFilterChanged;
        } catch { }
    }

    private void OnFilterChanged(object? sender, EventArgs e) { ReloadData(); }

    private void ReloadData()
    {
        if (_dateSelector.SelectedItem is string d) LoadLogFromDb(d);
    }

    private void LoadLogFromDb(string dateStr)
    {
        _logCache.Clear();
        int uniqueCount = 0; long sizeTotal = 0;
        long optimizedSizeTotal = 0; // リサイズで節約した容量の集計用

        string typeFilter = _typeSelector.SelectedItem?.ToString() ?? "すべて";
        string sqlFilter = "";
        
        if (typeFilter.StartsWith("Copy")) sqlFilter = "AND Type = 'Copy'";
        else if (typeFilter.StartsWith("Delete")) sqlFilter = "AND Type = 'Delete'";
        else if (typeFilter.StartsWith("Error")) sqlFilter = "AND Type = 'Error'";
        else if (typeFilter.StartsWith("Convert")) sqlFilter = "AND Type = 'Convert'";
        else if (typeFilter.StartsWith("Optimize")) sqlFilter = "AND Type = 'Optimize'";

        try {
            using var conn = new SqliteConnection($"Data Source={_dbPath}"); conn.Open();
            string sql = $@"
                SELECT Type, Path, MAX(Size), MAX(Message), MAX(User), MAX(Time), COUNT(Id) 
                FROM LogEntries 
                WHERE date(Time) = @date {sqlFilter}
                GROUP BY Type, Path 
                ORDER BY MAX(Time) ASC";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@date", dateStr);
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
                
                _logCache.Add(entry);
                uniqueCount++;
                
                if (entry.Type == "Copy") sizeTotal += entry.Size;
                if (entry.Type == "Optimize" && entry.Message.Contains("節約")) {
                    // メッセージから節約容量を抽出（例: "完了: 3.5MB節約"）の簡易計算
                    optimizedSizeTotal += entry.Size; // 処理後サイズを加算
                }
            }
        } catch { } finally {
            _gridMain.RowCount = _logCache.Count; // 仮想モードの要：行数だけをセットする
            _gridMain.Invalidate(); // 再描画
        }

        if (typeFilter.StartsWith("Optimize")) {
            _lblSummary.Text = $"[対象ファイル] {uniqueCount}個 (最適化後合計: {FormatSize(optimizedSizeTotal)})";
        } else {
            _lblSummary.Text = $"[対象ファイル] {uniqueCount}個 (最新合計: {FormatSize(sizeTotal)})";
        }
    }

    // ★ 仮想モード：画面に表示されるセルだけ都度このメソッドが呼ばれる（超高速）
    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _logCache.Count) return;
        var log = _logCache[e.RowIndex];

        switch (e.ColumnIndex) {
            case 0: e.Value = log.UpdateCount > 1 ? $"{log.Time:HH:mm:ss} (計{log.UpdateCount}回)" : $"{log.Time:HH:mm:ss}"; break;
            case 1: e.Value = log.Type; break;
            case 2: e.Value = log.Path; break;
            case 3: e.Value = FormatSize(log.Size); break;
            case 4: e.Value = log.User; break;
            case 5: e.Value = log.Message; break;
        }
    }

    // ★ 行の色付け
    private void OnRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _logCache.Count) return;
        var type = _logCache[e.RowIndex].Type;
        
        Color fColor = Color.Black;
        switch (type) {
            case "Copy": fColor = Color.DarkBlue; break;
            case "Delete": fColor = Color.DarkRed; break;
            case "Error": fColor = Color.Red; break;
            case "Convert": fColor = Color.Teal; break;
            case "Optimize": fColor = Color.DarkGreen; break;
        }
        _gridMain.Rows[e.RowIndex].DefaultCellStyle.ForeColor = fColor;
    }

    private void OnShowVersionsClick(object? sender, EventArgs e)
    {
        if (_gridMain.SelectedRows.Count == 0) return;
        int idx = _gridMain.SelectedRows[0].Index;
        string path = _logCache[idx].Path;

        string? bPath = GetBackupPath(path);
        if (bPath == null) return;
        var vers = new List<string>(); if (File.Exists(bPath)) vers.Add(bPath);
        for (int i = 1; i <= 50; i++) { string v = $"{bPath}.v{i}"; if (File.Exists(v)) vers.Add(v); }
        if (vers.Count == 0) {
            MessageBox.Show("履歴データが見つかりません。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var f = new Form { Text = "世代選択: " + Path.GetFileName(path), Size = new Size(650, 450), StartPosition = FormStartPosition.CenterParent };
        
        var grid = new DataGridView {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White
        };
        grid.Columns.Add("Type", "種別"); grid.Columns.Add("Date", "バックアップ日時"); grid.Columns.Add("Size", "サイズ");
        grid.Columns[0].Width = 60; grid.Columns[2].Width = 80;

        foreach (var v in vers) {
            var info = new FileInfo(v);
            string type = (v == bPath) ? "最新" : "履歴";
            grid.Rows.Add(type, info.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"), FormatSize(info.Length));
        }

        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(5) };
        var btnExport = new Button { Text = "🔍 別の場所に保存して確認", Dock = DockStyle.Right, Width = 280, Font = new Font(f.Font, FontStyle.Bold), BackColor = Color.LightYellow };
        var btnRestore = new Button { Text = "⚠️ 元の場所に上書き復元", Dock = DockStyle.Left, Width = 280, Font = new Font(f.Font, FontStyle.Bold), BackColor = Color.LightPink };

        btnRestore.Click += (s, ev) => { 
            if (grid.SelectedRows.Count > 0) { 
                RestoreFile(vers[grid.SelectedRows[0].Index], path); 
                f.DialogResult = DialogResult.OK; 
            } 
        };

        btnExport.Click += (s, ev) => {
            if (grid.SelectedRows.Count > 0) {
                ExportForPreview(vers[grid.SelectedRows[0].Index], path);
            }
        };

        grid.CellDoubleClick += (s, ev) => {
            if (ev.RowIndex >= 0) {
                RestoreFile(vers[ev.RowIndex], path); 
                f.DialogResult = DialogResult.OK;
            }
        };

        btnPanel.Controls.Add(btnRestore); btnPanel.Controls.Add(btnExport);
        f.Controls.Add(grid); f.Controls.Add(btnPanel); 
        f.ShowDialog();
    }

    private void ExportForPreview(string versionPath, string originalPath)
    {
        using var sfd = new SaveFileDialog();
        sfd.FileName = Path.GetFileName(originalPath);
        sfd.Title = "確認用に保存する場所を選んでください（デスクトップ推奨）";
        sfd.Filter = "すべてのファイル (*.*)|*.*";
        
        if (sfd.ShowDialog() == DialogResult.OK)
        {
            try {
                File.Copy(versionPath, sfd.FileName, true);
                if (MessageBox.Show("保存しました。今すぐ開いて中身を確認しますか？", "プレビュー確認", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                }
            } catch (Exception ex) {
                MessageBox.Show("保存に失敗しました: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void RestoreFile(string? vPath, string? tPath = null)
    {
        if (_gridMain.SelectedRows.Count == 0 && tPath == null) return;
        string path = tPath ?? _logCache[_gridMain.SelectedRows[0].Index].Path;
        string? bPath = vPath ?? GetBackupPath(path);
        
        if (string.IsNullOrEmpty(path) || bPath == null || !File.Exists(bPath)) return;
        
        if (MessageBox.Show($"⚠️警告⚠️\n以下のファイルをバックアップデータで【上書き】します。\n本当によろしいですか？\n\n対象: {path}", "上書き復元の最終確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
            try {
                string? d = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                File.Copy(bPath, path, true); MessageBox.Show("正常に復元されました！", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (Exception ex) { MessageBox.Show("復元エラー: " + ex.Message); }
        }
    }

    private void OnOpenBackupFolderClick(object? sender, EventArgs e)
    {
        if (_gridMain.SelectedRows.Count == 0) return;
        string p = _logCache[_gridMain.SelectedRows[0].Index].Path;
        
        string? b = GetBackupPath(p); 
        if (b != null) { 
            string? f = Path.GetDirectoryName(b); 
            if (!string.IsNullOrEmpty(f) && Directory.Exists(f)) Process.Start("explorer.exe", f); 
        }
    }

    private string? GetBackupPath(string p)
    {
        foreach (var pair in _backupPairs) {
            if (p.StartsWith(pair.Source, StringComparison.OrdinalIgnoreCase)) {
                string r = p.Substring(pair.Source.Length).TrimStart('\\');
                string n = Path.Combine(pair.Destination, r);
                
                string? dirName = Path.GetDirectoryName(n);
                var parentDir = Directory.GetParent(n);
                
                bool hasHistory = false;
                if (parentDir != null && dirName != null && Directory.Exists(dirName)) {
                    hasHistory = parentDir.GetFiles(Path.GetFileName(n) + ".v*").Length > 0;
                }

                if (File.Exists(n) || hasHistory) return n;
                
                return Path.Combine(pair.Destination, "_TRASH_", r);
            }
        }
        return null;
    }

    private string FormatSize(long b) { string[] s = { "B", "KB", "MB", "GB", "TB" }; double l = b; int i = 0; while (l >= 1024 && i < 4) { i++; l /= 1024; } return $"{l:0.##} {s[i]}"; }

    private void LoadSettings() {
        try {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MBack", "appsettings.json");
            if (File.Exists(p)) { var s = JsonSerializer.Deserialize<AppSettingsRaw>(File.ReadAllText(p)); if (s?.BackupSettings != null) _backupPairs = s.BackupSettings; }
        } catch { }
    }

    // ★ 仮想モード用のデータクラス
    private class LogEntry
    {
        public string Type { get; set; } = "";
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public string Message { get; set; } = "";
        public string User { get; set; } = "";
        public DateTime Time { get; set; }
        public int UpdateCount { get; set; }
    }
}