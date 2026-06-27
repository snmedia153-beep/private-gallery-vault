using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;
using PrivateGalleryVault.ViewModels;

namespace PrivateGalleryVault.Windows;

public partial class TopicThumbnailWindow : Window
{
    private readonly VaultContext _context;
    private readonly Topic _topic;
    private readonly ObservableCollection<MediaCardViewModel> _items = [];

    public TopicThumbnailWindow(VaultContext context, Topic topic)
    {
        InitializeComponent();
        _context = context;
        _topic = topic;
        TitleText.Text = $"'{topic.Name}' 주제 썸네일 지정";
        MediaList.ItemsSource = _items;
        Loaded += (_, _) => LoadItems();
    }

    private void LoadItems()
    {
        _items.Clear();
        var media = _context.Database.GetMedia(_topic.Id, null);
        if (media.Count == 0)
            media = _context.Database.GetMedia(null, null);

        foreach (var item in media.Where(i => i.Kind == MediaKind.Image || i.Kind == MediaKind.Video).OrderByDescending(i => i.CreatedUtc))
            _items.Add(new MediaCardViewModel(item, _context.Media.LoadThumbnail(item)));

        EmptyPanel.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
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
