using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Runtime.Versioning;

// ★修正：Worker.csと同じ名前空間に統一しました
namespace MBack.Service;

// CA1416警告（プラットフォーム互換性）を回避するためWindows限定を明示
[SupportedOSPlatform("windows")]
public class AccessManager
{
    /// <summary>
    /// Accessデータベースから「失注」かつ「3年以上前」の案件Noのリストを取得します。
    /// </summary>
    /// <param name="dbPath">AccessDB (.accdb / .mdb) のフルパス</param>
    /// <returns>条件に一致する案件Noのリスト</returns>
    public static List<string> GetTargetProjects(string dbPath)
    {
        var targetProjects = new List<string>();
        
        // パスが空、またはファイルが存在しない場合は空のリストを返す
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath)) return targetProjects;

        // 現在の年から3年引いて、対象となる境界年を算出（例: 2026年なら2023年以下が対象）
        int targetYear = DateTime.Now.Year - 3;

        // ACE.OLEDB.12.0プロバイダを使用して接続文字列を構築
        string connStr = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={dbPath};";
        
        try {
            using var conn = new OleDbConnection(connStr);
            conn.Open();
            
            // 案件ｔテーブルから確度が「失注」の案件Noをすべて取得
            string sql = "SELECT 案件No FROM 案件ｔ WHERE 確度 = '失注'";
            using var cmd = new OleDbCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read()) {
                // DBの値がNullの場合はスキップ
                if (reader.IsDBNull(0)) continue;
                
                string? projectNo = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(projectNo) || projectNo.Length < 4) continue;

                // 案件Noの先頭4文字（西暦）を抽出して数値変換
                string yearStr = projectNo.Substring(0, 4);
                if (int.TryParse(yearStr, out int year)) {
                    // 3年以上前の案件であればリストに追加
                    if (year <= targetYear) {
                        targetProjects.Add(projectNo);
                    }
                }
            }
        } catch (Exception ex) {
            // 例外が発生した場合は上位の呼び出し元（Worker等）でログ化させるためスロー
            throw new Exception($"Accessデータベースの読み込みに失敗しました: {ex.Message}");
        }
        
        return targetProjects;
    }
}