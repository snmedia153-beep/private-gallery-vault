using System.Windows;
using System.Windows.Input;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Windows;

public partial class LoginWindow : Window
{
    private readonly VaultService _vaultService;
    private bool _syncing;
    public VaultContext? Context { get; private set; }

    public LoginWindow(VaultService vaultService)
    {
        InitializeComponent();
        _vaultService = vaultService;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private string CurrentPassword => ShowPasswordCheck.IsChecked == true ? PasswordTextBox.Text : PasswordBox.Password;

    private void Unlock_Click(object sender, RoutedEventArgs e) => Unlock();

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Unlock();
    }

    private void PasswordTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Unlock();
    }

    private void Unlock()
    {
        try
        {
            MessageText.Text = string.Empty;
            Context = _vaultService.Unlock(CurrentPassword);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageText.Text = ex.Message;
            PasswordBox.SelectAll();
            PasswordBox.Focus();
        }
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ChangePasswordWindow(_vaultService) { Owner = this };
        dlg.ShowDialog();
    }

    private void ShowPasswordCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        if (ShowPasswordCheck.IsChecked == true)
        {
            PasswordTextBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordTextBox.Visibility = Visibility.Visible;
            PasswordTextBox.Focus();
            PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
        }
        else
        {
            PasswordBox.Password = PasswordTextBox.Text;
            PasswordTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordBox.Focus();
        }
        _syncing = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
