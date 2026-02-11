using System;
using System.IO;
using Windows.Storage;

namespace PLAI.Services
{
    internal static class ModelStoragePaths
    {
        public const string ModelsRootFolderName = "Models";
        public const string ModelFileName = "model.onnx";
        public const string TempDownloadFileName = "model.onnx.download";
        public const string LogFileName = "log.txt";

        public static string GetModelsRootPath()
        {
            // Mandatory: package-local storage
            // ApplicationData.Current.LocalFolder maps to LocalState for packaged desktop apps.
            var root = ApplicationData.Current.LocalFolder.Path;
            return Path.Combine(root, ModelsRootFolderName);
        }

        public static string GetModelFolderPath(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                throw new ArgumentException("Model id is required.", nameof(modelId));
            }

            return Path.Combine(GetModelsRootPath(), modelId);
        }

        public static string GetFinalModelPath(string modelId)
        {
            return Path.Combine(GetModelFolderPath(modelId), ModelFileName);
        }

        public static string GetTempModelPath(string modelId)
        {
            return Path.Combine(GetModelFolderPath(modelId), TempDownloadFileName);
        }

        public static string GetLogFilePath()
        {
            // Mandatory: LocalState/log.txt (same root as Models folder)
            var root = ApplicationData.Current.LocalFolder.Path;
            return Path.Combine(root, LogFileName);
        }
    }
}
