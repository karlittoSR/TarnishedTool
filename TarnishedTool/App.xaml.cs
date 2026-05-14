using System.Threading;
using System.Windows;

namespace TarnishedTool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        
        private static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "TarnishedTool - Karlitto";

            _mutex = new Mutex(true, appName, out var createdNew);

            if (!createdNew)
            {
                Current.Shutdown();
            }

            base.OnStartup(e);
        }    
    }
}