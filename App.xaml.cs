using System;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ID648;

public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _serviceProvider = ConfigureServices();
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Apply saved or system language before creating windows so resources are available
        Localization.ApplySavedOrSystemLanguage();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
        Dispatcher.BeginInvoke(mainWindow.ShowWelcomeIfNeeded,DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }

    private void App_DispatcherUnhandledException(object sender,DispatcherUnhandledExceptionEventArgs e)
    {
        ShowUnhandledErrorMessage(e.Exception,"UI");
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object? sender,UnhandledExceptionEventArgs e)
    {
        if(e.ExceptionObject is Exception exception)
        {
            ShowUnhandledErrorMessage(exception,"Application");
            return;
        }

        ShowUnhandledErrorMessage(new Exception(Localization.GetString("App.UnknownError")),"Application");
    }

    private void TaskScheduler_UnobservedTaskException(object? sender,UnobservedTaskExceptionEventArgs e)
    {
        ShowUnhandledErrorMessage(e.Exception,"BackgroundTask");
        e.SetObserved();
    }

    private static void ShowUnhandledErrorMessage(Exception exception,string source)
    {
        var messageBuilder = new StringBuilder();
        messageBuilder.AppendLine(Localization.GetString("App.UnexpectedError"));
        messageBuilder.AppendLine();
        messageBuilder.AppendLine($"{Localization.GetString("App.ErrorSourceLabel")}: {Localization.GetString($"App.Source.{source.Replace(" ",string.Empty)}")}");
        messageBuilder.AppendLine($"{Localization.GetString("App.ErrorTypeLabel")}: {exception.GetType().Name}");
        messageBuilder.AppendLine($"{Localization.GetString("App.ErrorMessageLabel")}: {exception.Message}");

        ThemedMessageDialog.Show(messageBuilder.ToString(),Localization.GetString("App.Title"),MessageBoxImage.Error);
    }
}