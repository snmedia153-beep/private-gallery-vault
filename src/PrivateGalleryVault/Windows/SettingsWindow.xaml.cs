using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Windows;

public partial class SettingsWindow : Window
{
    private readonly VaultService _vaultService;
    public AppSettings Settings { get; private set; }

    public SettingsWindow(VaultService vaultService, AppSettings settings)
    {
        InitializeComponent();
        _vaultService = vaultService;
        Settings = settings;
        VaultPathText.Text = VaultPaths.VaultRoot;
        LoadSettings();
    }

    private void LoadSettings()
    {
        foreach (var rb in new[] { Lock30, Lock60, Lock180, Lock360, Lock480 })
        {
            if (rb.Tag?.ToString() == Settings.AutoLockMinutes.ToString())
                rb.IsChecked = true;
        }
        if (Lock30.IsChecked != true && Lock60.IsChecked != true && Lock180.IsChecked != true && Lock360.IsChecked != true && Lock480.IsChecked != true)
            Lock60.IsChecked = true;

        foreach (ComboBoxItem item in ItemsPerPageCombo.Items)
        {
            if (item.Tag?.ToString() == Settings.ItemsPerPage.ToString())
            {
                ItemsPerPageCombo.SelectedItem = item;
                break;
            }
        }
        ItemsPerPageCombo.SelectedItem ??= ItemsPerPageCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == "72");

        LockOnStartupCheck.IsChecked = Settings.LockOnStartup;
        LockOnMinimizeCheck.IsChecked = Settings.LockOnMinimize;
        CleanTempOnExitCheck.IsChecked = Settings.CleanTempOnExit;
        DeleteTempAfterPlaybackCheck.IsChecked = Settings.DeleteTempMediaAfterPlayback;
        PlayVideosMutedCheck.IsChecked = Settings.PlayVideosMuted;
        SetInstantLockKeyText(Settings.InstantLockKey);
    }

    private void DragWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();
        }
        catch
        {
            // 드래그 시작 중 마우스 상태가 바뀌면 DragMove가 예외를 던질 수 있습니다.
        }
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PasswordMessageText.Text = string.Empty;
            if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
            {
                PasswordMessageText.Text = "새 비밀번호 확인이 일치하지 않습니다.";
                return;
            }
            _vaultService.ChangePassword(CurrentPasswordBox.Password, NewPasswordBox.Password);
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            PasswordMessageText.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Success"];
            PasswordMessageText.Text = "비밀번호가 변경되었습니다.";
        }
        catch (Exception ex)
        {
            PasswordMessageText.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Danger"];
            PasswordMessageText.Text = ex.Message;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.AutoLockMinutes = GetSelectedAutoLockMinutes();
        Settings.LockOnStartup = LockOnStartupCheck.IsChecked == true;
        Settings.LockOnMinimize = LockOnMinimizeCheck.IsChecked == true;
        Settings.CleanTempOnExit = CleanTempOnExitCheck.IsChecked == true;
        Settings.DeleteTempMediaAfterPlayback = DeleteTempAfterPlaybackCheck.IsChecked == true;
        Settings.PlayVideosMuted = PlayVideosMutedCheck.IsChecked == true;
        Settings.InstantLockKey = InstantLockKeyBox.Tag?.ToString() ?? string.Empty;
        Settings.ItemsPerPage = GetSelectedItemsPerPage();
        AppSettingsService.Save(Settings);
        DialogResult = true;
        Close();
    }

    private void InstantLockKeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = NormalizeKey(e);
        if (key == Key.Escape || key == Key.Delete || key == Key.Back)
        {
            SetInstantLockKeyText(string.Empty);
            return;
        }

        var modifiers = NormalizeModifiers(Keyboard.Modifiers);
        if (IsModifierKey(key))
        {
            InstantLockKeyBox.Text = "Ctrl / Alt / Shift + 다른 키를 눌러주세요";
            return;
        }

        if (key == Key.Tab || key == Key.None)
            return;

        if (modifiers == ModifierKeys.None)
        {
            InstantLockKeyBox.Tag = string.Empty;
            InstantLockKeyBox.Text = "단일 키는 사용할 수 없습니다. 예: Ctrl+X";
            return;
        }

        SetInstantLockKeyText(BuildHotkeyText(key, modifiers));
    }

    private void ClearInstantLockKey_Click(object sender, RoutedEventArgs e)
    {
        SetInstantLockKeyText(string.Empty);
        InstantLockKeyBox.Focus();
    }

    private void SetInstantLockKeyText(string? keyText)
    {
        var normalized = NormalizeHotkeyText(keyText);
        InstantLockKeyBox.Tag = normalized;
        InstantLockKeyBox.Text = string.IsNullOrWhiteSpace(normalized) ? "미설정 - 클릭 후 Ctrl+키를 누르세요" : FormatHotkeyName(normalized);
    }

    private static string NormalizeHotkeyText(string? keyText)
    {
        return TryParseInstantLockHotkey(keyText, out var key, out var modifiers)
            ? BuildHotkeyText(key, modifiers)
            : string.Empty;
    }

    private static bool TryParseInstantLockHotkey(string? text, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Control;
                continue;
            }
            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Alt;
                continue;
            }
            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Shift;
                continue;
            }

            if (Enum.TryParse<Key>(part, true, out var parsedKey))
                key = parsedKey;
        }

        return key != Key.None && !IsModifierKey(key) && modifiers != ModifierKeys.None;
    }

    private static string BuildHotkeyText(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private static string FormatHotkeyName(string hotkeyText)
    {
        var parts = hotkeyText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(FormatKeyName);
        return string.Join(" + ", parts);
    }

    private static string FormatKeyName(string keyText)
    {
        return keyText switch
        {
            "Control" => "Ctrl",
            "Return" => "Enter",
            "Escape" => "Esc",
            "Space" => "Space",
            _ => keyText
        };
    }

    private static ModifierKeys NormalizeModifiers(ModifierKeys modifiers)
    {
        return modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
    }

    private static Key NormalizeKey(KeyEventArgs e)
    {
        if (e.Key == Key.System)
            return e.SystemKey;
        if (e.Key == Key.ImeProcessed)
            return e.ImeProcessedKey;
        return e.Key;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.CapsLock;
    }

    private int GetSelectedAutoLockMinutes()
    {
        foreach (var rb in new[] { Lock30, Lock60, Lock180, Lock360, Lock480 })
        {
            if (rb.IsChecked == true && int.TryParse(rb.Tag?.ToString(), out var minutes))
                return minutes;
        }
        return 60;
    }

    private int GetSelectedItemsPerPage()
    {
        if (ItemsPerPageCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var count))
            return count;
        return 72;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
