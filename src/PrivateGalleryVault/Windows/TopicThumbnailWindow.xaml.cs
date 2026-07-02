using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;
using PrivateGalleryVault.ViewModels;

namespace PrivateGalleryVault.Windows;

public partial class TopicThumbnailWindow : Window
{
    private const int LoadBatchSize = 18;

    private readonly VaultContext _context;
    private readonly Topic _topic;
    private readonly ObservableCollection<MediaCardViewModel> _items = [];
    private bool _isLoading;

    public TopicThumbnailWindow(VaultContext context, Topic topic)
    {
        InitializeComponent();
        _context = context;
        _topic = topic;
        TitleText.Text = $"'{topic.Name}' 주제 썸네일 지정";
        MediaList.ItemsSource = _items;
        Loaded += async (_, _) => await LoadItemsAsync();
    }

    private async Task LoadItemsAsync()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        _items.Clear();
        EmptyPanel.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadProgressBar.IsIndeterminate = true;
        LoadProgressBar.Value = 0;
        LoadingText.Text = "미디어 목록을 준비하고 있습니다...";

        // 먼저 오버레이를 렌더링한 뒤 썸네일을 조금씩 추가합니다.
        // 파일이 많은 주제에서도 한 번에 UI 스레드를 오래 점유하지 않도록 배치 단위로 양보합니다.
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            var media = _context.Database.GetMedia(_topic.Id, null);
            if (media.Count == 0)
                media = _context.Database.GetMedia(null, null);

            var candidates = media
                .Where(i => i.Kind == MediaKind.Image || i.Kind == MediaKind.Video)
                .OrderByDescending(i => i.CreatedUtc)
                .ToList();

            if (candidates.Count == 0)
            {
                EmptyPanel.Visibility = Visibility.Visible;
                return;
            }

            LoadProgressBar.IsIndeterminate = false;
            LoadProgressBar.Maximum = candidates.Count;

            for (var i = 0; i < candidates.Count; i++)
            {
                var item = candidates[i];
                _items.Add(new MediaCardViewModel(item, _context.Media.LoadThumbnail(item)));

                LoadProgressBar.Value = i + 1;
                LoadingText.Text = $"썸네일 {i + 1:N0} / {candidates.Count:N0}개 불러오는 중...";

                if ((i + 1) % LoadBatchSize == 0)
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "주제 썸네일 목록을 불러오는 중 오류가 발생했습니다.\n\n" + ex.Message,
                "주제 썸네일", MessageBoxButton.OK, MessageBoxImage.Error);
            EmptyPanel.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _isLoading = false;
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        if (MediaList.SelectedItem is not MediaCardViewModel card)
        {
            MessageDialog.Show(this, "썸네일로 사용할 미디어를 선택하세요.", "주제 썸네일", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _context.Database.SetTopicCoverMedia(_topic.Id, card.Item.Id);
        DialogResult = true;
        Close();
    }

    private void UseDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        _context.Database.SetTopicCoverMedia(_topic.Id, null);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
