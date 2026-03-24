using System;
using System.Text.Json;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Forms;
using System.Drawing;
using System.IO;
using System.Collections.Generic;

namespace MBack.Config
{
    public partial class Form1 : Form
    {
        private Label _lblEmergency = new();
        private TabControl _tabControl = new();
        private DataGridView _grid = new();
        
        // タブボタン
        private Button _btnAdd = new();
        private Button _btnEdit = new();
        private Button _btnDuplicate = new();
        private Button _btnDelete = new();
        private Button _btnExclusion = new();
        private Button _btnLog = new();
        private Button _btnAdvanced = new(); 
        private Button _btnMail = new(); 
        private Button _btnHelp = new();
        private Button _btnCustom = new();

        // フッター
        private Button _btnService = new();
        private Button _btnRestart = new();
        private Button _btnSave = new();
        private Label _lblStatus = new();

        // 内部設定値
        private List<BackupPair> _backupList = new();
        private List<string> _globalExclusions = new();
        private int _logRetentionDays = 60; 
        private int _ransomwareThreshold = 2000; 
        private string _maintStart = "00:00";
        private string _maintEnd = "00:00";
        private bool _sendSummary = false;
        private MailSettings _mailConfig = new();
        
        // ★ MBack_CRS カスタム設定
        private bool _heicEnabled = false;
        private bool _keepHeic = true;
        private int _heicLongSide = 3500;
        private int _heicQuality = 85;
        private bool _preserveExif = true;
        private bool _resizeEnabled = false;
        private string _accessDbPath = "";
        private string _siteFolderPath = "";
        private string _resizeTime = "02:00";

        private string _configDir;
        private string _jsonPath;

        public Form1()
        {
            this.Text = "MBack 設定ツール (v2.0 - CRS Custom Edition)";
            this.ClientSize = new Size(800, 550); 
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AutoScaleMode = AutoScaleMode.Dpi; 

            _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MBack");
            if (!Directory.Exists(_configDir)) Directory.CreateDirectory(_configDir);
            _jsonPath = Path.Combine(_configDir, "appsettings.json");
            
            SetupLayout();
            LoadSettings();
            UpdateGrid();

            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += (s, e) => UpdateServiceState();
            timer.Start();
            UpdateServiceState();
        }

        private void SetupLayout()
        {
            _lblEmergency.Text = "【警告】異常検知により緊急停止中！下部の復旧ボタンを押してください";
            _lblEmergency.BackColor = Color.Red; _lblEmergency.ForeColor = Color.White;
            _lblEmergency.Dock = DockStyle.Top; _lblEmergency.Height = 35;
            _lblEmergency.TextAlign = ContentAlignment.MiddleCenter;
            _lblEmergency.Font = new Font(this.Font, FontStyle.Bold);
            _lblEmergency.Visible = false;

            var footerPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.FromArgb(235, 240, 245) };
            _lblStatus.Text = "状態取得中..."; _lblStatus.Font = new Font(this.Font, FontStyle.Bold);
            _lblStatus.Bounds = new Rectangle(15, 15, 170, 30); 

            InitButton(_btnService, "サービス停止", OnServiceClick); _btnService.Bounds = new Rectangle(190, 12, 180, 36);
            InitButton(_btnRestart, "🔄 再起動", OnRestartClick); _btnRestart.BackColor = Color.LightSkyBlue; _btnRestart.Bounds = new Rectangle(380, 12, 90, 36);
            InitButton(_btnSave, "保存して閉じる", OnSaveClick); _btnSave.Font = new Font(this.Font, FontStyle.Bold); _btnSave.BackColor = Color.LightGray; _btnSave.Bounds = new Rectangle(635, 12, 140, 36);
            footerPanel.Controls.AddRange(new Control[] { _lblStatus, _btnService, _btnRestart, _btnSave });

            _tabControl.Dock = DockStyle.Fill;
            _tabControl.ItemSize = new Size(180, 30);

            // タブ1
            var tab1 = new TabPage("バックアップ・除外設定");
            var tab1BtnPanel = new Panel { Dock = DockStyle.Bottom, Height = 55 };
            InitButton(_btnAdd, "➕ 追加", OnAddClick); _btnAdd.Bounds = new Rectangle(15, 10, 85, 35);
            InitButton(_btnEdit, "✏️ 編集", OnEditClick); _btnEdit.Bounds = new Rectangle(110, 10, 85, 35);
            InitButton(_btnDuplicate, "📋 複製", OnDuplicateClick); _btnDuplicate.Bounds = new Rectangle(205, 10, 85, 35);
            InitButton(_btnDelete, "❌ 削除", OnDeleteClick); _btnDelete.Bounds = new Rectangle(300, 10, 85, 35);
            InitButton(_btnExclusion, "🚫 グローバル除外設定", OnExclusionClick); _btnExclusion.Bounds = new Rectangle(585, 10, 180, 35);
            tab1BtnPanel.Controls.AddRange(new Control[] { _btnAdd, _btnEdit, _btnDuplicate, _btnDelete, _btnExclusion });

