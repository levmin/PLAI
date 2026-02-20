using System;
using System.Windows;
using PLAI.Services;
using PLAI.ViewModels;

namespace PLAI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Keep a single startup log entry.
            try { AppLogger.Info("Application starting"); } catch { }

            // During FRE we may have no MainWindow. Prevent auto shutdown until we explicitly show it.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Surface unexpected startup failures instead of silently exiting.
            this.DispatcherUnhandledException += (_, args) =>
            {
                try { AppLogger.Error($"Unhandled UI exception: {args.Exception}"); } catch { }
                try
                {
                    var message = $"Unhandled exception:\n\n{args.Exception}";
                    MessageBox.Show(message, "PLAI", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
                args.Handled = true;
                try { Shutdown(); } catch { }
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                try { AppLogger.Error($"Unhandled exception: {args.ExceptionObject}"); } catch { }
            };

            base.OnStartup(e);

            try
            {
                var vm = new MainViewModel();
                IHardwareInfoProvider provider = new HardwareInfoProvider();

                // Preflight: determine selection and whether a download is needed without showing any windows.
                // This allows a clean second-run experience where we show a "starting" window during warmup.
                var requiresDownload = vm.PreflightRequiresDownload(provider);

                StartupWindow? startupWindow = null;
                if (!requiresDownload)
                {
                    // Second-run path: model is already present, but load + warmup can take time.
                    // Show a small startup window with Cancel.
                    var modelName = vm.SelectedModel?.Name ?? "model";
                    startupWindow = new StartupWindow(vm, modelName);
                    startupWindow.Show();
                }

                // Run the full startup sequence.
                // If we already preflighted, selection is done and should not be repeated.
                await vm.StartupAsync(provider, selectionAlreadyDone: true);

                try { startupWindow?.CloseFromCode(); } catch { }

                if (!vm.IsChatReady)
                {
                    try { Shutdown(); } catch { }
                    return;
                }

                // If startup completed, show the main chat window.
                var main = new MainWindow(vm);
                MainWindow = main;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                main.Show();
            }
            catch (Exception ex)
            {
                // StartupAsync already logs and shuts down deterministically on failures.
                try { AppLogger.Error($"Fatal startup failure: {ex}"); } catch { }
                try { Shutdown(); } catch { }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    }
}
