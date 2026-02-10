using System.Text;
using System.Windows;
using PLAI.ViewModels;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PLAI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Set the view model as DataContext for minimal MVVM
            var vm = new MainViewModel();
            this.DataContext = vm;

            this.Loaded += (s, e) =>
            {
                try
                {
                    var provider = new Services.HardwareInfoProvider();
                    // Call detection on the UI thread for simplicity; provider is fast and guarded.
                    vm.RunDetectionAndSelection(provider);
                }
                catch
                {
                    // ensure no exception bubbles to the UI
                }
            };
        }
    }
}