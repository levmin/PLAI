using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using PLAI.Services;
using PLAI.ViewModels;

namespace PLAI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();

            _vm = new MainViewModel();
            DataContext = _vm;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            IHardwareInfoProvider provider = new HardwareInfoProvider();
            await _vm.StartupAsync(provider);

            try { InputTextBox.Focus(); } catch { }
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            await _vm.SendUserMessageAsync();
            try { InputTextBox.Focus(); } catch { }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _vm.CancelCurrentOperation();
        }

        private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                await _vm.SendUserMessageAsync();
                try { InputTextBox.Focus(); } catch { }
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            _vm.Shutdown();
        }
    }
}
