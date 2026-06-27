using System.Linq;
using System.Windows;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Windows;

public partial class ChangePasswordWindow : Window
{
    private readonly VaultService _vaultService;

    public ChangePasswordWindow(VaultService vaultService)
    {
        InitializeComponent();
        _vaultService = vaultService;
        Loaded += (_, _) => CurrentBox.Focus();
    }

    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        var password = NewBox.Password;
        var score = 0;
        if (password.Length >= 8) score++;
        if (password.Any(char.IsLetter) && password.Any(char.IsDigit)) score++;
        if (password.Any(ch => !char.IsLetterOrDigit(ch))) score++;
        if (password.Length >= 12) score++;
        StrengthBar.Value = score;
        StrengthText.Text = $"{score} / 4";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            MessageText.Text = string.Empty;
            if (NewBox.Password != ConfirmBox.Password)
            {
                MessageText.Text = "새 비밀번호 확인이 일치하지 않습니다.";
                return;
            }

            _vaultService.ChangePassword(CurrentBox.Password, NewBox.Password);
            MessageDialog.Show(this, "비밀번호가 변경되었습니다. 다음 잠금 해제부터 새 비밀번호를 사용하세요.", "비밀번호 변경", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageText.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
