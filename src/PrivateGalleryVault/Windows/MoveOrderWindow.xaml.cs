using System.Windows;
using System.Windows.Input;

namespace PrivateGalleryVault.Windows;

public partial class MoveOrderWindow : Window
{
    private readonly int _maxPosition;
    private readonly int _currentPageFirst;
    private readonly int _currentPageLast;
    private readonly int _itemsPerPage;

    public int TargetPosition { get; private set; }

    public MoveOrderWindow(string fileName, int currentPosition, int totalCount, int itemsPerPage, int currentPage)
    {
        InitializeComponent();

        _maxPosition = Math.Max(1, totalCount);
        _itemsPerPage = Math.Max(1, itemsPerPage);
        _currentPageFirst = Math.Min(_maxPosition, Math.Max(1, ((Math.Max(1, currentPage) - 1) * _itemsPerPage) + 1));
        _currentPageLast = Math.Min(_maxPosition, Math.Max(_currentPageFirst, Math.Max(1, currentPage) * _itemsPerPage));

        var currentPageOfItem = ((Math.Max(1, currentPosition) - 1) / _itemsPerPage) + 1;
        CurrentPositionText.Text = $"현재 위치: {currentPosition:N0}번째 ({currentPageOfItem:N0}페이지)";
        FileNameText.Text = fileName;
        RangeText.Text = $"1 ~ {_maxPosition:N0} 사이의 번호를 입력하세요.";
        PositionBox.Text = Math.Clamp(currentPosition, 1, _maxPosition).ToString();
        Loaded += (_, _) =>
        {
            PositionBox.Focus();
            PositionBox.SelectAll();
            UpdatePreview();
        };
    }

    private void DialogChrome_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void PositionBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Apply();
    }

    private void PositionBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdatePreview();

    private void Increase_Click(object sender, RoutedEventArgs e) => SetPosition(ReadPositionOrDefault() + 1);
    private void Decrease_Click(object sender, RoutedEventArgs e) => SetPosition(ReadPositionOrDefault() - 1);
    private void MoveFirst_Click(object sender, RoutedEventArgs e) => SetPosition(1);
    private void MoveLast_Click(object sender, RoutedEventArgs e) => SetPosition(_maxPosition);
    private void MovePageFirst_Click(object sender, RoutedEventArgs e) => SetPosition(_currentPageFirst);
    private void MovePageLast_Click(object sender, RoutedEventArgs e) => SetPosition(_currentPageLast);

    private void SetPosition(int position)
    {
        PositionBox.Text = Math.Clamp(position, 1, _maxPosition).ToString();
        PositionBox.Focus();
        PositionBox.SelectAll();
    }

    private int ReadPositionOrDefault()
    {
        return int.TryParse(PositionBox.Text.Trim().Replace(",", ""), out var value)
            ? value
            : 1;
    }

    private bool TryReadValidPosition(out int position)
    {
        position = 0;
        var raw = PositionBox.Text.Trim().Replace(",", "");
        if (!int.TryParse(raw, out var value))
        {
            ValidationText.Text = "숫자만 입력할 수 있습니다.";
            ApplyButton.IsEnabled = false;
            return false;
        }

        if (value < 1 || value > _maxPosition)
        {
            ValidationText.Text = $"1 ~ {_maxPosition:N0} 사이의 번호를 입력하세요.";
            ApplyButton.IsEnabled = false;
            return false;
        }

        position = value;
        ValidationText.Text = " ";
        ApplyButton.IsEnabled = true;
        return true;
    }

    private void UpdatePreview()
    {
        if (PreviewText == null)
            return;

        if (!TryReadValidPosition(out var position))
        {
            PreviewText.Text = "이동 후 위치 미리보기: 입력값을 확인하세요.";
            return;
        }

        var page = ((position - 1) / _itemsPerPage) + 1;
        PreviewText.Text = $"이동 후 위치 미리보기: {position:N0}번째 / {page:N0}페이지";
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => Apply();

    private void Apply()
    {
        if (!TryReadValidPosition(out var position))
            return;

        TargetPosition = position;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
