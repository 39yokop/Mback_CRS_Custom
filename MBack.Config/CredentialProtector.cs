using System;
using System.Security.Cryptography;
using System.Text;

namespace MBack.Config;

// appsettings.json 内のパスワードをDPAPI(LocalMachineスコープ)で保護する。
// MBack.Service側の同名クラスと同じ Prefix/Entropy を使うことで相互に復号できる。
internal static class CredentialProtector
{
    private const string Prefix = "enc:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MBack.CredentialProtector.v1");

    public static string Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText ?? "";
        if (plainText.StartsWith(Prefix, StringComparison.Ordinal)) return plainText; // 既に保護済み

        try
        {
            byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.LocalMachine);
            return Prefix + Convert.ToBase64String(cipher);
        }
        catch
        {
            return plainText; // DPAPIが使えない環境でも設定保存自体は失敗させない
        }
    }

    public static string Unprotect(string? storedValue)
    {
        if (string.IsNullOrEmpty(storedValue)) return storedValue ?? "";
        if (!storedValue.StartsWith(Prefix, StringComparison.Ordinal)) return storedValue; // 旧形式(平文)はそのまま扱う

        try
        {
            byte[] cipher = Convert.FromBase64String(storedValue.Substring(Prefix.Length));
            byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return storedValue; // 復号できない場合は壊れた値のまま返す
        }
    }
}