            _grid.Dock = DockStyle.Fill; _grid.AllowUserToAddRows = false; _grid.ReadOnly = true; _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.Columns.Add("Source", "監視元"); _grid.Columns[0].Width = 300;
            _grid.Columns.Add("Dest", "バックアップ先"); _grid.Columns[1].Width = 300;
            _grid.Columns.Add("User", "NAS認証"); _grid.Columns[2].Width = 100;
            _grid.Columns.Add("Cmd", "スクリプト"); _grid.Columns[3].Width = 150;
            tab1.Controls.Add(_grid); tab1.Controls.Add(tab1BtnPanel);

            // タブ2
            var tab2 = new TabPage("システム・ログ・通知");
            var tab2Panel = new Panel { Dock = DockStyle.Fill };
            Action<Button, string, Color, EventHandler> setupTile = (btn, text, color, handler) => {
                btn.Text = text; btn.BackColor = color; btn.FlatStyle = FlatStyle.Flat; btn.Font = new Font(this.Font.FontFamily, 11, FontStyle.Bold);
                btn.Click += handler;
            };
            setupTile(_btnLog, "🔍 履歴とログを見る・復元する", Color.LightYellow, OnLogClick);
            setupTile(_btnHelp, "❓ MBack 使い方ガイド", Color.LightCyan, OnHelpClick);
            setupTile(_btnAdvanced, "⚙️ 詳細設定", Color.WhiteSmoke, OnAdvancedClick);
            setupTile(_btnMail, "✉️ メール通知設定", Color.WhiteSmoke, OnMailClick);
            setupTile(_btnCustom, "🚀 MBack_CRS カスタム機能\n\n(HEIC変換 ＆ 過去案件リサイズ)", Color.AliceBlue, OnCustomClick);
            _btnCustom.FlatAppearance.BorderColor = Color.CornflowerBlue; _btnCustom.FlatAppearance.BorderSize = 2;

            _btnLog.Bounds = new Rectangle(40, 20, 340, 100); _btnHelp.Bounds = new Rectangle(400, 20, 340, 100);
            _btnAdvanced.Bounds = new Rectangle(40, 135, 340, 100); _btnMail.Bounds = new Rectangle(400, 135, 340, 100);
            _btnCustom.Bounds = new Rectangle(40, 250, 700, 90);

            tab2Panel.Controls.AddRange(new Control[] { _btnLog, _btnHelp, _btnAdvanced, _btnMail, _btnCustom });
            tab2.Controls.Add(tab2Panel);

            _tabControl.TabPages.AddRange(new[] { tab1, tab2 });
            this.Controls.Add(_tabControl); this.Controls.Add(footerPanel); this.Controls.Add(_lblEmergency);
            footerPanel.BringToFront(); _tabControl.BringToFront();
        }

        private void InitButton(Button btn, string text, EventHandler handler) { btn.Text = text; btn.Click += handler; }

        private void UpdateServiceState() {
            try {
                using var sc = new System.ServiceProcess.ServiceController("MBackService");
                bool isRunning = sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
                _lblStatus.Text = isRunning ? "● 実行中" : "○ 停止中";
                _lblStatus.ForeColor = isRunning ? Color.DarkGreen : Color.Red;
                _btnService.Text = isRunning ? "サービス停止 (Stop)" : "サービス開始 (Start)";
            } catch { _lblStatus.Text = "？ 状態不明"; }
        }

