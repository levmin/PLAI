using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PLAI.ViewModels;

namespace PLAI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private ScrollViewer? _transcriptScrollViewer;
        private bool _isPinnedToBottom = true;

        // Default constructor for designer support.
        public MainWindow() : this(new MainViewModel())
        {
        }

        public MainWindow(MainViewModel vm)
        {
            InitializeComponent();

            _vm = vm;
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
                if (!_isPinnedToBottom) return;

                if (TranscriptList.Items.Count > 0)
                {
                    var last = TranscriptList.Items[TranscriptList.Items.Count - 1];
                    TranscriptList.ScrollIntoView(last);
                }
            }
            catch { }
        }

        private void TranscriptScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            try
            {
                if (_transcriptScrollViewer is null) return;

                // Only treat it as a user scroll when content height did not change.
                if (e.ExtentHeightChange == 0)
                {
                    _isPinnedToBottom = _transcriptScrollViewer.VerticalOffset >= (_transcriptScrollViewer.ScrollableHeight - 20);
                }
            }
            catch { }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Wire scroll behavior after template is applied.
            try
            {
                _transcriptScrollViewer ??= FindVisualChild<ScrollViewer>(TranscriptList);
                if (_transcriptScrollViewer is not null)
                {
                    _transcriptScrollViewer.ScrollChanged -= TranscriptScrollChanged;
                    _transcriptScrollViewer.ScrollChanged += TranscriptScrollChanged;
                }
            }
            catch { }

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
            try
            {
                if (_transcriptScrollViewer is not null)
                {
                    _transcriptScrollViewer.ScrollChanged -= TranscriptScrollChanged;
                }
            }
            catch { }
            _vm.Shutdown();
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var descendant = FindVisualChild<T>(child);
                if (descendant is not null) return descendant;
            }
            return null;
        }
    }
}
