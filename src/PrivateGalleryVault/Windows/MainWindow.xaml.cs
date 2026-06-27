using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;
using PrivateGalleryVault.Views;
using PrivateGalleryVault.ViewModels;

namespace PrivateGalleryVault.Windows;

public partial class MainWindow : Window
{
    private enum DuplicateImportChoice
    {
        Ask,
        Add,
        AddAll,
        Skip,
        SkipAll
    }

    private const string SidebarTopicSortCustom = "custom";
    private const string SidebarTopicSortLatest = "latest";
    private const string SidebarTopicSortNameDesc = "nameDesc";
    private const string SidebarTopicSortNameAsc = "nameAsc";

    private readonly VaultContext _context;
    private readonly ObservableCollection<Topic> _topics = [];
    private readonly ObservableCollection<Topic> _sidebarTopics = [];
    private readonly ObservableCollection<MediaCardViewModel> _mediaCards = [];
    private readonly ObservableCollection<TopicCardViewModel> _topicCards = [];
    private List<MediaItem> _filteredItems = [];
    private readonly DispatcherTimer _autoLockTimer;
    private MediaKind? _kindFilter = null;
    private bool _favoritesOnly;
    private bool _ignoreTopicSelection;
    private bool _ignoreTopicCardSelection;
    private bool _locking;
    private bool _instantLockHotkeyPending;
    private bool _isLockTransitionRunning;
    private AppSettings _settings;
    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private int _currentPage = 1;
    private int _itemsPerPage = 72;
    private bool _isSidebarCollapsed;
    private bool _isMediaListView;
    private bool _isTopicListView;
    private int _mediaGridColumns = 3;
    private int _topicGridColumns = 3;
    private bool _updatingGridColumnCombos;
    private bool _isOrderEditMode;
    private bool _isImporting;
    private Point _reorderDragStartPoint;
    private object? _reorderDragSource;
    private Point _externalDragStartPoint;
    private MediaCardViewModel? _externalDragSource;
    private Popup? _reorderDragPreviewPopup;
    private FrameworkElement? _reorderDragSourceElement;
    private FrameworkElement? _reorderDropTargetContainer;
    private ReorderDropCueAdorner? _reorderDropCueAdorner;
    private object? _reorderDropTargetData;
    private ReorderDropPlacement _reorderDropPlacement;
    private readonly DispatcherTimer _reorderAutoScrollTimer;
    private ListBox? _reorderAutoScrollListBox;
    private int _reorderAutoScrollDirection;
    private List<string>? _topicOrderSnapshot;
    private List<string>? _mediaOrderSnapshot;
    private int _mediaReloadVersion;
    private int _contentBusyVersion;
    private bool _isContentBusy;
    private string _sidebarTopicSortMode = SidebarTopicSortCustom;

    private static readonly Brush SidebarButtonBackground = new SolidColorBrush(Color.FromRgb(17, 29, 47));
    private static readonly Brush SidebarButtonBorder = new SolidColorBrush(Color.FromRgb(42, 58, 82));
    private static readonly Brush SidebarButtonActiveBackground = new SolidColorBrush(Color.FromRgb(19, 40, 70));
    private static readonly Brush SidebarButtonActiveBorder = new SolidColorBrush(Color.FromRgb(47, 124, 255));
    private static readonly Brush DropZoneBackground = new SolidColorBrush(Color.FromRgb(11, 23, 40));
    private static readonly Brush DropZoneBorder = new SolidColorBrush(Color.FromRgb(30, 49, 74));
    private static readonly Brush DropZoneActiveBackground = new SolidColorBrush(Color.FromRgb(12, 37, 66));
    private static readonly Brush DropZoneActiveBorder = new SolidColorBrush(Color.FromRgb(47, 124, 255));
    private static readonly Brush DropZoneInvalidBackground = new SolidColorBrush(Color.FromRgb(54, 24, 33));
    private static readonly Brush DropZoneInvalidBorder = new SolidColorBrush(Color.FromRgb(185, 65, 88));

    public MainWindow(VaultContext context)
    {
        InitializeComponent();
        _context = context;
        _settings = AppSettingsService.Load();
        _sidebarTopicSortMode = NormalizeSidebarTopicSortMode(_settings.SidebarTopicSortMode);
        _itemsPerPage = NormalizeItemsPerPage(_settings.ItemsPerPage);
        _mediaGridColumns = NormalizeGridColumns(_settings.MediaGridColumns);
        _topicGridColumns = NormalizeGridColumns(_settings.TopicGridColumns);
        InitializeGridColumnSelectors();
        ApplySidebarSectionState();
        TopicList.ItemsSource = _sidebarTopics;
        GalleryList.ItemsSource = _mediaCards;
        TopicGridList.ItemsSource = _topicCards;
        TopicLinearList.ItemsSource = _topicCards;
        ApplyGalleryViewMode();
        ApplyTopicExplorerViewMode();
        UpdateOrderEditVisual();

        _autoLockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _autoLockTimer.Tick += AutoLockTimer_Tick;
        _autoLockTimer.Start();

        _reorderAutoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(55) };
        _reorderAutoScrollTimer.Tick += ReorderAutoScrollTimer_Tick;

