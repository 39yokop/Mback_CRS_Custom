using System;
using System.Drawing;
using System.Windows.Forms;

namespace MBack.Config;

public class HelpForm : Form
{
    public HelpForm()
    {
        // ★タイトルを3.0仕様にアップデート
        this.Text = "MBack_CRS_Custom 3.0 使い方ガイド (自律最適化＆防衛仕様)";
        this.Size = new Size(680, 600); // 新機能タブが増えたので少し広げます
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        SetupLayout();
    }

    private void SetupLayout()
    {
        var tab = new TabControl { Dock = DockStyle.Fill, Padding = new Point(10, 10), Font = new Font(this.Font.FontFamily, 10, FontStyle.Regular) };

        // --- 1. 基本操作 ---
        tab.TabPages.Add(CreateHelpPage("基本と便利機能", 
            "【MBackの便利なUI機能】\n\n" +
            "● ドラッグ＆ドロップ対応\n" +
            "   設定の「監視元フォルダ」や「バックアップ先」の入力欄には、\n" +
            "   エクスプローラーからフォルダを直接ドラッグ＆ドロップできます。\n\n" +
            "● 複製（コピー）追加機能\n" +
            "   NASのパスワード入力等を省略するため、既存の設定行を選択して\n" +
            "   『📋 複製』ボタンを押すだけで簡単に設定を増やせます。\n\n" +
            "● NAS認証の自動化\n" +
            "   Windowsサービスのログオン情報を変更せずに、アプリ側から\n" +
            "   NASへ自動ログインが可能です。ユーザー名は『サーバ名\\ユーザー名』\n" +
            "   の形式で入力してください。"));

        // --- 2. ★新規：CRSカスタム機能 ---
        tab.TabPages.Add(CreateHelpPage("CRS カスタム機能", 
            "【MBack_CRS_Custom 3.0 専用の自律最適化機能】\n\n" +
            "① 📸 HEIC画像の自動変換\n" +
            "   iPhone等で撮影されたHEIC画像を、印刷に最適なサイズのJPGへ自動変換します。\n" +
            "   変換後の元ファイルは「Original」フォルダに安全に退避され、\n" +
            "   EXIF（撮影情報）も工事写真用に保持することが可能です。\n\n" +
            "② 💾 過去案件の容量最適化（自律リサイズ）\n" +
            "   Accessデータベースと連動し、「失注」かつ「3年以上前」の\n" +
            "   プロジェクトフォルダを自動抽出。1MB以上の重い写真だけを\n" +
            "   画質劣化を抑えつつ1MB以下に圧縮し、NASの空き容量を劇的に回復させます。\n\n" +
            "③ 🌱 エコ設定とスケジュール\n" +
            "   バックアップの保存世代数を1〜50の間で設定可能になりました（推奨:10）。\n" +
            "   リサイズタスクは「毎月1日」などの指定スケジュールで深夜帯に自動実行されます。\n" +
            "   ※設定画面の「▶️ 今すぐテスト実行」で即時動作の確認も可能です。"));

        // --- 3. 除外設定 ---
        tab.TabPages.Add(CreateHelpPage("除外設定", 
            "【除外パターンの書き方（重要）】\n\n" +
            "● MBack では、アスタリスク（*）を使用しません。\n" +
            "● 指定した文字がパスの中に『含まれているか』で判定します。\n\n" +
            "   例1: .tmp と書けば、大文字小文字問わず .tmp を除外します。\n" +
            "   例2: ~ と書けば、Office等の一時ファイルをすべて除外します。\n\n" +
            "● フォルダごと除外したい場合は、\\System\\ のように\n" +
            "   円マークで囲んで指定すると安全です。"));

        // --- 4. 最新防衛仕様 (ランサム対策) ---
        tab.TabPages.Add(CreateHelpPage("ランサムウェア対策", 
            "【最強ハイブリッド検知・絶対防衛システム】\n\n" +
            "MBackは、以下の3段構えでファイル破壊からシステムを守ります。\n\n" +
            "① 囮（ハニーポット）検知\n" +
            "   監視元フォルダに「!000_MBack_Trap.txt」という隠しファイルを作ります。\n" +
            "   ランサムウェアがこのファイルに触れた瞬間、即座に緊急停止します。\n\n" +
            "② 遅延（上書き防止）バックアップ\n" +
            "   ファイルを変更後、バックアップ先にコピーされるまで待機時間を設けます。\n" +
            "   この間に異常を検知すれば、破壊されたファイルがNASに上書きされる\n" +
            "   ことはありません。\n\n" +
            "③ 工事写真リサイズ・スルー機能\n" +
            "   画像ファイルの操作は危険判定から除外されるため、大量の写真コピーで\n" +
            "   誤爆停止することはありません。\n\n" +
            "④ 緊急停止の永続化\n" +
            "   緊急停止が発動すると、その情報はファイルとして記録されます。\n" +
            "   Windows Updateによる再起動やサービスの予期しない再起動が\n" +
            "   発生しても、緊急停止の状態は解除されず維持されます。\n" +
            "   内容を確認して手動で解除するまで、バックアップが誤って\n" +
            "   再開されることはありません。"));

        // --- ★新規：災害復旧ツール(Mrestore)とパスワード保護 ---
        tab.TabPages.Add(CreateHelpPage("災害復旧(Mrestore)",
            "【もしもの時の一括復元ツール「Mrestore」】\n\n" +
            "ランサムウェア被害等でサーバー自体を再構築した場合、大量の\n" +
            "ファイルを1つずつ手作業で戻すのは非常に大変です。そのための\n" +
            "専用ツールが「Mrestore」です。\n\n" +
            "● どこにあるか\n" +
            "   スタートメニューの『MBack_CRS_Custom 災害復旧ツール(Mrestore)』\n" +
            "   から起動できます。管理者権限が必要です。\n\n" +
            "● 使うタイミング\n" +
            "   サーバーを再構築し、新しい共有フォルダを用意した直後に使います。\n" +
            "   通常のバックアップ運用中に触る必要はありません。\n\n" +
            "● 何をしてくれるか\n" +
            "   1. バックアップ先フォルダの中身を丸ごとスキャンし、\n" +
            "      フォルダ構造を復元先に再作成します\n" +
            "   2. 更新日時が新しいファイルから優先的にコピーするので、\n" +
            "      直近よく使うファイルから使える状態になっていきます\n" +
            "   3. 過去にランサムウェアの緊急停止が発動していた場合、\n" +
            "      発動時刻より後に変更された疑わしいファイルは自動的に\n" +
            "      1つ前の安全な世代（履歴）から復元し、汚染されたファイルを\n" +
            "      誤って復元してしまうことを防ぎます\n" +
            "   4. 復元先に既にファイルがある場合は、中身を確認してから\n" +
            "      削除するか選べるので、稼働中のフォルダを誤って\n" +
            "      上書きする事故を防げます\n" +
            "   5. 完了後、復元先フォルダに詳細ログを書き出します\n\n" +
            "● 事前の備え\n" +
            "   バックアップ先フォルダには自動的に「restore-manifest.json」\n" +
            "   という情報ファイルが作られており、Mrestoreはこれを読んで\n" +
            "   元のフォルダパスや発動時刻を自動的に把握します。復元を行う\n" +
            "   前に特別な準備をする必要はありません。\n\n" +
            "【NAS等の接続パスワードの保護】\n" +
            "バックアップ設定で入力したパスワードは、保存時に自動的に\n" +
            "暗号化されます（Windowsの標準機能によるもので、そのパソコン\n" +
            "以外では読み取れません）。設定ファイルを万一盗まれても、\n" +
            "中のパスワードがそのまま読まれる心配はありません。"));

        // --- 5. メンテと復旧 ---
        tab.TabPages.Add(CreateHelpPage("メンテと復旧", 
            "【緊急停止からのワンクリック復旧】\n\n" +
            "● 緊急停止が発生すると、メイン画面の下部にオレンジ色の\n" +
            "   『⚠️ 緊急停止を解除して再開』ボタンが出現します。\n" +
            "● フォルダ内にウイルス等の異常がないことを確認してから\n" +
            "   このボタンを1回押すだけで、ロックを解除して安全に再開します。\n\n" +
            "【監視の一時停止（メンテモード）】\n" +
            "● 詳細設定から『開始時間』と『終了時間』を指定すると、その時間は\n" +
            "   監視と異常検知が一時停止します。他ソフトとの衝突回避に使えます。"));

        var btnClose = new Button { Text = "閉じる", Dock = DockStyle.Bottom, Height = 40, Font = new Font(this.Font, FontStyle.Bold) };
        btnClose.Click += (s, e) => this.Close();

        this.Controls.Add(tab);
        this.Controls.Add(btnClose);
    }

    private TabPage CreateHelpPage(string title, string content)
    {
        var page = new TabPage(title);
        var txt = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Text = content,
            BorderStyle = BorderStyle.None,
            Padding = new Padding(15),
            Font = new Font("メイリオ", 10),
            BackColor = Color.White
        };
        page.Controls.Add(txt);
        return page;
    }
}