using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Windows;

public partial class SetupVaultWindow : Window
{
    private readonly VaultService _vaultService;
    private bool _syncing;
    public VaultContext? Context { get; private set; }

    public SetupVaultWindow(VaultService vaultService)
    {
        InitializeComponent();
        _vaultService = vaultService;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private string CurrentPassword => ShowPasswordCheck.IsChecked == true ? PasswordTextBox.Text : PasswordBox.Password;
    private string CurrentConfirm => ShowPasswordCheck.IsChecked == true ? ConfirmTextBox.Text : ConfirmBox.Password;

    private Control ActivePasswordControl => ShowPasswordCheck.IsChecked == true ? PasswordTextBox : PasswordBox;
    private Control ActiveConfirmControl => ShowPasswordCheck.IsChecked == true ? ConfirmTextBox : ConfirmBox;

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var password = CurrentPassword;
            var confirm = CurrentConfirm;

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowValidationAlert("마스터 비밀번호를 입력해 주세요.", ActivePasswordControl);
                return;
            }

            if (password.Length < 8)
            {
                ShowValidationAlert("비밀번호는 최소 8자 이상으로 설정하세요.", ActivePasswordControl);
                return;
            }

            if (string.IsNullOrWhiteSpace(confirm))
            {
                ShowValidationAlert("비밀번호 확인을 입력해 주세요.", ActiveConfirmControl);
                return;
            }

            if (password != confirm)
            {
                ShowValidationAlert("비밀번호 확인이 일치하지 않습니다.", ActiveConfirmControl);
                return;
            }

            if (KeyDerivationService.LooksWeak(password))
            {
                var result = MessageDialog.Show(this, "비밀번호가 다소 약합니다. 영문/숫자/기호 조합을 권장합니다. 그래도 계속할까요?", "약한 비밀번호", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            Context = _vaultService.CreateVault(password);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, ex.Message, "Vault 생성 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        UpdateStrength();
    }

    private void VisiblePassword_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        UpdateStrength();
    }

    private void ShowPasswordCheck_Changed(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        if (ShowPasswordCheck.IsChecked == true)
        {
            PasswordTextBox.Text = PasswordBox.Password;
            ConfirmTextBox.Text = ConfirmBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            ConfirmBox.Visibility = Visibility.Collapsed;
            PasswordTextBox.Visibility = Visibility.Visible;
            ConfirmTextBox.Visibility = Visibility.Visible;
        }
        else
        {
            PasswordBox.Password = PasswordTextBox.Text;
            ConfirmBox.Password = ConfirmTextBox.Text;
            PasswordTextBox.Visibility = Visibility.Collapsed;
            ConfirmTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            ConfirmBox.Visibility = Visibility.Visible;
        }
        _syncing = false;
        UpdateStrength();
    }

    private void UpdateStrength()
    {
        var password = CurrentPassword;
        var score = 0;
        if (password.Length >= 8) score++;
        if (password.Any(char.IsLetter) && password.Any(char.IsDigit)) score++;
        if (password.Any(ch => !char.IsLetterOrDigit(ch))) score++;
        if (password.Length >= 12) score++;
        StrengthBar.Value = score;
        StrengthText.Text = $"{score} / 4";
        StrengthHintText.Text = score switch
        {
            0 => "영문, 숫자, 특수문자를 조합해 8자 이상으로 설정하세요.",
            1 => "조금 더 복잡한 비밀번호를 권장합니다.",
            2 => "사용 가능하지만 특수문자와 길이를 더하면 좋습니다.",
            3 => "좋습니다. 12자 이상이면 더 안전합니다.",
            _ => "강력한 비밀번호입니다."
        };
    }

    private void ShowValidationAlert(string message, Control focusTarget)
    {
        MessageDialog.Show(this, message, "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);

        Dispatcher.BeginInvoke(() =>
        {
            focusTarget.Focus();
            switch (focusTarget)
            {
                case TextBox textBox:
                    textBox.SelectAll();
                    break;
                case PasswordBox passwordBox:
                    passwordBox.SelectAll();
                    break;
            }
        }, DispatcherPriority.Input);
    }
}
