using System.Windows;
using PrivateGalleryVault.Services;
using PrivateGalleryVault.Windows;

namespace PrivateGalleryVault;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        TempFileService.CleanPreviousSessions();

        try
        {
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
            MessageBox.Show("앱 시작 중 오류가 발생했습니다.\n\n" + ex.Message, "PrivateGalleryVault", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TempFileService.CleanCurrentSession();
        base.OnExit(e);
    }
}
