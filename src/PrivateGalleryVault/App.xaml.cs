using System.Windows;
using System.Windows.Threading;
using PrivateGalleryVault.Services;
using PrivateGalleryVault.Windows;

namespace PrivateGalleryVault;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        AppLogger.Initialize();
        RegisterGlobalExceptionHandlers();
        AppLogger.CleanupOldLogs();
        AppLogger.Info("Application startup");

        try
        {
            TempFileService.CleanPreviousSessions();
            VaultPaths.EnsureBaseDirectories();
            var vaultService = new VaultService();

            VaultContext? context = null;

            if (!vaultService.VaultExists())
            {
                var setup = new SetupVaultWindow(vaultService);
                if (setup.ShowDialog() != true || setup.Context == null)
                {
                    Shutdown();
                    return;
                }
                context = setup.Context;
            }
            else
            {
                var login = new LoginWindow(vaultService);
                if (login.ShowDialog() != true || login.Context == null)
                {
                    Shutdown();
                    return;
                }
                context = login.Context;
            }

            var main = new MainWindow(context);
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex)
        {
            AppLogger.Fatal("Application startup failed", ex);
            var crashReportPath = AppLogger.WriteCrashReport("startup-failed", ex);
            MessageDialog.Show(null, "앱 시작 중 오류가 발생했습니다.\n\n" + ex.Message + "\n\n로그 위치: " + AppLogger.LogDirectory + "\n진단 파일: " + crashReportPath, "PrivateGalleryVault", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            AppLogger.Fatal("AppDomain unhandled exception; terminating=" + args.IsTerminating, exception);
            AppLogger.WriteCrashReport("appdomain-unhandled", exception, "terminating=" + args.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error("Unobserved task exception", args.Exception);
            AppLogger.WriteCrashReport("unobserved-task", args.Exception);
            args.SetObserved();
        };
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Fatal("Dispatcher unhandled exception", e.Exception);
        var crashReportPath = AppLogger.WriteCrashReport("dispatcher-unhandled", e.Exception);
        e.Handled = true;

        MessageDialog.Show(
            null,
            "예상치 못한 오류가 발생했습니다. 앱이 즉시 종료되지 않도록 처리했고, 오류 로그와 진단 파일을 저장했습니다.\n\n" +
            e.Exception.Message + "\n\n로그 위치: " + AppLogger.LogDirectory + "\n진단 파일: " + crashReportPath,
            "PrivateGalleryVault",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("Application exit code=" + e.ApplicationExitCode);
        try
        {
            if (AppSettingsService.Load().CleanTempOnExit)
                TempFileService.CleanCurrentSession();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Temp cleanup on exit failed", ex);
        }

        base.OnExit(e);
    }

}
