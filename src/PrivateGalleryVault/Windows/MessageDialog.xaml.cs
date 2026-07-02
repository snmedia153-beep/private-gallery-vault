using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Windows;

public sealed class MessageDialogOption
{
    public string Text { get; }
    public string Result { get; }
    public bool Ghost { get; }
    public bool IsDefault { get; }
    public bool IsCancel { get; }

    public MessageDialogOption(string text, string result, bool ghost = true, bool isDefault = false, bool isCancel = false)
    {
        Text = text;
        Result = result;
        Ghost = ghost;
        IsDefault = isDefault;
        IsCancel = isCancel;
    }
}

public partial class MessageDialog : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;
    private string _customResult = string.Empty;

    private MessageDialog(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        IconText.Text = image switch
        {
            MessageBoxImage.Warning => "⚠",
            MessageBoxImage.Error => "!",
            MessageBoxImage.Question => "?",
            _ => "ⓘ"
        };
        IconText.Foreground = image switch
        {
            MessageBoxImage.Warning => (Brush)Application.Current.Resources["Accent2"],
            MessageBoxImage.Error => (Brush)Application.Current.Resources["Danger"],
            MessageBoxImage.Question => (Brush)Application.Current.Resources["Accent2"],
            _ => (Brush)Application.Current.Resources["Accent2"]
        };
        BuildButtons(buttons);
        TryAddOpenLogFolderButton(message, image);
    }

    private MessageDialog(string message, string title, MessageBoxImage image, IReadOnlyList<MessageDialogOption> options)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        IconText.Text = image switch
        {
            MessageBoxImage.Warning => "⚠",
            MessageBoxImage.Error => "!",
            MessageBoxImage.Question => "?",
            _ => "ⓘ"
        };
        IconText.Foreground = image switch
        {
            MessageBoxImage.Warning => (Brush)Application.Current.Resources["Accent2"],
            MessageBoxImage.Error => (Brush)Application.Current.Resources["Danger"],
            MessageBoxImage.Question => (Brush)Application.Current.Resources["Accent2"],
            _ => (Brush)Application.Current.Resources["Accent2"]
        };
        BuildCustomButtons(options);
        TryAddOpenLogFolderButton(message, image);
    }

    public static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information)
    {
        var dlg = new MessageDialog(message, title, buttons, image);
        if (owner != null)
            dlg.Owner = owner;

        try
        {
            dlg.ShowDialog();
        }
        catch (InvalidOperationException ex)
        {
            AppLogger.Warn($"MessageDialog.Show skipped because WPF dispatcher/window state is not modal-ready. title={title}; message={ex.Message}");
            try
            {
                if (dlg.IsVisible)
                    dlg.Close();
            }
            catch
            {
                // Ignore secondary close failures in crash/error paths.
            }
        }

        return dlg._result == MessageBoxResult.None ? MessageBoxResult.Cancel : dlg._result;
    }

    public static string ShowOptions(Window? owner, string message, string title, MessageBoxImage image, params MessageDialogOption[] options)
    {
        var dlg = new MessageDialog(message, title, image, options);
        if (owner != null)
            dlg.Owner = owner;

        try
        {
            dlg.ShowDialog();
        }
        catch (InvalidOperationException ex)
        {
            AppLogger.Warn($"MessageDialog.ShowOptions skipped because WPF dispatcher/window state is not modal-ready. title={title}; message={ex.Message}");
            try
            {
                if (dlg.IsVisible)
                    dlg.Close();
            }
            catch
            {
                // Ignore secondary close failures in crash/error paths.
            }
        }

        return string.IsNullOrWhiteSpace(dlg._customResult) ? "cancel" : dlg._customResult;
    }

    private void BuildButtons(MessageBoxButton buttons)
    {
        ButtonPanel.Children.Clear();
        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                AddButton("아니오", MessageBoxResult.No, true);
                AddButton("예", MessageBoxResult.Yes, false);
                break;
            case MessageBoxButton.OKCancel:
                AddButton("취소", MessageBoxResult.Cancel, true);
                AddButton("확인", MessageBoxResult.OK, false);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("취소", MessageBoxResult.Cancel, true);
                AddButton("아니오", MessageBoxResult.No, true);
                AddButton("예", MessageBoxResult.Yes, false);
                break;
            default:
                AddButton("확인", MessageBoxResult.OK, false);
                break;
        }
    }

    private void BuildCustomButtons(IReadOnlyList<MessageDialogOption> options)
    {
        ButtonPanel.Children.Clear();
        foreach (var option in options)
            AddCustomButton(option);
    }

    private void AddButton(string text, MessageBoxResult result, bool ghost)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 116,
            MinHeight = 38,
            Margin = new Thickness(ButtonPanel.Children.Count == 0 ? 0 : 10, 0, 0, 0),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            IsDefault = !ghost,
            IsCancel = result is MessageBoxResult.Cancel or MessageBoxResult.No
        };

        if (ghost)
        {
            button.Style = (Style)Application.Current.Resources["GhostButton"];
        }
        else
        {
            // Do not leave Style=null as a local value; that bypasses the implicit dark Button style
            // on some WPF themes and produces a light gray unreadable confirmation button.
            if (Application.Current.TryFindResource(typeof(Button)) is Style primaryStyle)
                button.Style = primaryStyle;
            button.Background = (Brush)Application.Current.Resources["Accent"];
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        }

        button.Click += (_, _) =>
        {
            _result = result;
            CloseSafely(true);
        };
        ButtonPanel.Children.Add(button);
    }

    private void AddCustomButton(MessageDialogOption option)
    {
        var button = new Button
        {
            Content = option.Text,
            MinWidth = option.Text.Length >= 7 ? 138 : 116,
            MinHeight = 38,
            Margin = new Thickness(ButtonPanel.Children.Count == 0 ? 0 : 10, 0, 0, 0),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            IsDefault = option.IsDefault,
            IsCancel = option.IsCancel
        };

        if (option.Ghost)
        {
            button.Style = (Style)Application.Current.Resources["GhostButton"];
        }
        else
        {
            if (Application.Current.TryFindResource(typeof(Button)) is Style primaryStyle)
                button.Style = primaryStyle;
            button.Background = (Brush)Application.Current.Resources["Accent"];
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        }

        button.Click += (_, _) =>
        {
            _customResult = option.Result;
            CloseSafely(true);
        };
        ButtonPanel.Children.Add(button);
    }

    private void CloseSafely(bool? dialogResult)
    {
        try
        {
            // DialogResult can be set only while the window is displayed through ShowDialog().
            // Some shutdown/error paths can make the dialog behave like a normal window,
            // so closing must never crash the app.
            DialogResult = dialogResult;
        }
        catch (InvalidOperationException ex)
        {
            AppLogger.Warn("MessageDialog closed without modal DialogResult. " + ex.Message);
        }

        if (IsVisible)
            Close();
    }

    private void TryAddOpenLogFolderButton(string message, MessageBoxImage image)
    {
        var hasLogMessage = message.Contains("로그", StringComparison.OrdinalIgnoreCase)
            || message.Contains(AppLogger.LogDirectory, StringComparison.OrdinalIgnoreCase);
        if (image != MessageBoxImage.Error && !hasLogMessage)
            return;

        var button = new Button
        {
            Content = "로그 폴더 열기",
            MinWidth = 138,
            MinHeight = 38,
            Margin = new Thickness(0, 0, 0, 0),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Style = (Style)Application.Current.Resources["GhostButton"]
        };

        button.Click += (_, _) => OpenLogFolder();
        ButtonPanel.Children.Insert(0, button);
        NormalizeButtonMargins();
    }

    private static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppLogger.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLogger.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("로그 폴더를 열 수 없습니다.\n\n" + ex.Message,
                "로그 폴더", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NormalizeButtonMargins()
    {
        for (var i = 0; i < ButtonPanel.Children.Count; i++)
        {
            if (ButtonPanel.Children[i] is FrameworkElement element)
                element.Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0);
        }
    }

    private void DialogChrome_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Cancel;
        CloseSafely(false);
    }
}
