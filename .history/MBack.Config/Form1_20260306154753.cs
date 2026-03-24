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
        
        // タブ1用ボタン
        private Button _btnAdd = new();
        private Button _btnEdit = new();
        private Button _btnDuplicate = new();
        private Button _btnDelete = new();
        private Button _btnExclusion = new();
        
        // タブ2（ダッシュボード）用ボタン
        private Button _btnLog = new();
        private Button _btnAdvanced = new(); 
        private Button _btnMail = new(); 
        private Button _btnHelp = new();
        private Button _btnCustom = new(); // ★新規：カスタム機能用ボタン

        // フッター用コントロール
        private Button _btnService = new();
        private Button _btnRestart = new();
        private Button _btnSave = new();
        private Label _lblStatus = new();

        private List<BackupPair> _backupList = new();
        private List<string> _globalExclusions = new();
        private int _logRetentionDays = 60; 
        private int _ransomwareThreshold = 2000; 
        private string _maintStart = "00:00";
        private string _maintEnd = "00:00";
        private bool _sendSummary = false;
        private MailSettings _mailConfig = new();

        // ★新規：MBack_CRS カスタム機能設定
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
            this.Text = "MBack 設定ツール (v2.0 - Fixed Size Edition)";
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
            // --- 1. 緊急停止警告ラベル ---
            _lblEmergency.Text = "【警告】異常検知により緊急停止中！下部の復旧ボタンを押してください";
            _lblEmergency.BackColor = Color.Red; _lblEmergency.ForeColor = Color.White;
            _lblEmergency.Dock = DockStyle.Top; _lblEmergency.Height = 35;
            _lblEmergency.TextAlign = ContentAlignment.MiddleCenter;
            _lblEmergency.Font = new Font(this.Font, FontStyle.Bold);
            _lblEmergency.Visible = false;

            // --- 2. サービス常駐フッター ---
            var footerPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.FromArgb(235, 240, 245) };
            
            _lblStatus.Text = "状態取得中...";
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft; 
            _lblStatus.Font = new Font(this.Font, FontStyle.Bold);
            _lblStatus.Bounds = new Rectangle(15, 15, 170, 30); 

            InitButton(_btnService, "サービス停止", OnServiceClick);
            _btnService.Bounds = new Rectangle(190, 12, 180, 36);

            InitButton(_btnRestart, "🔄 再起動", OnRestartClick); 
            _btnRestart.BackColor = Color.LightSkyBlue;
            _btnRestart.Bounds = new Rectangle(380, 12, 90, 36);

            InitButton(_btnSave, "保存して閉じる", OnSaveClick);
            _btnSave.Font = new Font(this.Font, FontStyle.Bold); 
            _btnSave.BackColor = Color.LightGray;
            _btnSave.Bounds = new Rectangle(635, 12, 140, 36);

            footerPanel.Controls.AddRange(new Control[] { _lblStatus, _btnService, _btnRestart, _btnSave });

            // --- 3. タブコントロール ---
            _tabControl.Dock = DockStyle.Fill;
            _tabControl.ItemSize = new Size(180, 30);
            _tabControl.Font = new Font(this.Font.FontFamily, 10, FontStyle.Regular);

            // 【タブ1】バックアップ設定・除外
            var tab1 = new TabPage("バックアップ・除外設定");
            
            var tab1BtnPanel = new Panel { Dock = DockStyle.Bottom, Height = 55 };
            
            InitButton(_btnAdd, "➕ 追加", OnAddClick); _btnAdd.Bounds = new Rectangle(15, 10, 85, 35);
            InitButton(_btnEdit, "✏️ 編集", OnEditClick); _btnEdit.Bounds = new Rectangle(110, 10, 85, 35);
            InitButton(_btnDuplicate, "📋 複製", OnDuplicateClick); _btnDuplicate.Bounds = new Rectangle(205, 10, 85, 35);
            InitButton(_btnDelete, "❌ 削除", OnDeleteClick); _btnDelete.Bounds = new Rectangle(300, 10, 85, 35);
            
            InitButton(_btnExclusion, "🚫 グローバル除外設定", OnExclusionClick);
            _btnExclusion.Bounds = new Rectangle(585, 10, 180, 35);

            tab1BtnPanel.Controls.AddRange(new Control[] { _btnAdd, _btnEdit, _btnDuplicate, _btnDelete, _btnExclusion });

            _grid.Dock = DockStyle.Fill;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None; 
            _grid.ScrollBars = ScrollBars.Both; 
            _grid.AllowUserToResizeColumns = true; 
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false; 
            _grid.ReadOnly = true; 
            _grid.AllowUserToAddRows = false;
            
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; 
            
            _grid.Columns.Add("Source", "監視元フォルダ"); _grid.Columns[0].Width = 300; 
            _grid.Columns.Add("Dest", "バックアップ先"); _grid.Columns[1].Width = 300; 
            _grid.Columns.Add("User", "NAS認証"); _grid.Columns[2].Width = 100;
            _grid.Columns.Add("Cmd", "事前スクリプト"); _grid.Columns[3].Width = 150;

            tab1.Controls.Add(_grid); 
            tab1.Controls.Add(tab1BtnPanel);

            // 【タブ2】システム・ログ・通知（絶対座標で配置）
            var tab2 = new TabPage("システム・ログ・通知");
            var tab2Panel = new Panel { Dock = DockStyle.Fill };

            Action<Button, string, Color, EventHandler> setupTile = (btn, text, color, handler) => {
                btn.Text = text; btn.BackColor = color; 
                btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderSize = 1; btn.FlatAppearance.BorderColor = Color.LightGray;
                btn.Font = new Font(this.Font.FontFamily, 11, FontStyle.Bold);
                btn.Click -= handler; btn.Click += handler;
            };

            setupTile(_btnLog, "🔍 履歴とログを見る・復元する\n\n(過去のデータを検索・安全に復旧)", Color.LightYellow, OnLogClick);
            setupTile(_btnHelp, "❓ MBack 使い方ガイド\n\n(防衛仕様の確認・マニュアル)", Color.LightCyan, OnHelpClick);
            setupTile(_btnAdvanced, "⚙️ 詳細設定\n\n(メンテ時間・異常検知の閾値など)", Color.WhiteSmoke, OnAdvancedClick);
            setupTile(_btnMail, "✉️ メール通知設定\n\n(日報やエラー通知の送信先)", Color.WhiteSmoke, OnMailClick);
            
            // ★新規：カスタム機能用ワイドタイル
            setupTile(_btnCustom, "🚀 MBack_CRS カスタム機能\n\n(HEIC変換 ＆ 過去案件の容量最適化)", Color.AliceBlue, OnCustomClick);
            _btnCustom.FlatAppearance.BorderColor = Color.CornflowerBlue;
            _btnCustom.FlatAppearance.BorderSize = 2;

            _btnLog.Bounds = new Rectangle(40, 20, 340, 100);
            _btnHelp.Bounds = new Rectangle(400, 20, 340, 100);
            _btnAdvanced.Bounds = new Rectangle(40, 135, 340, 100);
            _btnMail.Bounds = new Rectangle(400, 135, 340, 100);
            _btnCustom.Bounds = new Rectangle(40, 250, 700, 90);

            tab2Panel.Controls.AddRange(new Control[] { _btnLog, _btnHelp, _btnAdvanced, _btnMail, _btnCustom });
            tab2.Controls.Add(tab2Panel);

            _tabControl.TabPages.AddRange(new[] { tab1, tab2 });

            this.Controls.Add(_tabControl);
            this.Controls.Add(footerPanel);
            this.Controls.Add(_lblEmergency);

            _lblEmergency.BringToFront();
            footerPanel.BringToFront();
            _tabControl.BringToFront();
        }

        private void InitButton(Button btn, string text, EventHandler handler)
        {
            btn.Text = text; btn.Click -= handler; btn.Click += handler;
        }

        private void UpdateServiceState()
        {
            string statusText = ""; Color statusColor = Color.Black; bool isRunning = false;

            try {
                using var sc = new System.ServiceProcess.ServiceController("MBackService");
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running) {
                    statusText = "● 実行中"; statusColor = Color.DarkGreen; isRunning = true;
                } else {
                    statusText = "○ 停止中"; statusColor = Color.Red;
                }
            } catch { 
                statusText = "？ 状態不明"; statusColor = Color.Gray;
            }

            string emergencyFile = Path.Combine(_configDir, "emergency.txt");
            if (File.Exists(emergencyFile)) {
                _lblEmergency.Visible = true;
                statusText = "【緊急停止中】"; statusColor = Color.Red;
                _btnService.Text = "⚠️ 緊急停止を解除して再開"; _btnService.BackColor = Color.Orange;
                _btnRestart.Enabled = false;
            } else {
                _lblEmergency.Visible = false;
                _btnRestart.Enabled = true;

                if (isRunning) {
                    _btnService.Text = "サービス停止 (Stop)"; _btnService.BackColor = Color.LightGreen;
                } else {
                    _btnService.Text = "サービス開始 (Start)"; _btnService.BackColor = Color.LightPink;
                }
            }

            _lblStatus.Text = statusText; _lblStatus.ForeColor = statusColor;
        }

        private void OnAddClick(object? sender, EventArgs e)
        {
            using var form = new BackupSettingForm(new BackupPair());
            if (form.ShowDialog() == DialogResult.OK) { _backupList.Add(form.Pair); UpdateGrid(); SaveSettings(); }
        }

        private void OnEditClick(object? sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0) return;
            int index = _grid.SelectedRows[0].Index;
            using var form = new BackupSettingForm(_backupList[index]);
            if (form.ShowDialog() == DialogResult.OK) { _backupList[index] = form.Pair; UpdateGrid(); SaveSettings(); }
        }

        private void OnDuplicateClick(object? sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0) {
                MessageBox.Show("複製する行を選択してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information); return;
            }
            int index = _grid.SelectedRows[0].Index; var current = _backupList[index];
            var copied = new BackupPair {
                Source = current.Source, Destination = current.Destination,
                UserName = current.UserName, Password = current.Password, PreCommand = current.PreCommand
            };
            using var form = new BackupSettingForm(copied); form.Text = "バックアップ詳細設定 (複製)";
            if (form.ShowDialog() == DialogResult.OK) { _backupList.Add(form.Pair); UpdateGrid(); SaveSettings(); }
        }

        private void OnDeleteClick(object? sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0) return;
            if (MessageBox.Show("設定を削除してもよろしいですか？", "削除確認", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                _backupList.RemoveAt(_grid.SelectedRows[0].Index); UpdateGrid(); SaveSettings();
            }
        }

        private void OnExclusionClick(object? sender, EventArgs e) { using var form = new ExclusionForm(_globalExclusions); if (form.ShowDialog() == DialogResult.OK) { _globalExclusions = form.Exclusions; SaveSettings(); } }
        private void OnMailClick(object? sender, EventArgs e) { using var form = new MailSettingsForm(_mailConfig); if (form.ShowDialog() == DialogResult.OK) { _mailConfig = form.Config; SaveSettings(); } }
        
        private void OnAdvancedClick(object? sender, EventArgs e)
        {
            using var form = new AdvancedSettingsForm(_logRetentionDays, _ransomwareThreshold, _maintStart, _maintEnd, _sendSummary);
            if (form.ShowDialog() == DialogResult.OK) {
                _logRetentionDays = form.LogRetentionDays; _ransomwareThreshold = form.RansomwareThreshold;
                _maintStart = form.MaintenanceStart; _maintEnd = form.MaintenanceEnd; _sendSummary = form.SendDailySummary;
                SaveSettings(); 
            }
        }

        // ★新規：カスタム機能設定画面の呼び出し
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

        private void OnLogClick(object? sender, EventArgs e) { using var logForm = new LogViewerForm(); logForm.ShowDialog(); }

        private void OnServiceClick(object? sender, EventArgs e)
        {
            try {
                string emergencyFile = Path.Combine(_configDir, "emergency.txt");
                if (File.Exists(emergencyFile)) {
                    if (MessageBox.Show("異常がないことを確認しましたか？\n緊急停止を解除し、サービスを再開します。", "復旧の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) {
                        File.Delete(emergencyFile);
                        var scRestart = new System.ServiceProcess.ServiceController("MBackService");
                        if (scRestart.Status == System.ServiceProcess.ServiceControllerStatus.Running) {
                            scRestart.Stop(); scRestart.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        }
                        scRestart.Start(); scRestart.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                        MessageBox.Show("サービスを正常に再開しました。");
                    }
                    UpdateServiceState(); return;
                }

                var sc = new System.ServiceProcess.ServiceController("MBackService");
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running) {
                    if (MessageBox.Show("サービスを停止しますか？", "確認", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                        sc.Stop(); sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                } else {
                    if (MessageBox.Show("サービスを開始しますか？", "確認", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                        sc.Start(); sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                }
                UpdateServiceState();
            } catch (Exception ex) { MessageBox.Show("操作失敗: \n" + ex.Message); }
        }

        private void OnRestartClick(object? sender, EventArgs e)
        {
            try {
                var sc = new System.ServiceProcess.ServiceController("MBackService");
                _btnRestart.Text = "再起動中..."; _btnRestart.Enabled = false; Application.DoEvents();

                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running) {
                    sc.Stop(); sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                }
                sc.Start(); sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                
                MessageBox.Show("サービスを再起動しました。\n最新の設定が反映されています。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (Exception ex) { MessageBox.Show("再起動に失敗しました: \n" + ex.Message); } 
            finally { _btnRestart.Text = "🔄 再起動"; _btnRestart.Enabled = true; UpdateServiceState(); }
        }

        private void OnSaveClick(object? sender, EventArgs e) { SaveSettings(); this.Close(); }
        private void OnHelpClick(object? sender, EventArgs e) { using var form = new HelpForm(); form.ShowDialog(); }

        private void LoadSettings()
        {
            if (!File.Exists(_jsonPath)) return;
            try {
                var json = File.ReadAllText(_jsonPath);
                var settings = JsonSerializer.Deserialize<AppSettingsRaw>(json);
                if (settings != null) {
                    _backupList = settings.BackupSettings ?? new(); _globalExclusions = settings.GlobalExclusions ?? new();
                    _logRetentionDays = settings.LogRetentionDays > 0 ? settings.LogRetentionDays : 60;
                    _ransomwareThreshold = settings.RansomwareThreshold > 0 ? settings.RansomwareThreshold : 2000;
                    _maintStart = settings.MaintenanceStart ?? "00:00"; _maintEnd = settings.MaintenanceEnd ?? "00:00";
                    _sendSummary = settings.SendDailySummary; _mailConfig = settings.MailConfig ?? new(); 
                    
                    // ★新規：カスタム機能のロード
                    _heicEnabled = settings.HeicConversionEnabled;
                    _keepHeic = settings.KeepOriginalHeic;
                    _heicLongSide = settings.HeicLongSide > 0 ? settings.HeicLongSide : 3500;
                    _heicQuality = settings.HeicQuality > 0 ? settings.HeicQuality : 85;
                    _preserveExif = settings.PreserveExif;
                    _resizeEnabled = settings.ResizeTaskEnabled;
                    _accessDbPath = settings.AccessDbPath ?? "";
                    _siteFolderPath = settings.SiteFolderPath ?? "";
                    _resizeTime = settings.ResizeTaskTime ?? "02:00";
                }
            } catch { }
        }

        private void SaveSettings()
        {
            try {
                var settings = new AppSettingsRaw {
                    BackupSettings = _backupList, GlobalExclusions = _globalExclusions, LogRetentionDays = _logRetentionDays,
                    RansomwareThreshold = _ransomwareThreshold, MaintenanceStart = _maintStart, MaintenanceEnd = _maintEnd,
                    SendDailySummary = _sendSummary, MailConfig = _mailConfig,
                    
                    // ★新規：カスタム機能のセーブ
                    HeicConversionEnabled = _heicEnabled, KeepOriginalHeic = _keepHeic,
                    HeicLongSide = _heicLongSide, HeicQuality = _heicQuality, PreserveExif = _preserveExif,
                    ResizeTaskEnabled = _resizeEnabled, AccessDbPath = _accessDbPath, 
                    SiteFolderPath = _siteFolderPath, ResizeTaskTime = _resizeTime
                };
                File.WriteAllText(_jsonPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            } catch { }
        }

        private void UpdateGrid()
        {
            _grid.Rows.Clear(); 
            foreach (var p in _backupList) {
                string userDisp = string.IsNullOrEmpty(p.UserName) ? "なし" : p.UserName;
                string cmdDisp = string.IsNullOrEmpty(p.PreCommand) ? "なし" : Path.GetFileName(p.PreCommand);
                _grid.Rows.Add(p.Source, p.Destination, userDisp, cmdDisp);
            }
        }
    }

    public class BackupSettingForm : Form
    {
        public BackupPair Pair { get; private set; }
        private TextBox _txtSrc = new();
        private TextBox _txtDest = new();
        private TextBox _txtUser = new();
        private TextBox _txtPass = new() { PasswordChar = '*' };
        private TextBox _txtCmd = new();

        public BackupSettingForm(BackupPair current)
        {
            Pair = new BackupPair { 
                Source = current.Source, Destination = current.Destination, 
                UserName = current.UserName, Password = current.Password, PreCommand = current.PreCommand 
            };
            this.Text = "バックアップ詳細設定";
            this.Size = new Size(500, 380);
            this.StartPosition = FormStartPosition.CenterParent;
            SetupLayout();
            
            EnableDragAndDrop(_txtSrc);
            EnableDragAndDrop(_txtDest);
            EnableDragAndDrop(_txtCmd);
        }

        private void SetupLayout()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(15) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));

            int row = 0;
            var lblHint = new Label { Text = "💡 ヒント: パス入力欄にはフォルダやファイルをドラッグ＆ドロップできます。", ForeColor = Color.Teal, AutoSize = true, Font = new Font(this.Font, FontStyle.Italic) };
            panel.Controls.Add(lblHint, 0, row); panel.SetColumnSpan(lblHint, 3); row++;

            AddRow(panel, ref row, "監視元フォルダ:", _txtSrc, Pair.Source, true);
            AddRow(panel, ref row, "バックアップ先:", _txtDest, Pair.Destination, true);
            
            var lblAuth = new Label { Text = "\n--- NAS等 ネットワーク認証 (任意) ---", ForeColor = Color.Gray, AutoSize = true };
            panel.Controls.Add(lblAuth, 0, row); panel.SetColumnSpan(lblAuth, 3); row++;
            
            AddRow(panel, ref row, "ユーザー名:", _txtUser, Pair.UserName, false);
            AddRow(panel, ref row, "パスワード:", _txtPass, Pair.Password, false);

            var lblCmd = new Label { Text = "\n--- 事前実行スクリプト (任意) ---", ForeColor = Color.Gray, AutoSize = true };
            panel.Controls.Add(lblCmd, 0, row); panel.SetColumnSpan(lblCmd, 3); row++;
            AddRow(panel, ref row, "バッチ(.bat)パス:", _txtCmd, Pair.PreCommand, true, true);

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 45, Padding = new Padding(5) };
            var btnCancel = new Button { Text = "キャンセル", Width = 90 };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            var btnOk = new Button { Text = "OK", Width = 90 };
            btnOk.Click += (s, e) => { 
                if (string.IsNullOrWhiteSpace(_txtSrc.Text) || string.IsNullOrWhiteSpace(_txtDest.Text)) {
                    MessageBox.Show("元と先は必須です。"); return;
                }
                Pair.Source = _txtSrc.Text; Pair.Destination = _txtDest.Text; 
                Pair.UserName = _txtUser.Text; Pair.Password = _txtPass.Text; Pair.PreCommand = _txtCmd.Text;
                DialogResult = DialogResult.OK; Close(); 
            };
            btnPanel.Controls.Add(btnCancel); btnPanel.Controls.Add(btnOk);
            this.Controls.Add(panel); this.Controls.Add(btnPanel);
        }

        private void AddRow(TableLayoutPanel panel, ref int row, string label, TextBox txt, string val, bool showBrowse, bool isFile = false)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            txt.Text = val; txt.Dock = DockStyle.Fill;
            panel.Controls.Add(txt, 1, row);
            if (showBrowse) {
                var btn = new Button { Text = "参照", Dock = DockStyle.Fill };
                btn.Click += (s, e) => {
                    if (isFile) {
                        using var dlg = new OpenFileDialog { Filter = "実行ファイル|*.bat;*.exe;*.cmd|すべて|*.*" };
                        if (dlg.ShowDialog() == DialogResult.OK) txt.Text = dlg.FileName;
                    } else {
                        using var dlg = new FolderBrowserDialog();
                        if (dlg.ShowDialog() == DialogResult.OK) txt.Text = dlg.SelectedPath;
                    }
                };
                panel.Controls.Add(btn, 2, row);
            }
            row++;
        }

        private void EnableDragAndDrop(TextBox txt)
        {
            txt.AllowDrop = true;
            txt.DragEnter += (s, e) => {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) {
                    e.Effect = DragDropEffects.Copy;
                }
            };
            txt.DragDrop += (s, e) => {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) {
                    if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) {
                        txt.Text = files[0];
                    }
                }
            };
        }
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