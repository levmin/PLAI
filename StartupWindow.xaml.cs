using System.ComponentModel;
using System.Windows;
using PLAI.ViewModels;

namespace PLAI
{
    public partial class StartupWindow : Window
    {
        private readonly MainViewModel _vm;
        private bool _closingFromCode;

        public StartupWindow(MainViewModel vm, string modelName)
        {
            InitializeComponent();

            _vm = vm;

            Title = "PLAI";
            HeaderText.Text = "PLAI is starting...";
            DetailText.Text = string.IsNullOrWhiteSpace(modelName)
                ? "Initializing model"
                : $"Initializing {modelName}";

            Closing += StartupWindow_Closing;
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

        private void StartupWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_closingFromCode) return;
            _vm.CancelCurrentOperation();
        }
    }
}
