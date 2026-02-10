using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
        private readonly ISelectedModelStateStore _selectedModelStore = new InMemorySelectedModelStateStore();

        public MainViewModel()
        {
            // Manual registration of services (no DI framework)
            _hardwareDetectionService = new HardwareDetectionService();
            _modelCatalogService = new ModelCatalogService();
            _modelDownloadService = new ModelDownloadService();
            // Wire up selection logic (read-only)
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

        // Perform read-only wiring: obtain catalog, select best model for a hypothetical capability set
        // No IO, no async, no side-effects.
        private void InitializeSelection()
        {
            var models = _modelCatalogService.GetAllModels();

            // Example capabilities for selection - read-only values (no hardware calls)
            var capabilities = new HardwareCapabilities
            {
                AvailableRamGb = 16.0,
                AvailableVramGb = 8.0
            };

            SelectedModel = ModelSelectionService.ChooseBestModel(capabilities, models);
            // Persist fresh selection if available
            if (SelectedModel != null)
            {
                try { _selectedModelStore.SaveSelectedModelId(SelectedModel.Name); } catch { }
            }
        }

        private bool TryRestoreSelection()
        {
            try
            {
                if (!_selectedModelStore.TryLoadSelectedModelId(out var id) || string.IsNullOrEmpty(id))
                {
                    return false;
                }

                var models = _modelCatalogService.GetAllModels();
                foreach (var m in models)
                {
                    if (m.Name == id)
                    {
                        SelectedModel = m;
                        SelectedModelName = m.Name;
                        SelectionReason = "Restored previous selection";
                        DetectedHardwareSummary = "Hardware detection skipped (restored selection)";
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Perform detection using the provided hardware info provider and select a model accordingly.
        /// This method catches exceptions and updates bindable properties with safe text on error.
        /// </summary>
        public void RunDetectionAndSelection(IHardwareInfoProvider provider)
        {
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
                    SelectionReason = $"Selected by capability match (RAM {capabilities.AvailableRamGb} GB, VRAM {capabilities.AvailableVramGb} GB).";
                }
            }
            catch (System.Exception ex)
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
