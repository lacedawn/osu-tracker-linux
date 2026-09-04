using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Circle_Tracker.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Circle_Tracker
{
    public partial class App : Application
    {
        private static readonly ILogger<App> _log = AppLogger.For<App>();

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var serviceProvider = Program.GetServiceProvider();
                if (serviceProvider != null)
                {
                    var viewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();
                    desktop.MainWindow = new MainWindow(viewModel);
                }
                else
                {
                    _log.LogError("Service provider not initialized");
                    desktop.MainWindow = new MainWindow();
                }
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