        private void OnAddClick(object? s, EventArgs e) { using var f = new BackupSettingForm(new BackupPair()); if (f.ShowDialog() == DialogResult.OK) { _backupList.Add(f.Pair); UpdateGrid(); SaveSettings(); } }
        private void OnEditClick(object? s, EventArgs e) { if (_grid.SelectedRows.Count == 0) return; int i = _grid.SelectedRows[0].Index; using var f = new BackupSettingForm(_backupList[i]); if (f.ShowDialog() == DialogResult.OK) { _backupList[i] = f.Pair; UpdateGrid(); SaveSettings(); } }
        private void OnDuplicateClick(object? s, EventArgs e) { if (_grid.SelectedRows.Count == 0) return; int i = _grid.SelectedRows[0].Index; var c = _backupList[i]; var cp = new BackupPair { Source = c.Source, Destination = c.Destination, UserName = c.UserName, Password = c.Password, PreCommand = c.PreCommand }; using var f = new BackupSettingForm(cp); if (f.ShowDialog() == DialogResult.OK) { _backupList.Add(f.Pair); UpdateGrid(); SaveSettings(); } }
        private void OnDeleteClick(object? s, EventArgs e) { if (_grid.SelectedRows.Count == 0) return; if (MessageBox.Show("削除しますか？", "確認", MessageBoxButtons.YesNo) == DialogResult.Yes) { _backupList.RemoveAt(_grid.SelectedRows[0].Index); UpdateGrid(); SaveSettings(); } }
        private void OnExclusionClick(object? s, EventArgs e) { using var f = new ExclusionForm(_globalExclusions); if (f.ShowDialog() == DialogResult.OK) { _globalExclusions = f.Exclusions; SaveSettings(); } }
        private void OnMailClick(object? s, EventArgs e) { using var f = new MailSettingsForm(_mailConfig); if (f.ShowDialog() == DialogResult.OK) { _mailConfig = f.Config; SaveSettings(); } }
        private void OnAdvancedClick(object? s, EventArgs e) { using var f = new AdvancedSettingsForm(_logRetentionDays, _ransomwareThreshold, _maintStart, _maintEnd, _sendSummary); if (f.ShowDialog() == DialogResult.OK) { _logRetentionDays = f.LogRetentionDays; _ransomwareThreshold = f.RansomwareThreshold; _maintStart = f.MaintenanceStart; _maintEnd = f.MaintenanceEnd; _sendSummary = f.SendDailySummary; SaveSettings(); } }
        private void OnLogClick(object? s, EventArgs e) { using var f = new LogViewerForm(); f.ShowDialog(); }
        private void OnHelpClick(object? s, EventArgs e) { using var f = new HelpForm(); f.ShowDialog(); }
        private void OnServiceClick(object? s, EventArgs e) { /* 既存のサービス操作ロジック */ }
        private void OnRestartClick(object? s, EventArgs e) { /* 既存の再起動ロジック */ }

        private void OnCustomClick(object? sender, EventArgs e)
        {
            using var form = new MBackCrsSettingsForm(_heicEnabled, _keepHeic, _heicLongSide, _heicQuality, _preserveExif, _resizeEnabled, _accessDbPath, _siteFolderPath, _resizeTime);
            if (form.ShowDialog() == DialogResult.OK) {
                _heicEnabled = form.HeicEnabled; _keepHeic = form.KeepHeic;
                _heicLongSide = form.HeicLongSide; _heicQuality = form.HeicQuality; _preserveExif = form.PreserveExif;
                _resizeEnabled = form.ResizeEnabled; _accessDbPath = form.AccessDbPath; _siteFolderPath = form.SiteFolderPath; _resizeTime = form.ResizeTime;
                SaveSettings();
            }
        }

        private void LoadSettings() {
            if (!File.Exists(_jsonPath)) return;
            try {
                var s = JsonSerializer.Deserialize<AppSettingsRaw>(File.ReadAllText(_jsonPath));
                if (s != null) {
                    _backupList = s.BackupSettings ?? new(); _globalExclusions = s.GlobalExclusions ?? new();
                    _logRetentionDays = s.LogRetentionDays; _ransomwareThreshold = s.RansomwareThreshold;
                    _maintStart = s.MaintenanceStart; _maintEnd = s.MaintenanceEnd; _sendSummary = s.SendDailySummary; _mailConfig = s.MailConfig ?? new();
                    _heicEnabled = s.HeicConversionEnabled; _keepHeic = s.KeepOriginalHeic;
                    _heicLongSide = s.HeicLongSide; _heicQuality = s.HeicQuality; _preserveExif = s.PreserveExif;
                    _resizeEnabled = s.ResizeTaskEnabled; _accessDbPath = s.AccessDbPath ?? ""; _siteFolderPath = s.SiteFolderPath ?? ""; _resizeTime = s.ResizeTaskTime ?? "02:00";
                }
            } catch { }
        }

