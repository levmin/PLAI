using System;
using System.Windows;
using PLAI.Services;

namespace PLAI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Keep a single startup log entry.
            try { AppLogger.Info("Application starting"); } catch { }

            // Surface unexpected startup failures instead of silently exiting.
            this.DispatcherUnhandledException += (_, args) =>
            {
                try { AppLogger.Error($"Unhandled UI exception: {args.Exception}"); } catch { }
                try { MessageBox.Show($"Unhandled exception:\n\n{args.Exception}", "PLAI", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
                args.Handled = true;
                try { Shutdown(); } catch { }
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                try { AppLogger.Error($"Unhandled exception: {args.ExceptionObject}"); } catch { }
            };

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    }
}
