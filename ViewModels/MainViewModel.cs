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
        private string _title = "PLAI - MVVM";

        // Placeholder services registered manually in the constructor
        private readonly HardwareDetectionService _hardwareDetectionService;
        private readonly ModelCatalogService _modelCatalogService;
        private readonly ModelDownloadService _modelDownloadService;

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
            private set
            {
                if (_selectedModelName != value)
                {
                    _selectedModelName = value;
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
