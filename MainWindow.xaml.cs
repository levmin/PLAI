using System.Windows;
using PLAI.ViewModels;

namespace PLAI
{
    /// <summary>
    /// Interaction logic for MainWindow
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Set the view model as DataContext for minimal MVVM
            var vm = new MainViewModel();
            DataContext = vm;

            Loaded += async (s, e) =>
            {
                try
                {
                    var provider = new Services.HardwareInfoProvider();
                    await vm.RunDetectionSelectionAndEnsureModelAsync(provider);
                }
                catch
                {
                    // ensure no exception bubbles to the UI
                }
            };

            Closing += (s, e) =>
            {
                try { vm.CancelDownloads(); } catch { }
            };
        }
    }
}
