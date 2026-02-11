using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PLAI.Services
{
    // File-backed store in package-local app storage when packaged, otherwise falls back to LocalApplicationData.
    // Swallows exceptions and behaves like no saved state on error.
    public class PackageLocalSelectedModelStateStore : ISelectedModelStateStore
    {
        private readonly string _filePath;

        public PackageLocalSelectedModelStateStore()
        {
            try
            {
                var folder = TryGetPackagedLocalStatePath() ??
                             Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                // Keep a stable subfolder name regardless of packaging.
                var appFolder = Path.Combine(folder, "PLAI");
                Directory.CreateDirectory(appFolder);
                _filePath = Path.Combine(appFolder, "selected_model.txt");
            }
            catch
            {
                _filePath = string.Empty;
            }
        }

        private static string? TryGetPackagedLocalStatePath()
        {
            try
            {
                // If the process has no package identity, this returns APPMODEL_ERROR_NO_PACKAGE.
                uint length = 0;
                int rc = GetCurrentPackageFamilyName(ref length, null);
                const int APPMODEL_ERROR_NO_PACKAGE = 15700; // 0x3D54
                const int ERROR_INSUFFICIENT_BUFFER = 122;

                if (rc == APPMODEL_ERROR_NO_PACKAGE)
                {
                    return null;
                }

                if (rc != ERROR_INSUFFICIENT_BUFFER || length == 0)
                {
                    return null;
                }

                var sb = new StringBuilder((int)length);
                rc = GetCurrentPackageFamilyName(ref length, sb);
                if (rc != 0)
                {
                    return null;
                }

                var familyName = sb.ToString();
                if (string.IsNullOrWhiteSpace(familyName)) return null;

                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, "Packages", familyName, "LocalState");
            }
            catch
            {
                return null;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);

        public bool TryLoadSelectedModelId(out string? id)
        {
            id = null;
            try
            {
                if (string.IsNullOrEmpty(_filePath)) return false;
                if (!File.Exists(_filePath)) return false;
                var text = File.ReadAllText(_filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return false;
                id = text.Trim();
                return true;
            }
            catch
            {
                id = null;
                return false;
            }
        }

        public void SaveSelectedModelId(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(_filePath)) return;
                File.WriteAllText(_filePath, id ?? string.Empty, Encoding.UTF8);
            }
            catch
            {
                // swallow
            }
        }

        public void Clear()
        {
            try
            {
                if (string.IsNullOrEmpty(_filePath)) return;
                if (File.Exists(_filePath)) File.Delete(_filePath);
            }
            catch
            {
                // swallow
            }
        }
    }
}