        private void SaveSettings() {
            try {
                var s = new AppSettingsRaw {
                    BackupSettings = _backupList, GlobalExclusions = _globalExclusions, LogRetentionDays = _logRetentionDays,
                    RansomwareThreshold = _ransomwareThreshold, MaintenanceStart = _maintStart, MaintenanceEnd = _maintEnd,
                    SendDailySummary = _sendSummary, MailConfig = _mailConfig,
                    HeicConversionEnabled = _heicEnabled, KeepOriginalHeic = _keepHeic,
                    HeicLongSide = _heicLongSide, HeicQuality = _heicQuality, PreserveExif = _preserveExif,
                    ResizeTaskEnabled = _resizeEnabled, AccessDbPath = _accessDbPath, SiteFolderPath = _siteFolderPath, ResizeTaskTime = _resizeTime
                };
                File.WriteAllText(_jsonPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            } catch { }
        }

        private void UpdateGrid() { _grid.Rows.Clear(); foreach (var p in _backupList) _grid.Rows.Add(p.Source, p.Destination, p.UserName, Path.GetFileName(p.PreCommand)); }
    }

    // ==========================================
    // ★新しく美しくなった CRS カスタム設定画面
    // ==========================================
    public class MBackCrsSettingsForm : Form
    {
        public bool HeicEnabled { get; private set; }
        public bool KeepHeic { get; private set; }
        public int HeicLongSide { get; private set; }
        public int HeicQuality { get; private set; }
        public bool PreserveExif { get; private set; }
        public bool ResizeEnabled { get; private set; }
        public string AccessDbPath { get; private set; } = "";
        public string SiteFolderPath { get; private set; } = "";
        public string ResizeTime { get; private set; } = "02:00";

        private CheckBox _chkHeic = new() { Text = "HEIC画像を自動的にJPGへ変換する", AutoSize = true, Font = new Font("メイリオ", 10, FontStyle.Bold) };
        private CheckBox _chkKeepHeic = new() { Text = "元ファイルを「Original」フォルダに退避する", AutoSize = true };
        private CheckBox _chkExif = new() { Text = "撮影情報(EXIF)を維持する（工事写真に推奨）", AutoSize = true };
        
        private ComboBox _cmbHeicPreset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private TrackBar _tkQuality = new() { Minimum = 10, Maximum = 100, TickFrequency = 10, Width = 200 };
        private Label _lblQuality = new() { Text = "画質: 85%", AutoSize = true };

        private CheckBox _chkResize = new() { Text = "失注案件(3年以上前)の画像を自動リサイズする", AutoSize = true, Font = new Font("メイリオ", 10, FontStyle.Bold) };
        private TextBox _txtAccess = new();
        private TextBox _txtSite = new();
        private DateTimePicker _dtpTime = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
        private Button _btnTrial = new() { Text = "🔍 対象プロジェクトを試算...", Width = 180, Height = 30, BackColor = Color.WhiteSmoke };

        public MBackCrsSettingsForm(bool hOn, bool kH, int side, int qual, bool exif, bool rOn, string acc, string site, string time)
        {
            HeicEnabled = hOn; KeepHeic = kH; HeicLongSide = side; HeicQuality = qual; PreserveExif = exif;
            ResizeEnabled = rOn; AccessDbPath = acc; SiteFolderPath = site; ResizeTime = time;

            this.Text = "MBack_CRS カスタム機能プロキシ設定";
            this.Size = new Size(620, 680);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Font = new Font("メイリオ", 9);

            _cmbHeicPreset.Items.AddRange(new object[] { "画質優先 (A3印刷/長辺4000px)", "標準 (A4印刷/長辺3000px)", "軽量 (画面表示/長辺1600px)", "リサイズなし" });
            if (HeicLongSide >= 4000) _cmbHeicPreset.SelectedIndex = 0;
            else if (HeicLongSide >= 3000) _cmbHeicPreset.SelectedIndex = 1;
            else if (HeicLongSide >= 1600) _cmbHeicPreset.SelectedIndex = 2;
            else _cmbHeicPreset.SelectedIndex = 3;

            _tkQuality.Value = HeicQuality;
            _lblQuality.Text = $"画質: {HeicQuality}%";
            _tkQuality.Scroll += (s, e) => _lblQuality.Text = $"画質: {_tkQuality.Value}%";

            if (DateTime.TryParseExact(ResizeTime, "HH:mm", null, System.Globalization.DateTimeStyles.None, out var t)) _dtpTime.Value = t;

            _btnTrial.Click += (s, e) => MessageBox.Show("この機能は、MBackServiceが動作している環境で、Accessデータベースを解析して「失注」かつ「3年前」の案件フォルダ数をカウントします。\n(実装後、実際のDBをスキャンします)", "試算シミュレーション");

            SetupLayout();
        }

        private void SetupLayout()
        {
            var mainScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var container = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20), Width = 580, AutoSize = true };

