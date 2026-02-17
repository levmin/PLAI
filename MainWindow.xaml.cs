using System.ComponentModel;
using System.Collections.Specialized;
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

            // Keep the transcript pinned to the bottom when new messages are added.
            _vm.Messages.CollectionChanged += Messages_CollectionChanged;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                if (TranscriptList.Items.Count > 0)
                {
                    var last = TranscriptList.Items[TranscriptList.Items.Count - 1];
                    TranscriptList.ScrollIntoView(last);
                }
            }
            catch { }
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

        private async void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ChatGPT-style: Enter sends, Shift+Enter inserts newline.
            if ((e.Key == Key.Enter || e.Key == Key.Return) && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                await _vm.SendUserMessageAsync();
                try { InputTextBox.Focus(); } catch { }
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            try { _vm.Messages.CollectionChanged -= Messages_CollectionChanged; } catch { }
            _vm.Shutdown();
        }
    }
}
