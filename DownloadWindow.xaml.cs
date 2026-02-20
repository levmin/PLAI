using System.ComponentModel;
using System.Windows;
using PLAI.ViewModels;

namespace PLAI
{
    public partial class DownloadWindow : Window
    {
        private readonly MainViewModel _vm;
        private bool _closingFromCode;

        public DownloadWindow(MainViewModel vm, string modelName)
        {
            InitializeComponent();

            _vm = vm;
            DataContext = _vm;

            var title = string.IsNullOrWhiteSpace(modelName) ? "Downloading" : $"Downloading {modelName}";
            Title = title;
            HeaderText.Text = title;

            Closing += DownloadWindow_Closing;
        }

        public void CloseFromCode()
        {
            _closingFromCode = true;
            try { Close(); } catch { }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _vm.CancelCurrentOperation();
        }

        private void DownloadWindow_Closing(object? sender, CancelEventArgs e)
        {
            // If the user closes the window via X, treat as cancel.
            if (_closingFromCode) return;
            _vm.CancelCurrentOperation();
        }
    }
}
