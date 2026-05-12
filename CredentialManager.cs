using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TNovCommon
{
    public static class CredentialManager
    {
        private static readonly string CredFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TNov", "TNovCommon", "credentials.dat");

        public static void Save(string upn, string password)
        {
            var data = Encoding.UTF8.GetBytes($"{upn}:{password}");
            byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(CredFilePath));
            File.WriteAllBytes(CredFilePath, encrypted);
        }

        public static (string upn, string password) Load()
        {
            if (!File.Exists(CredFilePath)) return (null, null);
            byte[] encrypted = File.ReadAllBytes(CredFilePath);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            string text = Encoding.UTF8.GetString(decrypted);
            var parts = text.Split(new[] { ':' }, 2);
            return (parts[0], parts[1]);
        }

        public static void Clear()
        {
            if (File.Exists(CredFilePath))
                File.Delete(CredFilePath);
        }
    }
}
