using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.Windows;

public partial class TopicChooserWindow : Window
{
    private readonly List<Topic> _allTopics;
    private readonly ObservableCollection<Topic> _filteredTopics = [];

    public Topic? SelectedTopic => TopicList.SelectedItem as Topic;

    public TopicChooserWindow(IEnumerable<Topic> topics)
        : this(topics, 0)
    {
    }

    public TopicChooserWindow(IEnumerable<Topic> topics, int selectedMediaCount)
    {
        InitializeComponent();
        _allTopics = topics.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        TopicList.ItemsSource = _filteredTopics;
        if (selectedMediaCount > 0)
            SubtitleText.Text = $"선택한 미디어 {selectedMediaCount:N0}개를 이동할 주제를 고르세요.";
        ApplyTopicFilter();
        TopicSearchBox.Focus();
    }

    private void TopicSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyTopicFilter();

    private void ApplyTopicFilter()
    {
        var selectedId = SelectedTopic?.Id;
        var query = TopicSearchBox.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allTopics
            : _allTopics.Where(topic =>
                topic.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || (topic.Description ?? string.Empty).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || topic.ItemCount.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        _filteredTopics.Clear();
        foreach (var topic in filtered)
            _filteredTopics.Add(topic);

        if (!string.IsNullOrWhiteSpace(selectedId))
            TopicList.SelectedItem = _filteredTopics.FirstOrDefault(t => t.Id == selectedId);

        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(TopicSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrWhiteSpace(TopicSearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        SearchCountText.Text = string.IsNullOrWhiteSpace(query) ? $"{_filteredTopics.Count:N0}개" : $"{_filteredTopics.Count:N0}/{_allTopics.Count:N0}";
        EmptySearchPanel.Visibility = _filteredTopics.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        TopicSearchBox.Clear();
        TopicSearchBox.Focus();
    }

    private void TopicList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Ok();
    private void Ok_Click(object sender, RoutedEventArgs e) => Ok();

    private void Ok()
    {
        if (SelectedTopic == null)
        {
            MessageDialog.Show(this, "주제를 선택하세요.", "주제", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void DialogChrome_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