        PreviewMouseMove += (_, _) => MarkActivity();
        PreviewMouseDown += (_, _) => MarkActivity();
        PreviewKeyDown += (_, _) => MarkActivity();
        PreviewMouseWheel += (_, _) => MarkActivity();
        InputManager.Current.PreProcessInput += MainWindow_PreProcessInput;
        StateChanged += MainWindow_StateChanged;
        Loaded += (_, _) =>
        {
            ReloadAll();
            ShowTopicManagementView();
        };
        Closed += (_, _) =>
        {
            InputManager.Current.PreProcessInput -= MainWindow_PreProcessInput;
            _autoLockTimer.Stop();
            _reorderAutoScrollTimer.Stop();
            if (!_locking)
                _context.Dispose();
            if (_settings.CleanTempOnExit)
                TempFileService.CleanCurrentSession();
        };
        UpdateAutoLockStatus();
    }

    private Topic? SelectedTopic => TopicList.SelectedItem as Topic;
    private string? SelectedTopicId => SelectedTopic?.Id;

    private void ReloadAll()
    {
        ReloadTopics();
        ReloadMedia();
    }

    private void ReloadTopics()
    {
        var selectedId = SelectedTopicId;
        _ignoreTopicSelection = true;
        _topics.Clear();
        _topicCards.Clear();
        foreach (var t in _context.Database.GetTopics())
        {
            _topics.Add(t);
            _topicCards.Add(CreateTopicCard(t));
        }

        ApplyTopicFolderSearch(selectedId);
        _ignoreTopicSelection = false;

        SyncTopicCardSelection();
        UpdateFilterCounts();
        UpdateTopicDetail();
    }

    private void ApplyTopicFolderSearch(string? preferredSelectedId = null)
    {
        if (TopicList == null)
            return;

        var selectedId = preferredSelectedId ?? (TopicList.SelectedItem as Topic)?.Id;
        var query = TopicFolderSearchBox?.Text?.Trim() ?? string.Empty;
        var hasQuery = !string.IsNullOrWhiteSpace(query);

        _sidebarTopics.Clear();
        var visibleTopics = _topics.Where(topic => TopicMatchesSidebarSearch(topic, query));
        foreach (var topic in SortSidebarTopics(visibleTopics))
            _sidebarTopics.Add(topic);

        TopicList.SelectedItem = !string.IsNullOrWhiteSpace(selectedId)
            ? _sidebarTopics.FirstOrDefault(topic => topic.Id == selectedId)
            : null;

        if (TopicFolderSearchPlaceholder != null)
            TopicFolderSearchPlaceholder.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;

        if (TopicFolderSearchClearButton != null)
            TopicFolderSearchClearButton.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;

        if (TopicFolderSearchCountText != null)
            TopicFolderSearchCountText.Text = hasQuery ? $"{_sidebarTopics.Count}/{_topics.Count}" : string.Empty;

        ApplySidebarTopicSortVisual();
    }

    private IEnumerable<Topic> SortSidebarTopics(IEnumerable<Topic> topics)
    {
        return _sidebarTopicSortMode switch
        {
            SidebarTopicSortLatest => topics.OrderByDescending(topic => topic.CreatedUtc),
            SidebarTopicSortNameDesc => topics.OrderByDescending(topic => topic.Name, StringComparer.CurrentCultureIgnoreCase),
            SidebarTopicSortNameAsc => topics.OrderBy(topic => topic.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => topics
        };
    }

    private static string NormalizeSidebarTopicSortMode(string? mode)
    {
        return mode switch
        {
            SidebarTopicSortLatest => SidebarTopicSortLatest,
            SidebarTopicSortNameDesc => SidebarTopicSortNameDesc,
            SidebarTopicSortNameAsc => SidebarTopicSortNameAsc,
            _ => SidebarTopicSortCustom
        };
    }

    private string GetSidebarTopicSortLabel()
    {
        return _sidebarTopicSortMode switch
        {
            SidebarTopicSortLatest => "최신 등록 순",
            SidebarTopicSortNameDesc => "이름 내림차순",
            SidebarTopicSortNameAsc => "이름 오름차순",
            _ => "사용자 설정 순서"
        };
    }

    private void ApplySidebarTopicSortVisual()
    {
        if (TopicFolderSortModeText != null)
            TopicFolderSortModeText.Text = GetSidebarTopicSortLabel();

        if (TopicFolderSortButton != null)
            TopicFolderSortButton.ToolTip = $"주제 폴더 정렬: {GetSidebarTopicSortLabel()}";

        SetSortCheck(TopicFolderSortCustomCheck, _sidebarTopicSortMode == SidebarTopicSortCustom);
        SetSortCheck(TopicFolderSortLatestCheck, _sidebarTopicSortMode == SidebarTopicSortLatest);
        SetSortCheck(TopicFolderSortNameDescCheck, _sidebarTopicSortMode == SidebarTopicSortNameDesc);
        SetSortCheck(TopicFolderSortNameAscCheck, _sidebarTopicSortMode == SidebarTopicSortNameAsc);
    }

    private static void SetSortCheck(UIElement? element, bool isVisible)
    {
        if (element != null)
            element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetSidebarTopicSortMode(string mode, bool persist = true)
    {
        _sidebarTopicSortMode = NormalizeSidebarTopicSortMode(mode);
        if (persist)
        {
            _settings.SidebarTopicSortMode = _sidebarTopicSortMode;
            AppSettingsService.Save(_settings);
        }

        ApplyTopicFolderSearch(SelectedTopicId);
    }

    private void TopicFolderSortButton_Click(object sender, RoutedEventArgs e)
    {
        if (TopicFolderSortPopup == null)
            return;

        ApplySidebarTopicSortVisual();
        TopicFolderSortPopup.IsOpen = !TopicFolderSortPopup.IsOpen;
    }

    private void TopicFolderSortOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string mode })
            SetSidebarTopicSortMode(mode);

        if (TopicFolderSortPopup != null)
            TopicFolderSortPopup.IsOpen = false;
    }

    private static bool TopicMatchesSidebarSearch(Topic topic, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return ContainsIgnoreCase(topic.Name, query)
            || ContainsIgnoreCase(topic.Description, query)
            || ContainsIgnoreCase(topic.ItemCount.ToString(), query);
    }

    private static bool ContainsIgnoreCase(string? source, string query)
    {
        return !string.IsNullOrWhiteSpace(source)
            && source.Contains(query, StringComparison.OrdinalIgnoreCase);
    }


    private TopicCardViewModel CreateTopicCard(Topic topic)
    {
        ImageSource cover = LoadResourceImage("topic_default_folder.png");
        if (!string.IsNullOrWhiteSpace(topic.CoverMediaId))
        {
            try
            {
                var media = _context.Database.GetMediaById(topic.CoverMediaId);
                if (media != null)
                    cover = _context.Media.LoadThumbnail(media);
            }
            catch
            {
                cover = LoadResourceImage("topic_default_folder.png");
            }
        }
        return new TopicCardViewModel(topic, cover);
    }

    private static BitmapImage LoadResourceImage(string fileName)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri($"pack://application:,,,/Assets/Images/{fileName}", UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private List<MediaItem> BuildFilteredItems()
    {
        var items = _context.Database.GetMedia(SelectedTopicId, _kindFilter);

        if (_favoritesOnly)
            items = items.Where(item => item.Favorite).ToList();

        var query = SearchBox?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = query.Trim();
            var topicNameById = _topics.ToDictionary(topic => topic.Id, topic => topic.Name, StringComparer.OrdinalIgnoreCase);

            items = items.Where(item =>
            {
                if (item.OriginalName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (item.Extension.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                    return true;

                return topicNameById.TryGetValue(item.TopicId, out var topicName)
                       && topicName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        return items;
    }

    private async void ReloadMedia()
    {
        try
        {
            await ReloadMediaAsync();
        }
        catch (Exception ex)
        {
            EndContentBusyProgress();
            StatusText.Text = "미디어 목록을 불러오지 못했습니다.";
            MessageDialog.Show(this, "미디어 목록을 불러오는 중 오류가 발생했습니다.\n\n" + ex.Message, "미디어 목록", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ReloadMediaAsync()
    {
        var version = ++_mediaReloadVersion;
        _filteredItems = BuildFilteredItems();
        await ApplyPaginationAsync(version);
    }

    private async Task ApplyPaginationAsync(int version)
    {
        _mediaCards.Clear();
        _itemsPerPage = NormalizeItemsPerPage(_settings.ItemsPerPage);
        var totalItems = _filteredItems.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)_itemsPerPage));
        _currentPage = Math.Clamp(_currentPage, 1, totalPages);

        var pageItems = _filteredItems
            .Skip((_currentPage - 1) * _itemsPerPage)
            .Take(_itemsPerPage)
            .ToList();

        var showBusy = pageItems.Count >= 18;
        if (showBusy)
        {
            _contentBusyVersion = version;
            BeginContentBusyProgress(pageItems.Count, "미디어 목록 불러오는 중", "썸네일을 안전하게 불러오고 있습니다.", "준비 중");
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        else if (_isContentBusy)
        {
            EndContentBusyProgress();
        }

        try
        {
            var loaded = 0;
            foreach (var item in pageItems)
            {
                if (version != _mediaReloadVersion)
                    return;

                loaded++;
                if (showBusy)
                    UpdateContentBusyProgress(loaded, pageItems.Count, Path.GetFileName(item.OriginalName));

                var thumb = _context.Media.LoadThumbnail(item);
                _mediaCards.Add(new MediaCardViewModel(item, thumb));

                if (showBusy && (loaded % 3 == 0 || loaded == pageItems.Count))
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }

            if (version != _mediaReloadVersion)
                return;

            HeaderText.Text = BuildHeaderText();
            HeaderBadgeText.Text = $"{totalItems}개 항목" + (totalItems > _itemsPerPage ? $" · {_currentPage}/{totalPages} 페이지" : string.Empty);
            MediaCountText.Text = $"{totalItems}개 항목";
            EmptyMediaPanel.Visibility = totalItems == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = $"전체 {_context.Database.GetMedia(null, null).Count}개 항목  |  현재 페이지 {_mediaCards.Count}개 표시";
            UpdatePager(totalItems, totalPages);
            UpdateFilterCounts();
            UpdateTopicDetail();
            UpdateSidebarActiveState();
        }
        finally
        {
            if (showBusy && _contentBusyVersion == version)
                EndContentBusyProgress();
        }
    }

    private void UpdatePager(int totalItems, int totalPages)
    {
        var hasManyPages = totalItems > _itemsPerPage;
        PagerPanel.Visibility = totalItems == 0 ? Visibility.Collapsed : Visibility.Visible;
        PageSummaryText.Text = totalItems == 0
            ? "0개 항목"
            : $"총 {totalItems}개 · 페이지당 {_itemsPerPage}개";
        PageInfoText.Text = $"{_currentPage} / {Math.Max(1, totalPages)}";
        ItemsPerPageText.Text = $"페이지당 {_itemsPerPage}개 · 설정에서 변경";
        FirstPageButton.IsEnabled = hasManyPages && _currentPage > 1;
        PrevPageButton.IsEnabled = hasManyPages && _currentPage > 1;
        NextPageButton.IsEnabled = hasManyPages && _currentPage < totalPages;
        LastPageButton.IsEnabled = hasManyPages && _currentPage < totalPages;
    }

    private void ResetPageAndReload()
    {
        _currentPage = 1;
        ReloadMedia();
    }

    private void GoToPage(int page)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(_filteredItems.Count / (double)Math.Max(1, _itemsPerPage)));
        _currentPage = Math.Clamp(page, 1, totalPages);
        _ = ApplyPaginationAsync(++_mediaReloadVersion);
    }

    private static int NormalizeItemsPerPage(int value)
    {
        int[] allowed = [24, 48, 72, 96, 144, 200];
        return allowed.Contains(value) ? value : 72;
    }

    private static string GetKindText(MediaKind kind)
    {
        return kind switch
        {
            MediaKind.Video => "동영상",
            MediaKind.Document => "문서",
            MediaKind.Archive => "압축파일",
            MediaKind.Other => "기타 파일",
            _ => "이미지"
        };
    }

    private string BuildHeaderText()
    {
        var topic = SelectedTopic?.Name ?? "전체 항목";
        var filter = _kindFilter.HasValue ? GetKindText(_kindFilter.Value) : (_favoritesOnly ? "즐겨찾기" : "전체");
        return _kindFilter == null && !_favoritesOnly ? topic : $"{topic} · {filter}";
    }

    private void UpdateFilterCounts()
    {
        var all = _context.Database.GetMedia(null, null);
        AllCountText.Text = all.Count.ToString();
        ImagesCountText.Text = all.Count(i => i.Kind == MediaKind.Image).ToString();
        VideosCountText.Text = all.Count(i => i.Kind == MediaKind.Video).ToString();
        DocumentsCountText.Text = all.Count(i => i.Kind == MediaKind.Document).ToString();
        ArchivesCountText.Text = all.Count(i => i.Kind == MediaKind.Archive).ToString();
        OtherFilesCountText.Text = all.Count(i => i.Kind == MediaKind.Other).ToString();
        FavoritesCountText.Text = all.Count(i => i.Favorite).ToString();
    }

    private void UpdateSidebarActiveState()
    {
        var mediaVisible = MediaView.Visibility == Visibility.Visible;
        var topicManagerVisible = TopicManagementView.Visibility == Visibility.Visible;
        var noTopicSelected = SelectedTopic == null;

        SetSidebarButtonActive(AllButton, mediaVisible && noTopicSelected && _kindFilter == null && !_favoritesOnly);
        SetSidebarButtonActive(ImagesButton, mediaVisible && noTopicSelected && _kindFilter == MediaKind.Image && !_favoritesOnly);
        SetSidebarButtonActive(VideosButton, mediaVisible && noTopicSelected && _kindFilter == MediaKind.Video && !_favoritesOnly);
        SetSidebarButtonActive(DocumentsButton, mediaVisible && noTopicSelected && _kindFilter == MediaKind.Document && !_favoritesOnly);
        SetSidebarButtonActive(ArchivesButton, mediaVisible && noTopicSelected && _kindFilter == MediaKind.Archive && !_favoritesOnly);
        SetSidebarButtonActive(OtherFilesButton, mediaVisible && noTopicSelected && _kindFilter == MediaKind.Other && !_favoritesOnly);
        SetSidebarButtonActive(FavoritesButton, mediaVisible && noTopicSelected && _favoritesOnly);
        SetSidebarButtonActive(ManageTopicsButton, topicManagerVisible);

        SetSidebarButtonActive(CollapsedAllButton, mediaVisible && noTopicSelected && _kindFilter == null && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedImagesButton, mediaVisible && noTopicSelected && _kindFilter == MediaKind.Image && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedVideosButton, mediaVisible && noTopicSelected && _kindFilter == MediaKind.Video && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedDocumentsButton, mediaVisible && noTopicSelected && _kindFilter == MediaKind.Document && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedArchivesButton, mediaVisible && noTopicSelected && _kindFilter == MediaKind.Archive && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedOtherFilesButton, mediaVisible && noTopicSelected && _kindFilter == MediaKind.Other && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedFavoritesButton, mediaVisible && noTopicSelected && _favoritesOnly);
        SetSidebarButtonActive(CollapsedManageTopicsButton, topicManagerVisible);
    }

    private static void SetSidebarButtonActive(Button button, bool active)
    {
        button.Background = active ? SidebarButtonActiveBackground : SidebarButtonBackground;
        button.BorderBrush = active ? SidebarButtonActiveBorder : SidebarButtonBorder;
        button.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
    }

    private void UpdateTopicDetail()
    {
        var topic = SelectedTopic;
        UpdateDropZoneHint(topic);
        if (topic == null)
        {
            SelectedTopicTitleText.Text = "전체 미디어";
            MediaCountText.Text = $"{_mediaCards.Count}개 항목";
            TopicDescriptionText.Text = "주제 폴더 보기에서 원하는 폴더를 열거나 새 주제를 추가하세요.";
            TopicCoverImage.Source = LoadResourceImage("topic_default_folder.png");
            return;
        }

        SelectedTopicTitleText.Text = topic.Name;
        MediaCountText.Text = $"{topic.ItemCount}개 항목";
        TopicDescriptionText.Text = string.IsNullOrWhiteSpace(topic.Description) ? "설명이 없습니다." : topic.Description;
        TopicCoverImage.Source = CreateTopicCard(topic).CoverImage;
    }

    private void UpdateDropZoneHint(Topic? topic = null)
    {
        if (DropZoneTitleText == null || DropZoneSubtitleText == null || DropZoneFallbackText == null)
            return;

        topic ??= SelectedTopic;
        DropZoneTitleText.Text = "파일 드래그&드롭 추가";
        DropZoneSubtitleText.Text = topic == null ? "저장 위치: 미분류" : $"저장 위치: {topic.Name}";
        DropZoneFallbackText.Text = topic == null ? "주제 미선택 상태라 미분류에 저장됩니다" : "파일·폴더를 놓으면 선택한 주제에 저장됩니다";
        ResetDropZoneVisual();
    }

    private void ResetDropZoneVisual()
    {
        SidebarDropZone.Background = DropZoneBackground;
        SidebarDropZone.BorderBrush = DropZoneBorder;
    }

    private void SetDropZoneDragState(bool isValid)
    {
        SidebarDropZone.Background = isValid ? DropZoneActiveBackground : DropZoneInvalidBackground;
        SidebarDropZone.BorderBrush = isValid ? DropZoneActiveBorder : DropZoneInvalidBorder;
        DropZoneTitleText.Text = isValid ? "놓으면 보관함에 추가" : "지원 파일만 추가 가능";
        DropZoneFallbackText.Text = isValid
            ? "현재 대상 주제로 암호화 저장됩니다"
            : "파일 또는 폴더만 드래그할 수 있습니다";
    }

    private void SyncTopicCardSelection()
    {
        _ignoreTopicCardSelection = true;
        if (SelectedTopic == null)
        {
            TopicGridList.SelectedItem = null;
            TopicLinearList.SelectedItem = null;
            _ignoreTopicCardSelection = false;
            return;
        }
        var card = _topicCards.FirstOrDefault(c => c.Id == SelectedTopic.Id);
        TopicGridList.SelectedItem = card;
        TopicLinearList.SelectedItem = card;
        _ignoreTopicCardSelection = false;
    }

    private void ClearTopicSelection()
    {
        _ignoreTopicSelection = true;
        TopicList.SelectedItem = null;
        _ignoreTopicSelection = false;
        SyncTopicCardSelection();
        UpdateSidebarActiveState();
    }

    private void ShowMediaView()
    {
        if (ManagementOverlay != null)
            ManagementOverlay.Visibility = Visibility.Collapsed;
        MediaView.Visibility = Visibility.Visible;
        TopicManagementView.Visibility = Visibility.Collapsed;
        UpdateOrderEditVisual();
        UpdateSidebarActiveState();
    }

    private void ShowTopicManagementView()
    {
        // 주제 탐색 화면은 특정 주제를 미리 열지 않고 폴더 루트처럼 동작합니다.
        // 시작 화면과 사이드바의 '주제 폴더 보기' 동작이 전체 주제를 보여주도록 유지합니다.
        _kindFilter = null;
        _favoritesOnly = false;
        ClearTopicSelection();
        if (ManagementOverlay != null)
            ManagementOverlay.Visibility = Visibility.Collapsed;
        MediaView.Visibility = Visibility.Collapsed;
        TopicManagementView.Visibility = Visibility.Visible;
        UpdateOrderEditVisual();
        ReloadTopics();
        StatusText.Text = $"주제 {_topicCards.Count}개 표시  |  폴더를 클릭하면 해당 미디어를 엽니다.";
        UpdateSidebarActiveState();
    }

    private void TopicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreTopicSelection) return;
        if (_isOrderEditMode)
        {
            SyncTopicCardSelection();
            UpdateTopicDetail();
            return;
        }
        _kindFilter = null;
        _favoritesOnly = false;
        ShowMediaView();
        SyncTopicCardSelection();
        ResetPageAndReload();
    }

    private void TopicList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var item = FindAncestor<ListBoxItem>(source);
        if (item == null)
            return;

        item.IsSelected = true;
        item.Focus();
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void TopicCard_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreTopicCardSelection) return;
        if (sender is not ListBox lb || lb.SelectedItem is not TopicCardViewModel card)
            return;

        if (_isOrderEditMode)
        {
            _ignoreTopicSelection = true;
            TopicList.SelectedItem = _topics.FirstOrDefault(t => t.Id == card.Id);
            _ignoreTopicSelection = false;
            UpdateTopicDetail();
            return;
        }

        _ignoreTopicSelection = true;
        _ignoreTopicCardSelection = true;
        TopicList.SelectedItem = _topics.FirstOrDefault(t => t.Id == card.Id);
        TopicGridList.SelectedItem = card;
        TopicLinearList.SelectedItem = card;
        _ignoreTopicCardSelection = false;
        _ignoreTopicSelection = false;

        // 탐색기처럼 주제 폴더를 클릭하면 해당 주제의 미디어 목록을 바로 엽니다.
        _kindFilter = null;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }


    private void ToggleOrderEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_isOrderEditMode)
        {
            DoneOrderEdit_Click(sender, e);
            return;
        }

        EnterOrderEditMode("순서 편집 모드: 핸들을 드래그하여 위치를 변경합니다.");
    }

    private void EnterOrderEditMode(string? statusMessage = null)
    {
        if (!_isOrderEditMode)
        {
            _isOrderEditMode = true;
            CaptureOrderSnapshots();
        }

        UpdateOrderEditVisual();
        StatusText.Text = statusMessage ?? "순서 편집 모드: 항목을 드래그하여 위치를 바꾸고 자동 저장합니다.";
    }

    private void DoneOrderEdit_Click(object sender, RoutedEventArgs e)
    {
        _isOrderEditMode = false;
        _topicOrderSnapshot = null;
        _mediaOrderSnapshot = null;
        ClearReorderDropCue();
        StopReorderAutoScroll();
        UpdateOrderEditVisual();
        StatusText.Text = "순서 편집 완료: 현재 순서가 저장되었습니다.";
    }

    private void UndoOrderEdit_Click(object sender, RoutedEventArgs e)
    {
        var restored = false;

        if (_topicOrderSnapshot is { Count: > 0 })
        {
            _context.Database.UpdateTopicSortOrders(_topicOrderSnapshot);
            restored = true;
        }

        if (_mediaOrderSnapshot is { Count: > 0 })
        {
            _context.Database.UpdateMediaSortOrders(_mediaOrderSnapshot);
            restored = true;
        }

        if (!restored)
        {
            StatusText.Text = "되돌릴 정렬 변경이 없습니다.";
            return;
        }

        ReloadAll();
        CaptureOrderSnapshots(force: true);
        UpdateOrderEditVisual();
        StatusText.Text = "정렬 변경을 편집 시작 시점으로 되돌렸습니다.";
    }

    private void ResetOrderEdit_Click(object sender, RoutedEventArgs e)
    {
        if (!_isOrderEditMode)
            EnterOrderEditMode();

        if (TopicManagementView.Visibility == Visibility.Visible)
        {
            var topicIds = _topics.OrderBy(t => t.CreatedUtc).Select(t => t.Id).ToList();
            if (topicIds.Count > 0)
                _context.Database.UpdateTopicSortOrders(topicIds);
            ReloadTopics();
            StatusText.Text = "주제 순서를 생성일 기준 기본 순서로 복원했습니다.";
            return;
        }

        var mediaIds = _filteredItems
            .OrderByDescending(i => i.CreatedUtc)
            .Select(i => i.Id)
            .ToList();
        if (mediaIds.Count > 0)
            _context.Database.UpdateMediaSortOrders(mediaIds);
        ReloadMedia();
        StatusText.Text = "현재 미디어/파일 순서를 최신순 기본 정렬로 복원했습니다.";
    }

    private void CaptureOrderSnapshots(bool force = false)
    {
        if (force || _topicOrderSnapshot == null)
            _topicOrderSnapshot = _topics.Select(t => t.Id).ToList();

        if (force || _mediaOrderSnapshot == null)
            _mediaOrderSnapshot = _filteredItems.Select(i => i.Id).ToList();
    }

    private void UpdateOrderEditVisual()
    {
        if (MediaOrderEditButton == null || TopicOrderEditButton == null)
            return;

        SetSidebarButtonActive(MediaOrderEditButton, _isOrderEditMode);
        SetSidebarButtonActive(TopicOrderEditButton, _isOrderEditMode);
        MediaOrderEditButton.Content = _isOrderEditMode ? "✓ 순서 편집 중" : "✎ 순서 편집";
        TopicOrderEditButton.Content = _isOrderEditMode ? "✓ 순서 편집 중" : "✎ 순서 편집";

        MediaOrderEditBanner.Visibility = _isOrderEditMode && MediaView.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        TopicOrderEditBanner.Visibility = _isOrderEditMode && TopicManagementView.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!_isOrderEditMode)
        {
            MediaAutoScrollTopHint.Visibility = Visibility.Collapsed;
            MediaAutoScrollBottomHint.Visibility = Visibility.Collapsed;
            TopicAutoScrollTopHint.Visibility = Visibility.Collapsed;
            TopicAutoScrollBottomHint.Visibility = Visibility.Collapsed;
        }
    }

    private void ReorderHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        var dataContext = element.DataContext;
        if (dataContext is not Topic && dataContext is not TopicCardViewModel && dataContext is not MediaCardViewModel)
            return;

        if (!_isOrderEditMode)
            EnterOrderEditMode("순서 편집 모드: 핸들을 드래그하여 위치를 변경합니다.");

        _reorderDragStartPoint = e.GetPosition(null);
        _reorderDragSource = dataContext;
        e.Handled = true;
    }

    private void ReorderHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _reorderDragSource == null)
            return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _reorderDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _reorderDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (sender is not DependencyObject dragSource)
            return;

        var data = _reorderDragSource;
        _reorderDragSource = null;
        ExecuteReorderDrag(dragSource, data, FindAncestor<ListBoxItem>(dragSource));
        e.Handled = true;
    }

    private void ReorderList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _reorderDragStartPoint = e.GetPosition(null);
        _externalDragStartPoint = e.GetPosition(null);
        _reorderDragSource = null;
        _externalDragSource = null;

        if (e.OriginalSource is not DependencyObject source)
            return;

        var dataContext = FindAncestor<ListBoxItem>(source)?.DataContext;
        if (_isOrderEditMode)
        {
            _reorderDragSource = dataContext;
            return;
        }

        if (ReferenceEquals(sender, GalleryList) && dataContext is MediaCardViewModel mediaCard)
            _externalDragSource = mediaCard;
    }

    private void ReorderTopicList_PreviewMouseMove(object sender, MouseEventArgs e) => StartReorderDrag(sender, e, typeof(Topic));
    private void ReorderTopicCardList_PreviewMouseMove(object sender, MouseEventArgs e) => StartReorderDrag(sender, e, typeof(TopicCardViewModel));

    private void ReorderMediaList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isOrderEditMode)
        {
            StartReorderDrag(sender, e, typeof(MediaCardViewModel));
            return;
        }

        StartExternalMediaDrag(e);
    }

    private void StartExternalMediaDrag(MouseEventArgs e)
    {
        if (_isImporting || e.LeftButton != MouseButtonState.Pressed || _externalDragSource == null)
            return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _externalDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _externalDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var source = _externalDragSource;
        _externalDragSource = null;
        BeginExternalMediaDrag(source);
        e.Handled = true;
    }

    private void BeginExternalMediaDrag(MediaCardViewModel source)
    {
        try
        {
            Cursor = Cursors.Wait;
            StatusText.Text = "외부 드래그 내보내기 준비 중: " + source.Title;

            var selectedCards = GalleryList.SelectedItems.Cast<MediaCardViewModel>().ToList();
            if (!selectedCards.Any(card => card.Id == source.Id))
                selectedCards = [source];

            var fileDropList = new StringCollection();
            foreach (var card in selectedCards)
            {
                var tempPath = _context.Media.ExportMediaToExternalDragTemp(card.Item);
                fileDropList.Add(tempPath);
            }

            var data = new DataObject();
            data.SetFileDropList(fileDropList);
            data.SetData(DataFormats.FileDrop, fileDropList.Cast<string>().ToArray());

            Cursor = null;
            StatusText.Text = selectedCards.Count == 1
                ? "탐색기 폴더에 놓으면 복호화된 파일이 복사됩니다."
                : $"선택한 {selectedCards.Count}개 파일을 탐색기 폴더에 놓으면 복사됩니다.";

            DragDrop.DoDragDrop(GalleryList, data, DragDropEffects.Copy);
            StatusText.Text = "외부 드래그 내보내기 완료";
            _context.ActivityLogs.Add("export", "외부 드래그 내보내기", $"{selectedCards.Count}개 파일");
        }
        catch (Exception ex)
        {
            Cursor = null;
            MessageDialog.Show(this, "외부로 드래그할 파일을 준비할 수 없습니다.\n\n" + ex.Message, "외부 드래그 내보내기", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = null;
        }
    }

    private void StartReorderDrag(object sender, MouseEventArgs e, Type expectedType)
    {
        if (!_isOrderEditMode || e.LeftButton != MouseButtonState.Pressed || _reorderDragSource == null)
            return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _reorderDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _reorderDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (!expectedType.IsInstanceOfType(_reorderDragSource))
            return;

        var sourceElement = Mouse.DirectlyOver as DependencyObject;
        ExecuteReorderDrag((DependencyObject)sender, _reorderDragSource, FindAncestor<ListBoxItem>(sourceElement!));
        _reorderDragSource = null;
    }

    private void ExecuteReorderDrag(DependencyObject dragSource, object data, FrameworkElement? sourceContainer)
    {
        BeginReorderDragVisual(data, sourceContainer);
        try
        {
            DragDrop.DoDragDrop(dragSource, data, DragDropEffects.Move);
        }
        finally
        {
            EndReorderDragVisual();
            _reorderDragSource = null;
        }
    }

    private void BeginReorderDragVisual(object data, FrameworkElement? sourceContainer)
    {
        EndReorderDragVisual();
        ApplyReorderSourceDimming(sourceContainer, true);

        _reorderDragPreviewPopup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = this,
            StaysOpen = true,
            IsHitTestVisible = false,
            HorizontalOffset = 0,
            VerticalOffset = 0,
            Child = BuildReorderDragPreview(data),
            IsOpen = true
        };

        UpdateReorderDragPreview(Mouse.GetPosition(this));
    }

    private void EndReorderDragVisual()
    {
        ClearReorderDropCue();
        StopReorderAutoScroll();
        ApplyReorderSourceDimming(null, false);

        if (_reorderDragPreviewPopup != null)
        {
            _reorderDragPreviewPopup.IsOpen = false;
            _reorderDragPreviewPopup.Child = null;
            _reorderDragPreviewPopup = null;
        }
    }

    private void UpdateReorderDragPreview(Point position)
    {
        if (_reorderDragPreviewPopup == null)
            return;

        _reorderDragPreviewPopup.HorizontalOffset = position.X + 18;
        _reorderDragPreviewPopup.VerticalOffset = position.Y + 18;
    }

    private Border BuildReorderDragPreview(object data)
    {
        var (label, title, subtitle, imageSource) = DescribeReorderData(data);

        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12),
            MaxWidth = 320,
            Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                Opacity = 0.35,
                ShadowDepth = 0,
                Color = Colors.Black
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = imageSource == null ? new GridLength(0) : new GridLength(58) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = imageSource == null ? new GridLength(1, GridUnitType.Star) : new GridLength(1, GridUnitType.Star) });

        if (imageSource != null)
        {
            var previewFrame = new Border
            {
                Width = 58,
                Height = 46,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(5, 11, 20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(37, 53, 79)),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Child = CreatePreviewImage(imageSource)
            };
            Grid.SetColumn(previewFrame, 0);
            grid.Children.Add(previewFrame);
        }

        var infoPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = imageSource == null ? new Thickness(0) : new Thickness(12, 0, 0, 0)
        };

        var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
        chipRow.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(29, 78, 216)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            }
        });
        chipRow.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 34, 58)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(6, 0, 0, 0),
            Child = new TextBlock
            {
                Text = "순서 변경",
                Foreground = new SolidColorBrush(Color.FromRgb(191, 219, 254)),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            }
        });
        infoPanel.Children.Add(chipRow);
        infoPanel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 11.5,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(infoPanel);
        root.Child = grid;
        return root;
    }


    private static Image CreatePreviewImage(ImageSource imageSource)
    {
        var image = new Image
        {
            Source = imageSource,
            Stretch = Stretch.Uniform
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private (string Label, string Title, string Subtitle, ImageSource? ImageSource) DescribeReorderData(object data)
    {
        return data switch
        {
            MediaCardViewModel media => (media.Badge, media.Title, media.DetailText, media.Thumbnail),
            TopicCardViewModel topicCard => ("주제", topicCard.Name, topicCard.CountText, topicCard.CoverImage),
            Topic topic =>
                ("주제", topic.Name, $"{topic.ItemCount}개 항목", _topicCards.FirstOrDefault(card => card.Id == topic.Id)?.CoverImage),
            _ => ("이동", data.ToString() ?? "항목", "드롭할 위치를 파란 가이드로 미리 보여줍니다.", null)
        };
    }

    private void ApplyReorderSourceDimming(FrameworkElement? element, bool isDragging)
    {
        if (_reorderDragSourceElement != null)
            _reorderDragSourceElement.Opacity = 1.0;

        _reorderDragSourceElement = null;

        if (!isDragging || element == null)
            return;

        _reorderDragSourceElement = element;
        _reorderDragSourceElement.Opacity = 0.42;
    }

    private void ReorderTopicList_DragOver(object sender, DragEventArgs e) => HandleReorderDragOver<Topic>(sender, e, ReorderSurfaceVisualKind.List);
    private void ReorderTopicCardList_DragOver(object sender, DragEventArgs e) => HandleReorderDragOver<TopicCardViewModel>(sender, e, GetListSurfaceKind(sender));
    private void ReorderMediaList_DragOver(object sender, DragEventArgs e) => HandleReorderDragOver<MediaCardViewModel>(sender, e, GetListSurfaceKind(sender));

    private void HandleReorderDragOver<T>(object sender, DragEventArgs e, ReorderSurfaceVisualKind surfaceKind) where T : class
    {
        UpdateReorderDragPreview(e.GetPosition(this));

        if (!_isOrderEditMode || sender is not ListBox listBox || !e.Data.GetDataPresent(typeof(T)))
        {
            ClearReorderDropCue();
            StopReorderAutoScroll();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        UpdateReorderAutoScroll(listBox, e.GetPosition(listBox));
        UpdateReorderDropCue<T>(listBox, e, surfaceKind);
        e.Handled = true;
    }

    private void ReorderList_DragLeave(object sender, DragEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<ListBoxItem>(source) != null)
            return;

        ClearReorderDropCue();
        StopReorderAutoScroll();
        e.Handled = true;
    }

    private void UpdateReorderDropCue<T>(ListBox listBox, DragEventArgs e, ReorderSurfaceVisualKind surfaceKind) where T : class
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            ClearReorderDropCue();
            return;
        }

        var container = FindAncestor<ListBoxItem>(source);
        ReorderDropPlacement placement;

        if (container != null && container.DataContext is T targetData)
        {
            placement = GetDropPlacement(container, e.GetPosition(container), surfaceKind);
            if (ReferenceEquals(_reorderDropTargetContainer, container) && Equals(_reorderDropTargetData, targetData) && _reorderDropPlacement == placement)
                return;

            ClearReorderDropCue();
            var layer = AdornerLayer.GetAdornerLayer(container);
            if (layer == null)
                return;

            _reorderDropCueAdorner = new ReorderDropCueAdorner(container, placement, surfaceKind);
            layer.Add(_reorderDropCueAdorner);
            _reorderDropTargetContainer = container;
            _reorderDropTargetData = targetData;
            _reorderDropPlacement = placement;
            return;
        }

        if (listBox.Items.Count == 0)
        {
            ClearReorderDropCue();
            return;
        }

        container = listBox.ItemContainerGenerator.ContainerFromIndex(listBox.Items.Count - 1) as ListBoxItem;
        if (container?.DataContext is not T lastData)
        {
            ClearReorderDropCue();
            return;
        }

        placement = ReorderDropPlacement.After;
        if (ReferenceEquals(_reorderDropTargetContainer, container) && Equals(_reorderDropTargetData, lastData) && _reorderDropPlacement == placement)
            return;

        ClearReorderDropCue();
        var fallbackLayer = AdornerLayer.GetAdornerLayer(container);
        if (fallbackLayer == null)
            return;

        _reorderDropCueAdorner = new ReorderDropCueAdorner(container, placement, surfaceKind);
        fallbackLayer.Add(_reorderDropCueAdorner);
        _reorderDropTargetContainer = container;
        _reorderDropTargetData = lastData;
        _reorderDropPlacement = placement;
    }

    private void ClearReorderDropCue()
    {
        if (_reorderDropCueAdorner != null && _reorderDropTargetContainer != null)
        {
            var layer = AdornerLayer.GetAdornerLayer(_reorderDropTargetContainer);
            layer?.Remove(_reorderDropCueAdorner);
        }

        _reorderDropCueAdorner = null;
        _reorderDropTargetContainer = null;
        _reorderDropTargetData = null;
        _reorderDropPlacement = ReorderDropPlacement.Before;
    }

    private void UpdateReorderAutoScroll(ListBox listBox, Point position)
    {
        const double edgeSize = 72;
        var direction = 0;

        if (position.Y < edgeSize)
            direction = -1;
        else if (position.Y > listBox.ActualHeight - edgeSize)
            direction = 1;

        _reorderAutoScrollListBox = direction == 0 ? null : listBox;
        _reorderAutoScrollDirection = direction;

        UpdateReorderAutoScrollHints(listBox, direction);

        if (direction == 0)
        {
            _reorderAutoScrollTimer.Stop();
            return;
        }

        if (!_reorderAutoScrollTimer.IsEnabled)
            _reorderAutoScrollTimer.Start();
    }

    private void ReorderAutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_reorderAutoScrollListBox == null || _reorderAutoScrollDirection == 0)
        {
            StopReorderAutoScroll();
            return;
        }

        var scrollViewer = FindDescendant<ScrollViewer>(_reorderAutoScrollListBox);
        if (scrollViewer == null)
            return;

        var nextOffset = Math.Clamp(scrollViewer.VerticalOffset + (_reorderAutoScrollDirection * 22), 0, scrollViewer.ScrollableHeight);
        scrollViewer.ScrollToVerticalOffset(nextOffset);
    }

    private void StopReorderAutoScroll()
    {
        _reorderAutoScrollTimer.Stop();
        _reorderAutoScrollListBox = null;
        _reorderAutoScrollDirection = 0;
        MediaAutoScrollTopHint.Visibility = Visibility.Collapsed;
        MediaAutoScrollBottomHint.Visibility = Visibility.Collapsed;
        TopicAutoScrollTopHint.Visibility = Visibility.Collapsed;
        TopicAutoScrollBottomHint.Visibility = Visibility.Collapsed;
    }

    private void UpdateReorderAutoScrollHints(ListBox listBox, int direction)
    {
        MediaAutoScrollTopHint.Visibility = Visibility.Collapsed;
        MediaAutoScrollBottomHint.Visibility = Visibility.Collapsed;
        TopicAutoScrollTopHint.Visibility = Visibility.Collapsed;
        TopicAutoScrollBottomHint.Visibility = Visibility.Collapsed;

        if (direction == 0)
            return;

        if (ReferenceEquals(listBox, GalleryList))
        {
            MediaAutoScrollTopHint.Visibility = direction < 0 ? Visibility.Visible : Visibility.Collapsed;
            MediaAutoScrollBottomHint.Visibility = direction > 0 ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (ReferenceEquals(listBox, TopicGridList) || ReferenceEquals(listBox, TopicLinearList))
        {
            TopicAutoScrollTopHint.Visibility = direction < 0 ? Visibility.Visible : Visibility.Collapsed;
            TopicAutoScrollBottomHint.Visibility = direction > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static T? FindDescendant<T>(DependencyObject current) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
        {
            var child = VisualTreeHelper.GetChild(current, i);
            if (child is T match)
                return match;

            var nested = FindDescendant<T>(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private ReorderSurfaceVisualKind GetListSurfaceKind(object sender)
    {
        if (ReferenceEquals(sender, TopicLinearList) || ReferenceEquals(sender, TopicList))
            return ReorderSurfaceVisualKind.List;

        if (ReferenceEquals(sender, GalleryList))
            return _isMediaListView ? ReorderSurfaceVisualKind.List : ReorderSurfaceVisualKind.Grid;

        return _isTopicListView ? ReorderSurfaceVisualKind.List : ReorderSurfaceVisualKind.Grid;
    }

    private static ReorderDropPlacement GetDropPlacement(FrameworkElement container, Point point, ReorderSurfaceVisualKind surfaceKind)
    {
        return surfaceKind == ReorderSurfaceVisualKind.List
            ? (point.Y <= container.ActualHeight / 2 ? ReorderDropPlacement.Before : ReorderDropPlacement.After)
            : (point.X <= container.ActualWidth / 2 ? ReorderDropPlacement.Before : ReorderDropPlacement.After);
    }

    private void ReorderTopicList_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!_isOrderEditMode || !e.Data.GetDataPresent(typeof(Topic)))
                return;

            var source = (Topic)e.Data.GetData(typeof(Topic))!;
            if (TryGetCurrentDropTarget<Topic>(out var target, out var placement))
                MoveTopicOrder(source.Id, target.Id, placement);
        }
        finally
        {
            ClearReorderDropCue();
            e.Handled = true;
        }
    }

    private void ReorderTopicCardList_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!_isOrderEditMode || !e.Data.GetDataPresent(typeof(TopicCardViewModel)))
                return;

            var source = (TopicCardViewModel)e.Data.GetData(typeof(TopicCardViewModel))!;
            if (TryGetCurrentDropTarget<TopicCardViewModel>(out var target, out var placement))
                MoveTopicOrder(source.Id, target.Id, placement);
        }
        finally
        {
            ClearReorderDropCue();
            e.Handled = true;
        }
    }

    private void ReorderMediaList_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!_isOrderEditMode || !e.Data.GetDataPresent(typeof(MediaCardViewModel)))
                return;

            var source = (MediaCardViewModel)e.Data.GetData(typeof(MediaCardViewModel))!;
            if (TryGetCurrentDropTarget<MediaCardViewModel>(out var target, out var placement))
                MoveMediaOrder(source.Item.Id, target.Item.Id, placement);
        }
        finally
        {
            ClearReorderDropCue();
            e.Handled = true;
        }
    }

    private bool TryGetCurrentDropTarget<T>(out T? target, out ReorderDropPlacement placement) where T : class
    {
        target = _reorderDropTargetData as T;
        placement = _reorderDropPlacement;
        return target != null;
    }

    private void MoveTopicOrder(string sourceId, string targetId, ReorderDropPlacement placement)
    {
        if (sourceId == targetId) return;
        var ids = _topics.Select(t => t.Id).ToList();
        MoveId(ids, sourceId, targetId, placement);
        _context.Database.UpdateTopicSortOrders(ids);
        _sidebarTopicSortMode = SidebarTopicSortCustom;
        _settings.SidebarTopicSortMode = _sidebarTopicSortMode;
        AppSettingsService.Save(_settings);
        ReloadTopics();
        StatusText.Text = "주제 순서가 저장되었습니다. 사이드바 정렬이 사용자 설정 순서로 전환되었습니다.";
    }

    private void MoveMediaOrder(string sourceId, string targetId, ReorderDropPlacement placement)
    {
        if (sourceId == targetId) return;
        var ids = _filteredItems.Select(i => i.Id).ToList();
        MoveId(ids, sourceId, targetId, placement);
        _context.Database.UpdateMediaSortOrders(ids);
        ReloadMedia();
        StatusText.Text = "미디어/파일 순서가 저장되었습니다.";
    }

    private static void MoveId(List<string> ids, string sourceId, string targetId, ReorderDropPlacement placement)
    {
        var from = ids.IndexOf(sourceId);
        var to = ids.IndexOf(targetId);
        if (from < 0 || to < 0 || from == to) return;

        var id = ids[from];
        ids.RemoveAt(from);

        if (from < to)
            to--;

        var insertIndex = placement == ReorderDropPlacement.After ? to + 1 : to;
        insertIndex = Math.Max(0, Math.Min(insertIndex, ids.Count));
        ids.Insert(insertIndex, id);
    }


    private void ShowManagementView(string title, string subtitle, UserControl view)
    {
        ClearReorderDropCue();
        _isOrderEditMode = false;
        UpdateOrderEditVisual();
        MediaView.Visibility = Visibility.Collapsed;
        TopicManagementView.Visibility = Visibility.Collapsed;
        ManagementTitleText.Text = title;
        ManagementSubtitleText.Text = subtitle;
        ManagementContentHost.Content = view;
        ManagementOverlay.Visibility = Visibility.Visible;
        SetSidebarButtonActive(ActivityCenterButton, view is ActivityLogView);
        SetSidebarButtonActive(DuplicateCenterButton, view is DuplicateManagerView);
        SetSidebarButtonActive(BackupCenterButton, view is BackupRestoreWizardView);
        SetSidebarButtonActive(TagCenterButton, view is TagManagerView);
        StatusText.Text = title + " 화면을 열었습니다.";
    }

    private void ActivityCenter_Click(object sender, RoutedEventArgs e)
    {
        ShowManagementView("최근 작업 기록", "가져오기, 변경, 내보내기, 중복 처리 기록을 타임라인으로 확인합니다.", new ActivityLogView(_context));
    }

    private void DuplicateCenter_Click(object sender, RoutedEventArgs e)
    {
        ShowManagementView("중복 파일 관리", "같은 source hash를 가진 파일을 그룹으로 비교하고 정리합니다.", new DuplicateManagerView(_context));
    }

    private void BackupCenter_Click(object sender, RoutedEventArgs e)
    {
        ShowManagementView("백업 / 복원 마법사", "vault 전체 백업과 복원 파일 검토를 단계별로 진행합니다.", new BackupRestoreWizardView(_context));
    }

    private void TagCenter_Click(object sender, RoutedEventArgs e)
    {
        ShowManagementView("태그 / 색상 라벨", "파일에 색상 라벨과 태그를 일괄 적용하고 관리합니다.", new TagManagerView(_context));
    }

    private void CloseManagement_Click(object sender, RoutedEventArgs e)
    {
        ManagementOverlay.Visibility = Visibility.Collapsed;
        ManagementContentHost.Content = null;
        SetSidebarButtonActive(ActivityCenterButton, false);
        SetSidebarButtonActive(DuplicateCenterButton, false);
        SetSidebarButtonActive(BackupCenterButton, false);
        SetSidebarButtonActive(TagCenterButton, false);
        ShowMediaView();
        StatusText.Text = "갤러리 화면으로 돌아왔습니다.";
    }

    private void All_Click(object sender, RoutedEventArgs e)
    {
        ClearTopicSelection();
        _kindFilter = null;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void Images_Click(object sender, RoutedEventArgs e)
    {
        ClearTopicSelection();
        _kindFilter = MediaKind.Image;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void Videos_Click(object sender, RoutedEventArgs e)
    {
        ClearTopicSelection();
        _kindFilter = MediaKind.Video;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void Documents_Click(object sender, RoutedEventArgs e)
    {
        ClearTopicSelection();
        _kindFilter = MediaKind.Document;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void Archives_Click(object sender, RoutedEventArgs e)
    {
        ClearTopicSelection();
        _kindFilter = MediaKind.Archive;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void OtherFiles_Click(object sender, RoutedEventArgs e)
    {
        ClearTopicSelection();
        _kindFilter = MediaKind.Other;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        ClearTopicSelection();
        _kindFilter = null;
        _favoritesOnly = true;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void ManageTopics_Click(object sender, RoutedEventArgs e)
    {
        ShowTopicManagementView();
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;

        var sidebarWidth = _isSidebarCollapsed ? 84 : 336;
        SidebarColumn.Width = new GridLength(sidebarWidth);
        HeaderSidebarColumn.Width = new GridLength(sidebarWidth);

        SidebarExpandedContent.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarCollapsedRail.Visibility = _isSidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;

        HeaderBrandExpanded.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        HeaderBrandCollapsed.Visibility = _isSidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        UpdateSidebarActiveState();
    }



    private void ToggleManagementCenterSection_Click(object sender, RoutedEventArgs e)
    {
        _settings.SidebarManagementCenterExpanded = !_settings.SidebarManagementCenterExpanded;
        SaveAndApplySidebarSectionState();
    }

    private void ToggleMediaFilterSection_Click(object sender, RoutedEventArgs e)
    {
        _settings.SidebarMediaFiltersExpanded = !_settings.SidebarMediaFiltersExpanded;
        SaveAndApplySidebarSectionState();
    }

    private void ToggleTopicFolderSection_Click(object sender, RoutedEventArgs e)
    {
        _settings.SidebarTopicFoldersExpanded = !_settings.SidebarTopicFoldersExpanded;
        SaveAndApplySidebarSectionState();
    }

    private void SaveAndApplySidebarSectionState()
    {
        AppSettingsService.Save(_settings);
        ApplySidebarSectionState();
    }

    private void ApplySidebarSectionState()
    {
        if (ManagementCenterPanel == null)
            return;

        SetSidebarSectionExpanded(ManagementCenterPanel, ManagementCenterToggleButton, _settings.SidebarManagementCenterExpanded, "관리 센터");
        SetSidebarSectionExpanded(MediaFilterPanel, MediaFilterToggleButton, _settings.SidebarMediaFiltersExpanded, "미디어 필터");
        SetSidebarSectionExpanded(TopicFolderPanel, TopicFolderToggleButton, _settings.SidebarTopicFoldersExpanded, "주제 폴더");
        SidebarSectionSeparator.Visibility = _settings.SidebarTopicFoldersExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetSidebarSectionExpanded(UIElement panel, Button toggleButton, bool isExpanded, string title)
    {
        panel.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        toggleButton.Content = isExpanded ? "▼" : "▶";
        toggleButton.FontFamily = new FontFamily("Segoe UI Symbol");
        toggleButton.FontSize = isExpanded ? 10 : 10;
        toggleButton.ToolTip = isExpanded ? $"{title} 접기" : $"{title} 펼치기";
    }

    private void BeginContentBusyProgress(int total, string title, string detail, string progressText)
    {
        if (_isImporting)
            return;

        _isContentBusy = true;
        Cursor = Cursors.Wait;
        ContentBusyOverlay.Visibility = Visibility.Visible;
        ContentBusyTitleText.Text = title;
        ContentBusyDetailText.Text = detail;
        ContentBusyPulseBar.IsIndeterminate = true;
        ContentBusyProgressBar.IsIndeterminate = total <= 0;
        ContentBusyProgressBar.Minimum = 0;
        ContentBusyProgressBar.Maximum = Math.Max(1, total);
        ContentBusyProgressBar.Value = 0;
        ContentBusyProgressText.Text = progressText;
    }

    private void UpdateContentBusyProgress(int current, int total, string currentName)
    {
        if (!_isContentBusy)
            return;

        var safeTotal = Math.Max(1, total);
        var safeCurrent = Math.Clamp(current, 0, safeTotal);
        ContentBusyProgressBar.IsIndeterminate = false;
        ContentBusyProgressBar.Maximum = safeTotal;
        ContentBusyProgressBar.Value = safeCurrent;
        ContentBusyProgressText.Text = $"{safeCurrent} / {safeTotal}";
        ContentBusyDetailText.Text = string.IsNullOrWhiteSpace(currentName)
            ? "썸네일을 안전하게 불러오고 있습니다."
            : currentName;
    }

    private void EndContentBusyProgress()
    {
        _contentBusyVersion = 0;
        _isContentBusy = false;
        if (!_isImporting)
            Cursor = null;
        ContentBusyProgressBar.IsIndeterminate = false;
        ContentBusyProgressBar.Value = 0;
        ContentBusyOverlay.Visibility = Visibility.Collapsed;
    }

    private void InitializeGridColumnSelectors()
    {
        _updatingGridColumnCombos = true;
        try
        {
            SelectGridColumnCombo(MediaGridColumnsCombo, _mediaGridColumns);
            SelectGridColumnCombo(TopicGridColumnsCombo, _topicGridColumns);
        }
        finally
        {
            _updatingGridColumnCombos = false;
        }
    }

    private static void SelectGridColumnCombo(ComboBox combo, int columns)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(item.Tag?.ToString(), out var value) && value == columns)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private static int NormalizeGridColumns(int value) => Math.Clamp(value, 3, 8);

    private static int ReadGridColumnCombo(ComboBox combo, int fallback)
    {
        return combo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var value)
            ? NormalizeGridColumns(value)
            : NormalizeGridColumns(fallback);
    }

    private void MediaGridColumnsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingGridColumnCombos)
            return;

        _mediaGridColumns = ReadGridColumnCombo(MediaGridColumnsCombo, _mediaGridColumns);
        _settings.MediaGridColumns = _mediaGridColumns;
        AppSettingsService.Save(_settings);
        ApplyGalleryViewMode();
        StatusText.Text = $"파일 GRID 한 줄 {_mediaGridColumns}개 표시로 변경되었습니다.";
    }

    private void TopicGridColumnsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingGridColumnCombos)
            return;

        _topicGridColumns = ReadGridColumnCombo(TopicGridColumnsCombo, _topicGridColumns);
        _settings.TopicGridColumns = _topicGridColumns;
        AppSettingsService.Save(_settings);
        ApplyTopicExplorerViewMode();
        StatusText.Text = $"주제 GRID 한 줄 {_topicGridColumns}개 표시로 변경되었습니다.";
    }

    private void MediaGrid_Click(object sender, RoutedEventArgs e)
    {
        _isMediaListView = false;
        ApplyGalleryViewMode();
    }

    private void MediaList_Click(object sender, RoutedEventArgs e)
    {
        _isMediaListView = true;
        ApplyGalleryViewMode();
    }

    private void ApplyGalleryViewMode()
    {
        if (GalleryList == null) return;

        GalleryList.Tag = _mediaGridColumns;
        GalleryList.ItemsPanel = (ItemsPanelTemplate)FindResource(_isMediaListView ? "GalleryListItemsPanel" : "GalleryGridItemsPanel");
        GalleryList.ItemTemplate = (DataTemplate)FindResource(_isMediaListView ? "GalleryListTemplate" : "GalleryGridTemplate");
        MediaGridColumnsCombo.Visibility = _isMediaListView ? Visibility.Collapsed : Visibility.Visible;

        SetSidebarButtonActive(MediaGridButton, !_isMediaListView);
        SetSidebarButtonActive(MediaListButton, _isMediaListView);
    }

    private void TopicGrid_Click(object sender, RoutedEventArgs e)
    {
        _isTopicListView = false;
        ApplyTopicExplorerViewMode();
    }

    private void TopicListView_Click(object sender, RoutedEventArgs e)
    {
        _isTopicListView = true;
        ApplyTopicExplorerViewMode();
    }

    private void ApplyTopicExplorerViewMode()
    {
        if (TopicGridList == null || TopicLinearList == null) return;

        TopicGridList.Tag = _topicGridColumns;
        TopicGridList.Visibility = _isTopicListView ? Visibility.Collapsed : Visibility.Visible;
        TopicLinearList.Visibility = _isTopicListView ? Visibility.Visible : Visibility.Collapsed;
        TopicGridColumnsCombo.Visibility = _isTopicListView ? Visibility.Collapsed : Visibility.Visible;

        SetSidebarButtonActive(TopicGridButton, !_isTopicListView);
        SetSidebarButtonActive(TopicListButton, _isTopicListView);
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ResetPageAndReload();
    }


    private void TopicFolderSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var previousIgnore = _ignoreTopicSelection;
        _ignoreTopicSelection = true;
        ApplyTopicFolderSearch();
        _ignoreTopicSelection = previousIgnore;
        SyncTopicCardSelection();
        UpdateTopicDetail();
    }

    private void ClearTopicFolderSearch_Click(object sender, RoutedEventArgs e)
    {
        TopicFolderSearchBox.Text = string.Empty;
        TopicFolderSearchBox.Focus();
    }

    private void AddTopic_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog("주제 추가", "새 주제 이름을 입력하세요.") { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        var topicName = (dlg.Value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(topicName))
            return;

        var duplicateTopic = _topics.FirstOrDefault(topic =>
            string.Equals(topic.Name.Trim(), topicName, StringComparison.OrdinalIgnoreCase));

        if (duplicateTopic != null)
        {
            var confirm = MessageDialog.Show(
                this,
                $"이미 같은 이름의 주제가 있습니다.\n\n기존 주제: {duplicateTopic.Name}\n항목 수: {duplicateTopic.ItemCount}개\n\n그래도 '{topicName}' 주제를 새로 추가할까요?",
                "중복 주제 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;
        }

        try
        {
            EndContentBusyProgress();
            var topic = _context.Database.CreateTopic(topicName);
            _context.ActivityLogs.Add("topic", "주제 추가", topic.Name, topic.Id, topic.Name);
            TopicFolderSearchBox.Text = string.Empty;
            ReloadTopics();
            TopicList.SelectedItem = _sidebarTopics.FirstOrDefault(t => t.Id == topic.Id);
            _currentPage = 1;
            ShowMediaView();
            ReloadMedia();
        }
        catch (Exception ex)
        {
            EndContentBusyProgress();
            MessageDialog.Show(this, "주제를 추가하지 못했습니다.\n\n" + ex.Message, "주제 추가", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RenameTopic_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTopic == null)
        {
            MessageDialog.Show(this, "먼저 변경할 주제를 선택하세요.", "주제", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new InputDialog("주제 이름 변경", "새 이름을 입력하세요.", SelectedTopic.Name) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var oldName = SelectedTopic.Name;
            _context.Database.RenameTopic(SelectedTopic.Id, dlg.Value);
            _context.ActivityLogs.Add("topic", "주제 이름 변경", $"{oldName} → {dlg.Value}", SelectedTopic.Id, dlg.Value);
            ReloadAll();
        }
    }

    private void EditTopicDescription_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTopic == null)
        {
            MessageDialog.Show(this, "먼저 설명을 수정할 주제를 선택하세요.", "주제", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new InputDialog("주제 설명 수정", "주제 설명을 입력하세요.", SelectedTopic.Description) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _context.Database.SetTopicDescription(SelectedTopic.Id, dlg.Value);
            _context.ActivityLogs.Add("topic", "주제 설명 수정", SelectedTopic.Name, SelectedTopic.Id, SelectedTopic.Name);
            ReloadAll();
        }
    }

    private void SetTopicCover_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTopic == null)
        {
            MessageDialog.Show(this, "먼저 썸네일을 지정할 주제를 선택하세요.", "주제", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new TopicThumbnailWindow(_context, SelectedTopic) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _context.ActivityLogs.Add("topic", "주제 썸네일 지정", SelectedTopic.Name, SelectedTopic.Id, SelectedTopic.Name);
            ReloadAll();
        }
    }

    private void DeleteTopic_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTopic == null)
        {
            MessageDialog.Show(this, "먼저 삭제할 주제를 선택하세요.", "주제", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var media = _context.Database.GetMedia(SelectedTopic.Id, null);
        var confirm = MessageDialog.Show(this, $"'{SelectedTopic.Name}' 주제를 삭제합니다.\n이 주제 안의 미디어 {media.Count}개도 함께 삭제됩니다. 계속할까요?", "주제 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        foreach (var item in media)
            _context.Media.DeleteMediaFiles(item);
        var deletedTopicName = SelectedTopic.Name;
        _context.Database.DeleteTopic(SelectedTopic.Id);
        _context.ActivityLogs.Add("topic", "주제 삭제", $"{deletedTopicName} · {media.Count}개 항목", targetName: deletedTopicName);
        TopicList.SelectedItem = null;
        ReloadAll();
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_isImporting)
        {
            ShowImportBusyNotice();
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "가져올 파일 선택",
            Filter = "지원 파일|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff;*.mp4;*.mov;*.m4v;*.avi;*.mkv;*.wmv;*.webm;*.pdf;*.txt;*.md;*.rtf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.csv;*.json;*.xml;*.html;*.htm;*.log;*.ini;*.yaml;*.yml;*.zip;*.7z;*.rar;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.iso|이미지|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff|동영상|*.mp4;*.mov;*.m4v;*.avi;*.mkv;*.wmv;*.webm|문서|*.pdf;*.txt;*.md;*.rtf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.csv;*.json;*.xml;*.html;*.htm;*.log;*.ini;*.yaml;*.yml|압축파일|*.zip;*.7z;*.rar;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.iso|모든 파일|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dlg.ShowDialog(this) == true)
            await ImportFilesAsync(dlg.FileNames);
    }

    private Topic ResolveImportTargetTopic()
    {
        if (SelectedTopic != null)
            return SelectedTopic;

        var uncategorized = _topics.FirstOrDefault(t => string.Equals(t.Name.Trim(), "미분류", StringComparison.OrdinalIgnoreCase));
        if (uncategorized != null)
            return uncategorized;

        var created = _context.Database.CreateTopic("미분류");
        ReloadTopics();
        return _topics.FirstOrDefault(t => t.Id == created.Id)
            ?? _topics.FirstOrDefault(t => string.Equals(t.Name.Trim(), "미분류", StringComparison.OrdinalIgnoreCase))
            ?? created;
    }

    private async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        if (_isImporting)
        {
            ShowImportBusyNotice();
            return;
        }

        var pathList = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (pathList.Count == 0)
        {
            StatusText.Text = "가져올 파일이 없습니다.";
            return;
        }

        BeginImportProgress(0, "파일 목록 확인 중...");
        try
        {
            var scanResult = await Task.Run(() => CollectImportFiles(pathList));
            foreach (var error in scanResult.Errors.Take(3))
                MessageDialog.Show(this, error, "가져오기", MessageBoxButton.OK, MessageBoxImage.Warning);

            var files = scanResult.Files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0)
            {
                StatusText.Text = "가져올 파일이 없습니다.";
                return;
            }

            var topic = ResolveImportTargetTopic();
            var imported = 0;
            var duplicateAdded = 0;
            var duplicateSkipped = 0;
            var skipped = 0;
            var failed = 0;
            var processed = 0;
            var total = files.Count;
            var duplicatePolicy = DuplicateImportChoice.Ask;

            ConfigureImportProgress(total);

            foreach (var file in files)
            {
                processed++;
                try
                {
                    if (!MediaVaultService.IsSupported(file))
                    {
                        skipped++;
                        UpdateImportProgress(processed, total, $"지원하지 않는 파일 건너뜀: {Path.GetFileName(file)}");
                        continue;
                    }

                    UpdateImportProgress(processed, total, $"중복 검사 중: {Path.GetFileName(file)}");
                    var sourceHash = await Task.Run(() => _context.Media.ComputeSourceFingerprint(file));
                    var duplicates = _context.Database.GetMediaBySourceHash(sourceHash);

                    if (duplicates.Count > 0)
                    {
                        var duplicateChoice = duplicatePolicy;
                        if (duplicateChoice == DuplicateImportChoice.Ask)
                        {
                            Cursor = null;
                            duplicateChoice = ConfirmDuplicateImport(file, duplicates);
                            Cursor = Cursors.Wait;
                        }

                        if (duplicateChoice == DuplicateImportChoice.AddAll)
                            duplicatePolicy = DuplicateImportChoice.AddAll;
                        else if (duplicateChoice == DuplicateImportChoice.SkipAll)
                            duplicatePolicy = DuplicateImportChoice.SkipAll;

                        if (duplicateChoice is DuplicateImportChoice.Skip or DuplicateImportChoice.SkipAll)
                        {
                            duplicateSkipped++;
                            UpdateImportProgress(processed, total, $"중복 파일 건너뜀: {Path.GetFileName(file)}");
                            _context.ActivityLogs.Add("duplicate", "중복 파일 건너뜀", Path.GetFileName(file));
                            continue;
                        }

                        duplicateAdded++;
                    }

                    UpdateImportProgress(processed, total, $"암호화 저장 중: {Path.GetFileName(file)}");
                    var item = await RunStaAsync(() => _context.Media.PrepareMediaImport(file, topic.Id, sourceHash));
                    _context.Database.AddMedia(item);
                    imported++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Cursor = null;
                    MessageDialog.Show(this, $"가져오기 실패: {Path.GetFileName(file)}\n\n{ex.Message}", "가져오기", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Cursor = Cursors.Wait;
                }
            }

            ShowMediaView();
            ReloadAll();
            StatusText.Text = $"'{topic.Name}'에 가져오기 완료: {imported}개, 중복 추가: {duplicateAdded}개, 중복 건너뜀: {duplicateSkipped}개, 기타 건너뜀: {skipped}개, 실패: {failed}개";
            _context.ActivityLogs.Add("import", "파일 가져오기 완료", $"{topic.Name} · 추가 {imported}개 · 중복 건너뜀 {duplicateSkipped}개 · 실패 {failed}개", topic.Id, topic.Name, failed > 0 ? "partial" : "success");
        }
        finally
        {
            EndImportProgress();
        }
    }

    private static (List<string> Files, List<string> Errors) CollectImportFiles(IEnumerable<string> paths)
    {
        var files = new List<string>();
        var errors = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                    files.AddRange(Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories));
                else if (File.Exists(path))
                    files.Add(path);
            }
            catch (Exception ex)
            {
                errors.Add($"파일 목록을 읽을 수 없습니다: {Path.GetFileName(path)}\n\n{ex.Message}");
            }
        }

        return (files, errors);
    }

    private DuplicateImportChoice ConfirmDuplicateImport(string sourcePath, List<MediaItem> duplicates)
    {
        var fileName = Path.GetFileName(sourcePath);
        var info = new FileInfo(sourcePath);
        var first = duplicates.First();
        var topicName = _topics.FirstOrDefault(t => t.Id == first.TopicId)?.Name ?? "알 수 없음";
        var moreText = duplicates.Count > 1 ? $"\n동일한 내용의 보관 항목이 {duplicates.Count}개 있습니다." : string.Empty;

        var message =
            "이미 같은 내용의 파일이 보관함에 있습니다.\n\n" +
            $"추가하려는 파일: {fileName}\n" +
            $"크기: {FormatBytes(info.Length)}\n\n" +
            "이미 보관된 항목:\n" +
            $"주제: {topicName}\n" +
            $"파일명: {first.OriginalName}\n" +
            $"추가일: {first.CreatedUtc.ToLocalTime():yyyy.MM.dd HH:mm}" +
            moreText +
            "\n\n이 파일을 그래도 추가할까요?";

        var result = MessageDialog.ShowOptions(
            this,
            message,
            "중복 파일 확인",
            MessageBoxImage.Question,
            new MessageDialogOption("모두 건너뛰기", "skip_all", true),
            new MessageDialogOption("아니오", "skip", true, isCancel: true),
            new MessageDialogOption("예", "add", false),
            new MessageDialogOption("모두 덮어쓰기", "add_all", false, isDefault: true));

        return result switch
        {
            "add_all" => DuplicateImportChoice.AddAll,
            "add" => DuplicateImportChoice.Add,
            "skip_all" => DuplicateImportChoice.SkipAll,
            _ => DuplicateImportChoice.Skip
        };
    }

    private static Task<T> RunStaAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private void BeginImportProgress(int total, string message)
    {
        _isImporting = true;
        Cursor = Cursors.Wait;
        ImportProgressPanel.Visibility = Visibility.Visible;
        ImportModalOverlay.Visibility = Visibility.Visible;
        ImportModalTitleText.Text = "파일 가져오는 중";
        SetImportProgress(total <= 0, 0, Math.Max(1, total), 0, total <= 0 ? "파일 목록 확인 중" : $"가져오기 0 / {total}", message);
    }

    private void ConfigureImportProgress(int total)
    {
        ImportProgressPanel.Visibility = Visibility.Visible;
        ImportModalOverlay.Visibility = Visibility.Visible;
        SetImportProgress(false, 0, Math.Max(1, total), 0, $"가져오기 0 / {total}", "가져올 파일을 암호화 저장할 준비가 끝났습니다.");
    }

    private void UpdateImportProgress(int current, int total, string message)
    {
        var safeTotal = Math.Max(1, total);
        var safeCurrent = Math.Min(current, safeTotal);
        SetImportProgress(false, 0, safeTotal, safeCurrent, $"가져오기 {Math.Min(current, total)} / {total}", message);
    }

    private void SetImportProgress(bool isIndeterminate, double minimum, double maximum, double value, string progressText, string detailText)
    {
        StatusText.Text = detailText;

        ImportProgressBar.IsIndeterminate = isIndeterminate;
        ImportProgressBar.Minimum = minimum;
        ImportProgressBar.Maximum = maximum;
        ImportProgressBar.Value = value;
        ImportProgressText.Text = progressText;

        ImportModalPulseBar.IsIndeterminate = true;
        ImportModalProgressBar.IsIndeterminate = isIndeterminate;
        ImportModalProgressBar.Minimum = minimum;
        ImportModalProgressBar.Maximum = maximum;
        ImportModalProgressBar.Value = value;
        ImportModalProgressText.Text = progressText;
        ImportModalDetailText.Text = detailText;
    }

    private void ShowImportBusyNotice()
    {
        ImportModalOverlay.Visibility = Visibility.Visible;
        ImportProgressPanel.Visibility = Visibility.Visible;
        ImportModalTitleText.Text = "파일 가져오기 진행 중";
        ImportModalDetailText.Text = "이미 파일을 암호화 저장하고 있습니다. 완료되면 자동으로 닫힙니다.";
        StatusText.Text = "파일 가져오기가 진행 중입니다. 완료 후 다시 시도해주세요.";
    }

    private void EndImportProgress()
    {
        _isImporting = false;
        Cursor = null;
        ImportProgressBar.IsIndeterminate = false;
        ImportProgressBar.Value = 0;
        ImportModalProgressBar.IsIndeterminate = false;
        ImportModalProgressBar.Value = 0;
        ImportProgressPanel.Visibility = Visibility.Collapsed;
        ImportModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void StorageDropZone_DragEnter(object sender, DragEventArgs e)
    {
        var isValid = e.Data.GetDataPresent(DataFormats.FileDrop);
        SetDropZoneDragState(isValid);
        e.Effects = isValid ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void StorageDropZone_DragOver(object sender, DragEventArgs e)
    {
        var isValid = e.Data.GetDataPresent(DataFormats.FileDrop);
        SetDropZoneDragState(isValid);
        e.Effects = isValid ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void StorageDropZone_DragLeave(object sender, DragEventArgs e)
    {
        UpdateDropZoneHint();
        e.Handled = true;
    }

    private async void StorageDropZone_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        UpdateDropZoneHint();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await ImportFilesAsync(paths);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (_reorderDragPreviewPopup != null)
        {
            UpdateReorderDragPreview(e.GetPosition(this));
            if (e.Data.GetDataPresent(typeof(Topic)) || e.Data.GetDataPresent(typeof(TopicCardViewModel)) || e.Data.GetDataPresent(typeof(MediaCardViewModel)))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }
        }

        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await ImportFilesAsync(paths);
    }

    private void GalleryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedMedia();
    }

    private void GalleryList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var item = FindAncestor<ListBoxItem>(source);
        if (item?.DataContext is not MediaCardViewModel)
            return;

        if (!item.IsSelected)
        {
            GalleryList.SelectedItems.Clear();
            item.IsSelected = true;
        }

        item.Focus();
    }

    private void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedMedia();
    }

    private void OpenSelectedMedia()
    {
        if (GalleryList.SelectedItem is not MediaCardViewModel card)
            return;

        try
        {
            if (card.Item.Kind == MediaKind.Image || card.Item.Kind == MediaKind.Video)
            {
                var viewer = new ViewerWindow(_context, card.Item) { Owner = this };
                viewer.ShowDialog();

                // 즉시 잠금 단축키로 모달 뷰어가 닫히면 ShowDialog 이후 코드가 다시 이어질 수 있습니다.
                // 이 경우 기존 MainWindow는 이미 숨김/정리 중이므로 이전 보관함 컨텍스트에 접근하지 않습니다.
                if (_locking || _isLockTransitionRunning || !IsVisible)
                    return;

                // 단순 보기 후 닫기에서는 전체 목록을 다시 읽지 않습니다.
                // 즐겨찾기, 주제, 썸네일, 삭제처럼 실제 데이터가 변경된 경우에만 새로고침합니다.
                if (viewer.HasChanges)
                    ReloadAll();

                return;
            }

            var confirm = MessageDialog.Show(
                this,
                $"'{card.Item.OriginalName}' 파일을 임시로 복호화한 뒤 Windows 기본 앱으로 열까요?\n\n잠금/종료 시 현재 세션의 임시 파일은 정리됩니다.",
                "파일 열기",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            _context.Media.OpenWithDefaultApp(card.Item);
            StatusText.Text = "임시 파일로 열기: " + card.Item.OriginalName;
        }
        catch (Exception ex)
        {
            if (_locking || _isLockTransitionRunning)
                return;

            MessageDialog.Show(this, "항목을 열 수 없습니다.\n\n" + ex.Message, "열기", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = GalleryList.SelectedItems.Cast<MediaCardViewModel>().ToList();
        if (selected.Count == 0) return;
        if (selected.Count > 1)
        {
            MessageDialog.Show(this, "현재는 한 번에 1개 항목만 내보낼 수 있습니다.", "내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var item = selected[0].Item;
        var dlg = new SaveFileDialog
        {
            Title = "내보낼 위치 선택",
            FileName = item.OriginalName,
            Filter = "원본 파일|*" + item.Extension + "|모든 파일|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            _context.Media.ExportMedia(item, dlg.FileName);
            StatusText.Text = "내보내기 완료: " + Path.GetFileName(dlg.FileName);
            _context.ActivityLogs.Add("export", "파일 내보내기", Path.GetFileName(dlg.FileName), item.Id, item.OriginalName);
        }
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        var selected = GalleryList.SelectedItems.Cast<MediaCardViewModel>().ToList();
        foreach (var card in selected)
            _context.Database.SetFavorite(card.Item.Id, !card.Item.Favorite);
        ReloadMedia();
    }

    private void EditThumbnailCrop_Click(object sender, RoutedEventArgs e)
    {
        var card = GetSingleSelectedMediaCardForThumbnail();
        if (card == null)
            return;

        if (card.Item.Kind == MediaKind.Video)
        {
            ResetVideoThumbnail(card.Item);
            return;
        }

        if (card.Item.Kind != MediaKind.Image)
        {
            MessageDialog.Show(this, "이미지 항목은 원본 이미지를 기준으로 크롭할 수 있습니다.\n문서·압축·기타 파일은 '외부 이미지로 썸네일 설정'을 사용하세요.", "썸네일 크롭", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var source = _context.Media.LoadImage(card.Item);
            ShowThumbnailCropWindow(card.Item, source);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "썸네일 편집 창을 열 수 없습니다.\n\n" + ex.Message, "썸네일 크롭", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetVideoThumbnail_Click(object sender, RoutedEventArgs e)
    {
        var card = GetSingleSelectedMediaCardForThumbnail();
        if (card == null)
            return;

        ResetVideoThumbnail(card.Item);
    }

    private void ResetVideoThumbnail(MediaItem item)
    {
        if (item.Kind != MediaKind.Video)
        {
            MessageDialog.Show(this, "이 기능은 동영상 항목에서만 사용할 수 있습니다.", "영상 썸네일", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? temp = null;
        try
        {
            temp = _context.Media.DecryptVideoToTemp(item);
            var dlg = new VideoThumbnailWindow(temp, item.OriginalName) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.SourceFrameBytes == null || dlg.ThumbnailBytes == null)
                return;

            _context.Media.SetVideoThumbnailFromFrame(item, dlg.SourceFrameBytes, dlg.ThumbnailBytes, dlg.CapturePosition);
            StatusText.Text = "영상 원본 프레임에서 썸네일 재설정 완료: " + item.OriginalName;
            ReloadAll();
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "영상 썸네일 재설정 중 오류가 발생했습니다.\n\n" + ex.Message, "영상 썸네일", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TempFileService.TryDelete(temp ?? string.Empty);
        }
    }

    private void SetExternalThumbnail_Click(object sender, RoutedEventArgs e)
    {
        var card = GetSingleSelectedMediaCardForThumbnail();
        if (card == null)
            return;

        var dlg = new OpenFileDialog
        {
            Title = "썸네일로 사용할 이미지 선택",
            Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff|모든 파일|*.*",
            Multiselect = false
        };

        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            var source = ThumbnailCropWindow.LoadBitmapFromFile(dlg.FileName);
            ShowThumbnailCropWindow(card.Item, source);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "선택한 이미지를 썸네일로 불러올 수 없습니다.\n\n" + ex.Message, "외부 썸네일", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private MediaCardViewModel? GetSingleSelectedMediaCardForThumbnail()
    {
        var selected = GalleryList.SelectedItems.Cast<MediaCardViewModel>().ToList();
        if (selected.Count == 0)
        {
            MessageDialog.Show(this, "썸네일을 수정할 항목을 먼저 선택하세요.", "썸네일", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        if (selected.Count > 1)
        {
            MessageDialog.Show(this, "썸네일 수정은 한 번에 1개 항목만 가능합니다.", "썸네일", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        return selected[0];
    }

    private void ShowThumbnailCropWindow(MediaItem item, BitmapSource source)
    {
        var crop = new ThumbnailCropWindow(source, item.OriginalName) { Owner = this };
        if (crop.ShowDialog() != true || crop.ThumbnailBytes == null)
            return;

        _context.Media.SetCustomThumbnail(item, crop.ThumbnailBytes);
        StatusText.Text = "썸네일 수정 완료: " + item.OriginalName;
        ReloadAll();
    }

    private void DeleteMedia_Click(object sender, RoutedEventArgs e)
    {
        var selected = GalleryList.SelectedItems.Cast<MediaCardViewModel>().ToList();
        if (selected.Count == 0) return;

        var confirm = MessageDialog.Show(this, $"선택한 항목 {selected.Count}개를 보관함에서 삭제할까요?\n삭제하면 복구할 수 없습니다.", "미디어 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        foreach (var card in selected)
            _context.Media.DeleteMediaFiles(card.Item);
        _context.ActivityLogs.Add("delete", "미디어 삭제", $"{selected.Count}개 항목 삭제");
        ReloadAll();
    }

    private async void MoveMediaOrderByNumber_Click(object sender, RoutedEventArgs e)
    {
        var selected = GalleryList.SelectedItems.Cast<MediaCardViewModel>().ToList();
        if (selected.Count == 0)
            return;

        if (selected.Count > 1)
        {
            MessageDialog.Show(this, "순서 이동은 정확한 위치 지정 기능이라 한 번에 1개 파일만 이동할 수 있습니다.\n하나만 선택한 뒤 다시 시도해주세요.", "순서 이동", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var card = selected[0];
        var currentIndex = _filteredItems.FindIndex(item => item.Id == card.Item.Id);
        if (currentIndex < 0)
        {
            MessageDialog.Show(this, "현재 목록에서 선택 항목의 위치를 찾을 수 없습니다.\n필터를 새로고침한 뒤 다시 시도해주세요.", "순서 이동", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var total = _filteredItems.Count;
        if (total <= 1)
        {
            MessageDialog.Show(this, "이동할 다른 항목이 없습니다.", "순서 이동", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new MoveOrderWindow(card.Item.OriginalName, currentIndex + 1, total, Math.Max(1, _itemsPerPage), _currentPage)
        {
            Owner = this
        };

        if (dlg.ShowDialog() != true)
            return;

        await MoveMediaToAbsolutePositionAsync(card.Item.Id, dlg.TargetPosition);
    }

    private async Task MoveMediaToAbsolutePositionAsync(string mediaId, int targetPosition)
    {
        var orderedIds = _filteredItems.Select(item => item.Id).ToList();
        var fromIndex = orderedIds.IndexOf(mediaId);
        if (fromIndex < 0)
            return;

        targetPosition = Math.Clamp(targetPosition, 1, orderedIds.Count);
        var targetIndex = targetPosition - 1;
        if (fromIndex == targetIndex)
        {
            StatusText.Text = $"이미 {targetPosition:N0}번째 위치입니다.";
            return;
        }

        var id = orderedIds[fromIndex];
        orderedIds.RemoveAt(fromIndex);
        targetIndex = Math.Clamp(targetIndex, 0, orderedIds.Count);
        orderedIds.Insert(targetIndex, id);

        _context.Database.UpdateMediaSortOrders(orderedIds);
        _context.ActivityLogs.Add("order", "파일 순서 이동", $"{fromIndex + 1:N0}번째 항목을 {targetPosition:N0}번째 위치로 이동", mediaId, mediaId);

        _filteredItems = BuildFilteredItems();
        _itemsPerPage = NormalizeItemsPerPage(_settings.ItemsPerPage);
        _currentPage = ((targetPosition - 1) / Math.Max(1, _itemsPerPage)) + 1;
        await ApplyPaginationAsync(++_mediaReloadVersion);

        var movedCard = _mediaCards.FirstOrDefault(card => card.Item.Id == mediaId);
        if (movedCard != null)
        {
            GalleryList.SelectedItems.Clear();
            GalleryList.SelectedItem = movedCard;
            GalleryList.ScrollIntoView(movedCard);
            await Dispatcher.InvokeAsync(() => GalleryList.ScrollIntoView(movedCard), DispatcherPriority.Background);
        }

        StatusText.Text = $"순서 이동 완료: {fromIndex + 1:N0}번째 항목을 {targetPosition:N0}번째 위치로 이동했습니다.";
    }

    private void MoveMedia_Click(object sender, RoutedEventArgs e)
    {
        var selected = GalleryList.SelectedItems.Cast<MediaCardViewModel>().ToList();
        if (selected.Count == 0)
        {
            MessageDialog.Show(this, "주제를 변경할 파일을 먼저 선택하세요.\n여러 파일은 Ctrl 또는 Shift로 함께 선택할 수 있습니다.", "주제 변경", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var chooser = new TopicChooserWindow(_context.Database.GetTopics(), selected.Count) { Owner = this };
        if (chooser.ShowDialog() != true || chooser.SelectedTopic == null) return;

        var targetTopic = chooser.SelectedTopic;
        if (selected.Count > 1)
        {
            var confirm = MessageDialog.Show(
                this,
                $"선택한 파일 {selected.Count:N0}개를 '{targetTopic.Name}' 주제로 이동할까요?",
                "주제 일괄 변경",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;
        }

        foreach (var card in selected)
            _context.Database.MoveMedia(card.Item.Id, targetTopic.Id);

        _context.ActivityLogs.Add("topic", "파일 주제 변경", $"{selected.Count:N0}개 파일을 '{targetTopic.Name}' 주제로 이동", targetTopic.Id, targetTopic.Name);
        ReloadAll();
        StatusText.Text = $"주제 변경 완료: {selected.Count:N0}개 파일 → {targetTopic.Name}";
    }

    private void FirstPage_Click(object sender, RoutedEventArgs e)
    {
        GoToPage(1);
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        GoToPage(_currentPage - 1);
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        GoToPage(_currentPage + 1);
    }

    private void LastPage_Click(object sender, RoutedEventArgs e)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(_filteredItems.Count / (double)Math.Max(1, _itemsPerPage)));
        GoToPage(totalPages);
    }

    private void MainWindow_PreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (_locking || _instantLockHotkeyPending)
            return;

        if (e.StagingItem.Input is not KeyEventArgs keyArgs)
            return;

        if (keyArgs.RoutedEvent != Keyboard.KeyDownEvent && keyArgs.RoutedEvent != Keyboard.PreviewKeyDownEvent)
            return;

        var key = NormalizeKey(keyArgs);
        var modifiers = NormalizeModifiers(Keyboard.Modifiers);
        if (!IsConfiguredInstantLockHotkey(key, modifiers))
            return;

        keyArgs.Handled = true;
        _instantLockHotkeyPending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _instantLockHotkeyPending = false;
            LockNow(true);
        }), DispatcherPriority.Input);
    }

    private bool IsConfiguredInstantLockHotkey(Key key, ModifierKeys modifiers)
    {
        if (!TryParseInstantLockHotkey(_settings.InstantLockKey, out var configuredKey, out var configuredModifiers))
            return false;

        return key == configuredKey && modifiers == configuredModifiers;
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

    private void HideOwnedWindowsForLock()
    {
        var ownedWindows = OwnedWindows.Cast<Window>().Where(window => window != this).ToList();
        foreach (var window in ownedWindows)
        {
            try
            {
                if (window is ViewerWindow viewer)
                    viewer.PrepareForVaultLock();
                else
                    window.Hide();
            }
            catch
            {
                // 민감한 하위 창은 가능한 한 먼저 화면에서 숨깁니다.
            }
        }
    }

    private void CloseOwnedWindowsForLock()
    {
        var ownedWindows = OwnedWindows.Cast<Window>().Where(window => window != this).ToList();
        foreach (var window in ownedWindows)
        {
            try
            {
                if (window is ViewerWindow viewer)
                    viewer.PrepareForVaultLock();
                else if (window.IsVisible)
                    window.Hide();

                window.Close();
            }
            catch
            {
                // 하위 창이 이미 닫히는 중이어도 잠금 전환은 계속 진행합니다.
            }
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(new VaultService(), _settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _settings = dlg.Settings;
            _itemsPerPage = NormalizeItemsPerPage(_settings.ItemsPerPage);
            _mediaGridColumns = NormalizeGridColumns(_settings.MediaGridColumns);
            _topicGridColumns = NormalizeGridColumns(_settings.TopicGridColumns);
            InitializeGridColumnSelectors();
            ApplySidebarSectionState();
            ApplyGalleryViewMode();
            ApplyTopicExplorerViewMode();
            _lastActivityUtc = DateTime.UtcNow;
            UpdateAutoLockStatus();
            ReloadMedia();
        }
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        LockNow();
    }

    private void LockNow() => LockNow(false);

    private void LockNow(bool fromInstantHotkey)
    {
        if (_locking || _isLockTransitionRunning) return;
        _locking = true;
        _isLockTransitionRunning = true;

        var app = Application.Current;
        var previousShutdownMode = app.ShutdownMode;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            try { EndImportProgress(); } catch { }
            try { EndContentBusyProgress(); } catch { }
            try { _autoLockTimer.Stop(); } catch { }
            try { _reorderAutoScrollTimer.Stop(); } catch { }

            // 무거운 미디어 정리보다 먼저 창을 숨겨 이미지와 영상이 즉시 사라지도록 합니다.
            try { HideOwnedWindowsForLock(); } catch { }
            try { Hide(); } catch { }

            try { CloseOwnedWindowsForLock(); } catch { }
            try { TempFileService.CleanCurrentSession(); } catch { }
            try { _context.Dispose(); } catch { }

            var login = new LoginWindow(new VaultService());
            var unlocked = login.ShowDialog() == true && login.Context != null;
            if (unlocked)
            {
                var main = new MainWindow(login.Context);
                app.MainWindow = main;
                main.Show();

                // 기존 숨김 창이 완전히 닫힐 때까지 명시적 종료 모드를 유지합니다.
                // 일부 WPF/MediaElement 경로에서는 복원 시점이 빠르면 앱이 종료될 수 있습니다.
                try { Close(); } catch { }
                app.ShutdownMode = previousShutdownMode;
                return;
            }

            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.Shutdown();
        }
        catch
        {
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.Shutdown();
        }
    }

    private void MarkActivity()
    {
        _lastActivityUtc = DateTime.UtcNow;
    }

    private void AutoLockTimer_Tick(object? sender, EventArgs e)
    {
        var minutes = Math.Max(1, _settings.AutoLockMinutes);
        var remaining = TimeSpan.FromMinutes(minutes) - (DateTime.UtcNow - _lastActivityUtc);
        if (remaining <= TimeSpan.Zero)
        {
            StatusText.Text = "자동 잠금 시간이 지나 보관함을 잠급니다.";
            LockNow();
            return;
        }
        AutoLockStatusText.Text = $"자동 잠금: {FormatRemaining(remaining)} 남음";
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_settings.LockOnMinimize && WindowState == WindowState.Minimized)
            LockNow();
    }

    private void UpdateAutoLockStatus()
    {
        AutoLockStatusText.Text = $"자동 잠금: {_settings.AutoLockMinutes}분";
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}시간 {remaining.Minutes}분";
        return $"{Math.Max(0, remaining.Minutes)}분";
    }

}

