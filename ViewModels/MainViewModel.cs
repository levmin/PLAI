using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using PLAI.Services;
using PLAI.Models;

namespace PLAI.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _title = "PLAI";

        // Placeholder services registered manually in the constructor
        private readonly HardwareDetectionService _hardwareDetectionService;
        private readonly ModelCatalogService _modelCatalogService;
        private readonly ModelDownloadService _modelDownloadService;

        // Persisted selected model id (package-local app storage).
        private readonly ISelectedModelStateStore _selectedModelStore = new PackageLocalSelectedModelStateStore();

        // Cancel downloads when the app/window is closing.
        private readonly CancellationTokenSource _downloadCts = new();

        public MainViewModel()
        {
            // Manual registration of services (no DI framework)
            _hardwareDetectionService = new HardwareDetectionService();
            _modelCatalogService = new ModelCatalogService();
            _modelDownloadService = new ModelDownloadService();

            // Startup wiring: attempt to restore the last selection; otherwise
            // selection will be performed after hardware detection on window load.
            InitializeSelection();

#if DEBUG
            try
            {
                var provider = new HardwareInfoProvider();
                var info = provider.GetHardwareInfo();
                Debug.WriteLine($"HardwareInfo: {info}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HardwareInfoProvider threw: {ex}");
            }
#endif
        }

        public void CancelDownloads()
        {
            try { _downloadCts.Cancel(); } catch { }
        }

        // Selected model (read-only from the UI's perspective)
        private ModelDescriptor? _selectedModel;

        public ModelDescriptor? SelectedModel
        {
            get => _selectedModel;
            private set
            {
                if (_selectedModel != value)
                {
                    _selectedModel = value;
                    OnPropertyChanged();
                    SelectedModelName = _selectedModel?.Name ?? string.Empty;
                }
            }
        }

        private string _selectedModelName = string.Empty;

        public string SelectedModelName
        {
            get => _selectedModelName;
            set
            {
                if (_selectedModelName != value)
                {
                    _selectedModelName = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _detectedHardwareSummary = "Detecting...";

        public string DetectedHardwareSummary
        {
            get => _detectedHardwareSummary;
            set
            {
                if (_detectedHardwareSummary != value)
                {
                    _detectedHardwareSummary = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _selectionReason = string.Empty;

        public string SelectionReason
        {
            get => _selectionReason;
            set
            {
                if (_selectionReason != value)
                {
                    _selectionReason = value;
                    OnPropertyChanged();
                }
            }
        }

        private void InitializeSelection()
        {
            TryRestoreSelection();
        }

        private bool TryRestoreSelection()
        {
            try
            {
                if (!_selectedModelStore.TryLoadSelectedModelId(out var id) || string.IsNullOrEmpty(id))
                {
                    return false;
                }

                // Startup recovery (mandatory):
                // If model files are missing or incomplete, clear persisted selection and treat as first run.
                if (!_modelDownloadService.IsModelComplete(id))
                {
                    try { AppLogger.Warn($"Missing model detected at startup (id {id})"); } catch { }
                    _modelDownloadService.CleanupIncompleteModelArtifacts(id);

                    try { _selectedModelStore.Clear(); } catch { }
                    try { AppLogger.Info($"Persisted selection cleared (id {id})"); } catch { }
                    return false;
                }

                var models = _modelCatalogService.GetAllModels();
                foreach (var m in models)
                {
                    if (m.Id == id)
                    {
                        SelectedModel = m;
                        SelectedModelName = m.Name;
                        SelectionReason = "Restored previous selection";
                        DetectedHardwareSummary = "Hardware detection skipped (restored selection)";
                        return true;
                    }
                }

                // Saved id no longer exists in catalog.
                // Clear so next launch behaves as first run.
                try { _selectedModelStore.Clear(); } catch { }
                try { AppLogger.Info($"Persisted selection cleared (id {id})"); } catch { }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Perform detection and selection (frozen behavior) and then ensure the selected model is downloaded.
        /// </summary>
        public async Task RunDetectionSelectionAndEnsureModelAsync(IHardwareInfoProvider provider)
        {
            // Existing frozen detection/selection behavior.
            RunDetectionAndSelection(provider);

            var chosen = SelectedModel;
            if (chosen is null)
            {
                return;
            }

            // If restored selection, model is already complete by contract check in TryRestoreSelection.
            if (_modelDownloadService.IsModelComplete(chosen.Id))
            {
                return;
            }

            // Deterministic download contract:
            // - download to temp file
            // - atomic rename
            // - on failure/cancel: clear persisted selection so next launch behaves as first run
            bool ok = false;
            try
            {
                ok = await _modelDownloadService.EnsureModelDownloadedAsync(chosen, _downloadCts.Token).ConfigureAwait(true);
            }
            catch
            {
                ok = false;
            }

            if (!ok)
            {
                try { _selectedModelStore.Clear(); } catch { }
                try { AppLogger.Info($"Persisted selection cleared (id {chosen.Id})"); } catch { }
            }
        }

        /// <summary>
        /// Perform detection using the provided hardware info provider and select a model accordingly.
        /// This method catches exceptions and updates bindable properties with safe text on error.
        /// Selection semantics are frozen and must not change.
        /// </summary>
        public void RunDetectionAndSelection(IHardwareInfoProvider provider)
        {
            // If a prior selection is available, keep it.
            // First-run behavior is handled elsewhere (e.g. download completeness).
            if (TryRestoreSelection())
            {
                return;
            }

            if (provider is null)
            {
                DetectedHardwareSummary = "Unknown (no provider)";
                SelectionReason = "Provider missing";
                SelectedModelName = string.Empty;
                return;
            }

            try
            {
                var info = provider.GetHardwareInfo();

                DetectedHardwareSummary = $"RAM: {info.RamGb} GB (known: {info.IsRamKnown}), VRAM: {info.VramGb} GB (known: {info.IsVramKnown}), Discrete GPU: {info.HasDiscreteGpu}";

                var capabilities = new HardwareCapabilities
                {
                    AvailableRamGb = info.RamGb,
                    AvailableVramGb = info.IsVramKnown ? info.VramGb : 0.0
                };

                var models = _modelCatalogService.GetAllModels();
                var chosen = ModelSelectionService.ChooseBestModel(capabilities, models);

                if (chosen is null)
                {
                    SelectedModelName = string.Empty;
                    SelectionReason = "No suitable model found for detected hardware.";
                }
                else
                {
                    SelectedModel = chosen;
                    try { AppLogger.Info($"Selected model {chosen.Id}"); } catch { }
                    SelectionReason = $"Selected by capability match (RAM {capabilities.AvailableRamGb} GB, VRAM {capabilities.AvailableVramGb} GB).";

                    try { _selectedModelStore.SaveSelectedModelId(chosen.Id); } catch { }
                }
            }
            catch (Exception ex)
            {
                DetectedHardwareSummary = "Unknown (error)";
                SelectedModelName = string.Empty;
                SelectionReason = "Error during detection: " + ex.Message;
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