            // --- HEIC Group ---
            var gpHeic = new GroupBox { Text = "📸 HEIC自動変換設定", Width = 540, Height = 260, Padding = new Padding(15) };
            _chkHeic.Location = new Point(20, 30);
            _chkKeepHeic.Location = new Point(45, 60);
            _chkExif.Location = new Point(45, 85);
            _chkExif.Checked = PreserveExif;
            
            var lblP = new Label { Text = "変換プリセット:", Location = new Point(45, 125), AutoSize = true };
            _cmbHeicPreset.Location = new Point(160, 122);
            
            _lblQuality.Location = new Point(45, 165);
            _tkQuality.Location = new Point(160, 160);

            var lblHeicTip = new Label { Text = "※ iPhone等のHEIC画像を、印刷に最適なサイズで自動変換します。", Location = new Point(20, 215), ForeColor = Color.DimGray, AutoSize = true };
            
            gpHeic.Controls.AddRange(new Control[] { _chkHeic, _chkKeepHeic, _chkExif, lblP, _cmbHeicPreset, _lblQuality, _tkQuality, lblHeicTip });
            _chkHeic.Checked = HeicEnabled; _chkKeepHeic.Checked = KeepHeic;

            // --- Resize Group ---
            var gpResize = new GroupBox { Text = "💾 過去案件の容量最適化", Width = 540, Height = 260, Padding = new Padding(15) };
            _chkResize.Location = new Point(20, 30); _chkResize.Checked = ResizeEnabled;
            
            var lblAcc = new Label { Text = "Access DB:", Location = new Point(45, 75), AutoSize = true };
            _txtAccess.Bounds = new Rectangle(140, 72, 300, 25);
            var btnAcc = new Button { Text = "...", Bounds = new Rectangle(450, 72, 40, 25) };
            btnAcc.Click += (s, e) => { var d = new OpenFileDialog { Filter = "Access|*.accdb" }; if (d.ShowDialog() == DialogResult.OK) _txtAccess.Text = d.FileName; };

            var lblSite = new Label { Text = "現場ルート:", Location = new Point(45, 110), AutoSize = true };
            _txtSite.Bounds = new Rectangle(140, 107, 300, 25);
            var btnSite = new Button { Text = "...", Bounds = new Rectangle(450, 107, 40, 25) };
            btnSite.Click += (s, e) => { var d = new FolderBrowserDialog(); if (d.ShowDialog() == DialogResult.OK) _txtSite.Text = d.SelectedPath; };

            var lblT = new Label { Text = "実行時刻:", Location = new Point(45, 150), AutoSize = true };
            _dtpTime.Location = new Point(140, 147);
            _btnTrial.Location = new Point(240, 147);

            var lblResizeTip = new Label { Text = "※ 「失注」から3年以上経過した写真のみ、1MB以下に圧縮します。\n   PDFなどはスキップされ、元データは上書きされます。", Location = new Point(20, 195), ForeColor = Color.Maroon, AutoSize = true };

            gpResize.Controls.AddRange(new Control[] { _chkResize, lblAcc, _txtAccess, btnAcc, lblSite, _txtSite, btnSite, lblT, _dtpTime, _btnTrial, lblResizeTip });
            _txtAccess.Text = AccessDbPath; _txtSite.Text = SiteFolderPath;

            // --- Footer ---
            var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Width = 540, Height = 50, Padding = new Padding(0, 10, 0, 0) };
            var btnOk = new Button { Text = "設定を反映する", Width = 150, Height = 35, BackColor = Color.CornflowerBlue, ForeColor = Color.White, Font = new Font(this.Font, FontStyle.Bold), FlatStyle = FlatStyle.Flat };
            var btnCancel = new Button { Text = "キャンセル", Width = 100, Height = 35 };
            
            btnOk.Click += (s, e) => {
                HeicEnabled = _chkHeic.Checked; KeepHeic = _chkKeepHeic.Checked; PreserveExif = _chkExif.Checked;
                HeicQuality = _tkQuality.Value;
                HeicLongSide = _cmbHeicPreset.SelectedIndex switch { 0 => 4000, 1 => 3000, 2 => 1600, _ => 0 };
                ResizeEnabled = _chkResize.Checked; AccessDbPath = _txtAccess.Text; SiteFolderPath = _txtSite.Text; ResizeTime = _dtpTime.Value.ToString("HH:mm");
                DialogResult = DialogResult.OK;
            };
            btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            btnPanel.Controls.AddRange(new Control[] { btnOk, btnCancel });

            container.Controls.Add(gpHeic);
            container.Controls.Add(new Label { Height = 10 }); // Spacer
            container.Controls.Add(gpResize);
            container.Controls.Add(btnPanel);
            mainScroll.Controls.Add(container);
            this.Controls.Add(mainScroll);
        }
    }
}