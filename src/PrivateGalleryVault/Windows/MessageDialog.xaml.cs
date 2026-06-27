using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
    }

    public static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information)
    {
        var dlg = new MessageDialog(message, title, buttons, image);
        if (owner != null)
            dlg.Owner = owner;
        dlg.ShowDialog();
        return dlg._result == MessageBoxResult.None ? MessageBoxResult.Cancel : dlg._result;
    }

    public static string ShowOptions(Window? owner, string message, string title, MessageBoxImage image, params MessageDialogOption[] options)
    {
        var dlg = new MessageDialog(message, title, image, options);
        if (owner != null)
            dlg.Owner = owner;
        dlg.ShowDialog();
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
            // Style=null을 로컬 값으로 남기면 일부 WPF 테마에서 암시적 다크 버튼 스타일이 적용되지 않습니다.
            if (Application.Current.TryFindResource(typeof(Button)) is Style primaryStyle)
                button.Style = primaryStyle;
            button.Background = (Brush)Application.Current.Resources["Accent"];
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        }

        button.Click += (_, _) =>
        {
            _result = result;
            DialogResult = true;
            Close();
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
            DialogResult = true;
            Close();
        };
        ButtonPanel.Children.Add(button);
    }

    private void DialogChrome_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Cancel;
        DialogResult = false;
        Close();
    }
}
