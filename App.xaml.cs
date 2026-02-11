using System.Configuration;
using System.Data;
using System.Windows;

namespace PLAI
{
    using PLAI.Services;
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            try { AppLogger.Info("Application starting"); } catch { }
            base.OnStartup(e);
        }
    }

}
