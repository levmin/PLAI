using System;
using System.IO;
using System.Text;

namespace PLAI.Services
{
    // Simple file-backed store in LocalApplicationData folder. Swallows exceptions and behaves like no saved state on error.
    public class PackageLocalSelectedModelStateStore : ISelectedModelStateStore
    {
        private readonly string _filePath;

        public PackageLocalSelectedModelStateStore()
        {
            try
            {
                var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appFolder = Path.Combine(folder, "PLAI");
                Directory.CreateDirectory(appFolder);
                _filePath = Path.Combine(appFolder, "selected_model.txt");
            }
            catch
            {
                _filePath = string.Empty;
            }
        }

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
