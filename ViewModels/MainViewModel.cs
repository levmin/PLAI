using System.ComponentModel;
using System.Runtime.CompilerServices;
using PLAI.Services;

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
