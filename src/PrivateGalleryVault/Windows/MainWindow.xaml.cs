using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
using PrivateGalleryVault.Controllers;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;
using PrivateGalleryVault.State;
using PrivateGalleryVault.Views;
using PrivateGalleryVault.ViewModels;

namespace PrivateGalleryVault.Windows;

public partial class MainWindow : Window
{
    private const string SidebarTopicSortCustom = "custom";
    private const string SidebarTopicSortLatest = "latest";
    private const string SidebarTopicSortNameDesc = "nameDesc";
    private const string SidebarTopicSortNameAsc = "nameAsc";
    private const string FolderSearchScopeCurrent = "current";
    private const string FolderSearchScopeDescendants = "descendants";
    private const string FolderSearchScopeTopic = "topic";

    private readonly VaultContext _context;
    private readonly MainWindowState _state = new();
    private readonly FolderNavigationController _folderNavigationController;
    private readonly MediaImportController _mediaImportController;
    private readonly MediaFilterController _mediaFilterController;
    private readonly SidebarTopicController _sidebarTopicController;
    private readonly OrderEditController _orderEditController;
    private readonly MediaDragDropController _mediaDragDropController = new();
    private readonly ObservableCollection<Topic> _topics = [];
    private readonly ObservableCollection<Topic> _sidebarTopics = [];
    private readonly ObservableCollection<MediaCardViewModel> _mediaCards = [];
    private readonly ObservableCollection<TopicFolderCardViewModel> _topicFolderCards = [];
    private readonly ObservableCollection<FolderBreadcrumbItemViewModel> _folderBreadcrumbItems = [];
    private readonly ObservableCollection<FolderTreeItemViewModel> _folderTreeItems = [];
    private readonly Dictionary<string, ObservableCollection<FolderTreeItemViewModel>> _sidebarTopicFolderTrees = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<TopicCardViewModel> _topicCards = [];
    private readonly ObservableCollection<MediaTagFilterOption> _mediaTagFilterOptions = [];
    private List<MediaItem> _filteredItems = [];
    private readonly DispatcherTimer _autoLockTimer;
    private MediaKind? _kindFilter = null;
    private bool _favoritesOnly;
    private string? _selectedMediaTagFilterId;
    private bool _updatingMediaTagFilterOptions;
    private string? _currentTopicFolderId;
    private string _folderSearchScope = FolderSearchScopeCurrent;
    private bool _ignoreFolderSearchScopeChange;
    private bool _ignoreFolderTreeSelection;
    private bool _isCenterFolderTreeVisible;
    private bool _ignoreSidebarTopicFolderTreeSelection;
    private bool _sidebarTopicFolderTreeRefreshQueued;
    private bool _isRefreshingSidebarTopicFolderTrees;
    private DateTime _lastSidebarTopicFolderTreeInputUtc = DateTime.MinValue;
    private TreeView? _lastSidebarTopicFolderTreeInputTree;
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
    private Point _folderDragStartPoint;
    private string? _folderDragSourceId;
    private string? _pendingSelectMediaIdAfterReload;
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
    private List<string>? _folderOrderSnapshot;
    private int _mediaReloadVersion;
    private bool _isReloadingMedia;
    private bool _reloadMediaRequestedAfterCurrent;
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
        _folderNavigationController = new FolderNavigationController(_context);
        _mediaImportController = new MediaImportController(_context);
        _mediaFilterController = new MediaFilterController(_context);
        _sidebarTopicController = new SidebarTopicController();
        _orderEditController = new OrderEditController(_context);
        _settings = AppSettingsService.Load();
        _sidebarTopicSortMode = NormalizeSidebarTopicSortMode(_settings.SidebarTopicSortMode);
        _itemsPerPage = NormalizeItemsPerPage(_settings.ItemsPerPage);
        _mediaGridColumns = NormalizeGridColumns(_settings.MediaGridColumns);
        _topicGridColumns = NormalizeGridColumns(_settings.TopicGridColumns);
        InitializeGridColumnSelectors();
        ApplySidebarSectionState();
        TopicList.ItemsSource = _sidebarTopics;
        GalleryList.ItemsSource = _mediaCards;
        FolderGridList.ItemsSource = _topicFolderCards;
        CurrentFolderBreadcrumbList.ItemsSource = _folderBreadcrumbItems;
        FolderTreeView.ItemsSource = _folderTreeItems;
        MediaTagFilterCombo.ItemsSource = _mediaTagFilterOptions;
        InitializeFolderSearchScopeSelector();
        TopicGridList.ItemsSource = _topicCards;
        TopicLinearList.ItemsSource = _topicCards;
        ApplyGalleryViewMode();
        ApplyTopicExplorerViewMode();
        EnsureOrderEditCancelButtons();
        UpdateOrderEditVisual();

        _autoLockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _autoLockTimer.Tick += AutoLockTimer_Tick;
        _autoLockTimer.Start();

        _reorderAutoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(55) };
        _reorderAutoScrollTimer.Tick += ReorderAutoScrollTimer_Tick;

        PreviewMouseMove += (_, _) => MarkActivity();
        PreviewMouseDown += (_, _) => MarkActivity();
        PreviewKeyDown += (_, _) => MarkActivity();
        PreviewKeyDown += MainWindow_OrderEditPreviewKeyDown;
        PreviewMouseWheel += (_, _) => MarkActivity();
        InputManager.Current.PreProcessInput += MainWindow_PreProcessInput;
        StateChanged += MainWindow_StateChanged;
        Loaded += (_, _) =>
        {
            RefreshMediaTagFilterOptions();
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

    private void SyncMainWindowState()
    {
        _state.SetNavigation(SelectedTopicId, _currentTopicFolderId, _folderSearchScope);
        _state.SelectedMediaTagFilterId = _selectedMediaTagFilterId;
        _state.FavoritesOnly = _favoritesOnly;
        _state.IsOrderEditMode = _isOrderEditMode;
        _state.IsImporting = _isImporting;
        _state.IsReloadingMedia = _isReloadingMedia;
        _state.ReloadMediaRequestedAfterCurrent = _reloadMediaRequestedAfterCurrent;
        _state.CurrentPage = _currentPage;
    }

    private void ReloadAll()
    {
        RefreshMediaTagFilterOptions();
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
        foreach (var topic in _sidebarTopicController.ApplySearchAndSort(_topics, query, _sidebarTopicSortMode))
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

        if (TopicFolderSearchEmptyState != null)
            TopicFolderSearchEmptyState.Visibility = hasQuery && _sidebarTopics.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (TopicFolderSearchEmptyText != null && hasQuery && _sidebarTopics.Count == 0)
            TopicFolderSearchEmptyText.Text = _sidebarTopicController.BuildEmptySearchMessage(query);

        ApplySidebarTopicSortVisual();
        RefreshSidebarTopicFolderTrees();
    }

    private void RefreshSidebarTopicFolderTrees()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(RefreshSidebarTopicFolderTrees));
            return;
        }

        if (_sidebarTopicFolderTreeRefreshQueued)
            return;

        _sidebarTopicFolderTreeRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(RefreshSidebarTopicFolderTreesCore));
    }

    private void RefreshSidebarTopicFolderTreesCore()
    {
        if (!_sidebarTopicFolderTreeRefreshQueued || _isRefreshingSidebarTopicFolderTrees)
            return;

        _sidebarTopicFolderTreeRefreshQueued = false;
        _isRefreshingSidebarTopicFolderTrees = true;
        var retry = false;

        try
        {
            var topicsSnapshot = _sidebarTopics.ToList();
            var visibleIds = topicsSnapshot.Select(topic => topic.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var staleId in _sidebarTopicFolderTrees.Keys.Where(id => !visibleIds.Contains(id)).ToList())
                _sidebarTopicFolderTrees.Remove(staleId);

            foreach (var topic in topicsSnapshot)
            {
                var nodes = new ObservableCollection<FolderTreeItemViewModel>(BuildSidebarTopicFolderNodes(topic));
                _sidebarTopicFolderTrees[topic.Id] = nodes;
            }

            RefreshLoadedSidebarTopicFolderTreeControls();
            SyncSidebarTopicFolderTreeSelection();
        }
        catch (InvalidOperationException ex) when (IsWpfContentGenerationException(ex))
        {
            retry = true;
            AppLogger.Warn("Sidebar folder tree refresh deferred because WPF is still generating item containers. " + ex.Message);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Sidebar folder tree refresh failed.", ex);
        }
        finally
        {
            _isRefreshingSidebarTopicFolderTrees = false;
        }

        if (retry)
        {
            _sidebarTopicFolderTreeRefreshQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RefreshSidebarTopicFolderTreesCore));
        }
    }

    private static bool IsWpfContentGenerationException(InvalidOperationException ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("StartAt", StringComparison.OrdinalIgnoreCase)
               || message.Contains("콘텐츠 생성", StringComparison.OrdinalIgnoreCase)
               || message.Contains("content generation", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshLoadedSidebarTopicFolderTreeControls()
    {
        var previousIgnore = _ignoreSidebarTopicFolderTreeSelection;
        _ignoreSidebarTopicFolderTreeSelection = true;
        try
        {
            foreach (var tree in FindVisualChildren<TreeView>(TopicList).Where(t => string.Equals(t.Name, "SidebarTopicFolderTree", StringComparison.Ordinal)))
            {
                if (tree.DataContext is Topic topic && _sidebarTopicFolderTrees.TryGetValue(topic.Id, out var nodes))
                {
                    if (!ReferenceEquals(tree.ItemsSource, nodes))
                        tree.ItemsSource = nodes;
                    tree.Visibility = nodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    tree.ItemsSource = null;
                    tree.Visibility = Visibility.Collapsed;
                }
            }
        }
        finally
        {
            _ignoreSidebarTopicFolderTreeSelection = previousIgnore;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null)
            yield break;

        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private List<FolderTreeItemViewModel> BuildSidebarTopicFolderNodes(Topic topic)
    {
        var result = new List<FolderTreeItemViewModel>();
        try
        {
            var root = new FolderTreeItemViewModel(
                topic.Name,
                null,
                "⌂",
                "주제 루트",
                isRoot: true,
                canRenameOrDelete: false,
                mediaCount: 0,
                childFolderCount: 0);

            var visitedFolderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddFolderTreeChildren(root, topic.Id, null, 0, visitedFolderIds);
            foreach (var child in root.Children)
            {
                child.IsExpanded = true;
                result.Add(child);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Sidebar folder tree build failed topicId={topic.Id}; topicName={topic.Name}", ex);
        }

        return result;
    }

    private void SidebarTopicFolderTree_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeView tree || tree.DataContext is not Topic topic)
            return;

        tree.PreviewMouseLeftButtonDown -= SidebarTopicFolderTree_UserInput;
        tree.PreviewMouseLeftButtonDown += SidebarTopicFolderTree_UserInput;
        tree.PreviewKeyDown -= SidebarTopicFolderTree_UserKeyInput;
        tree.PreviewKeyDown += SidebarTopicFolderTree_UserKeyInput;

        if (!_sidebarTopicFolderTrees.TryGetValue(topic.Id, out var nodes))
        {
            nodes = new ObservableCollection<FolderTreeItemViewModel>(BuildSidebarTopicFolderNodes(topic));
            _sidebarTopicFolderTrees[topic.Id] = nodes;
        }

        var previousIgnore = _ignoreSidebarTopicFolderTreeSelection;
        _ignoreSidebarTopicFolderTreeSelection = true;
        try
        {
            if (!ReferenceEquals(tree.ItemsSource, nodes))
                tree.ItemsSource = nodes;
            tree.Visibility = nodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _ignoreSidebarTopicFolderTreeSelection = previousIgnore;
        }
    }

    private void SidebarTopicFolderTree_UserInput(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeView tree)
        {
            _lastSidebarTopicFolderTreeInputTree = tree;
            _lastSidebarTopicFolderTreeInputUtc = DateTime.UtcNow;
        }
    }

    private void SidebarTopicFolderTree_UserKeyInput(object sender, KeyEventArgs e)
    {
        if (sender is not TreeView tree)
            return;

        if (e.Key is Key.Enter or Key.Space or Key.Up or Key.Down or Key.Left or Key.Right)
        {
            _lastSidebarTopicFolderTreeInputTree = tree;
            _lastSidebarTopicFolderTreeInputUtc = DateTime.UtcNow;
        }
    }

    private bool IsUserInitiatedSidebarTopicFolderTreeSelection(TreeView tree)
    {
        if (_isRefreshingSidebarTopicFolderTrees || _sidebarTopicFolderTreeRefreshQueued)
            return false;

        if (!ReferenceEquals(_lastSidebarTopicFolderTreeInputTree, tree))
            return false;

        return DateTime.UtcNow - _lastSidebarTopicFolderTreeInputUtc <= TimeSpan.FromSeconds(2);
    }

    private void SidebarTopicFolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_ignoreSidebarTopicFolderTreeSelection)
            return;

        if (sender is not TreeView tree || tree.DataContext is not Topic topic || e.NewValue is not FolderTreeItemViewModel node)
            return;

        if (!IsUserInitiatedSidebarTopicFolderTreeSelection(tree))
        {
            AppLogger.Info($"Ignored non-user sidebar folder tree selection topicId={topic.Id}; folderId={node.FolderId ?? "(root)"}");
            return;
        }

        OpenTopicFromSidebarTree(topic, node.FolderId);
    }

    private void SidebarTopicRow_DragEnter(object sender, DragEventArgs e) => UpdateSidebarTopicDropEffect(sender, e);
    private void SidebarTopicRow_DragOver(object sender, DragEventArgs e) => UpdateSidebarTopicDropEffect(sender, e);

    private void SidebarTopicRow_DragLeave(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Topic topic)
            StatusText.Text = $"'{topic.Name}' 주제";
    }

    private async void SidebarTopicRow_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Topic topic)
            return;

        if (IsExternalFileDropData(e.Data))
        {
            e.Handled = true;
            await ImportExternalDroppedFilesAsync(e.Data, topic, null, $"{topic.Name} / 루트");
            return;
        }

        if (TryGetDraggedMediaIds(e.Data).Count == 0)
            return;

        MoveDraggedMediaToTopicFolder(e, topic, null, $"{topic.Name} / 루트");
    }

    private void SidebarFolderTreeItem_DragEnter(object sender, DragEventArgs e) => UpdateSidebarFolderTreeDropEffect(sender, e);
    private void SidebarFolderTreeItem_DragOver(object sender, DragEventArgs e) => UpdateSidebarFolderTreeDropEffect(sender, e);

    private void SidebarFolderTreeItem_DragLeave(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FolderTreeItemViewModel node)
            StatusText.Text = node.IsRoot ? "주제 루트" : $"'{node.Name}' 폴더";
    }

    private async void SidebarFolderTreeItem_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FolderTreeItemViewModel node)
            return;

        var tree = FindAncestor<TreeView>(sender as DependencyObject);
        if (tree?.DataContext is not Topic topic)
            return;

        var targetName = node.IsRoot ? $"{topic.Name} / 루트" : $"{topic.Name} / {node.Name}";
        if (IsExternalFileDropData(e.Data))
        {
            e.Handled = true;
            await ImportExternalDroppedFilesAsync(e.Data, topic, node.FolderId, targetName);
            return;
        }

        if (TryGetDraggedMediaIds(e.Data).Count == 0)
            return;

        MoveDraggedMediaToTopicFolder(e, topic, node.FolderId, targetName);
    }

    private void UpdateSidebarTopicDropEffect(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Topic topic)
            SetSidebarMediaDropEffect(e, topic, null, $"{topic.Name} / 루트");
        else
            SetSidebarMediaDropEffect(e, null, null, "주제");
    }

    private void UpdateSidebarFolderTreeDropEffect(object sender, DragEventArgs e)
    {
        var tree = FindAncestor<TreeView>(sender as DependencyObject);
        var topic = tree?.DataContext as Topic;
        var node = (sender as FrameworkElement)?.DataContext as FolderTreeItemViewModel;
        var targetName = topic == null
            ? "사이드바 폴더"
            : node == null || node.IsRoot
                ? $"{topic.Name} / 루트"
                : $"{topic.Name} / {node.Name}";

        SetSidebarMediaDropEffect(e, topic, node?.FolderId, targetName);
    }

    private void SetSidebarMediaDropEffect(DragEventArgs e, Topic? targetTopic, string? targetFolderId, string targetName)
    {
        if (IsExternalFileDropData(e.Data))
        {
            SetExternalImportDropEffect(e, targetTopic, targetFolderId, targetName);
            return;
        }

        if (HasDraggedFolder(e.Data))
        {
            e.Effects = DragDropEffects.None;
            StatusText.Text = "사이드바에는 파일만 이동할 수 있습니다. 폴더 이동은 중앙 폴더 트리/폴더 카드에서 진행하세요.";
            e.Handled = true;
            return;
        }

        var mediaIds = TryGetDraggedMediaIds(e.Data);
        if (mediaIds.Count == 0)
            return;

        if (!_isOrderEditMode && CanMoveMediaToTopicFolder(targetTopic, targetFolderId, out var statusText))
        {
            e.Effects = DragDropEffects.Move;
            StatusText.Text = string.IsNullOrWhiteSpace(statusText)
                ? $"놓으면 선택 파일을 '{targetName}' 위치로 이동합니다."
                : statusText;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            if (_isOrderEditMode)
                StatusText.Text = "순서 편집 중에는 사이드바로 파일을 이동할 수 없습니다.";
        }

        e.Handled = true;
    }

    private void OpenTopicFromSidebarTree(Topic topic, string? folderId)
    {
        var normalizedFolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
        var selectedTopicId = SelectedTopicId;
        var currentFolderId = string.IsNullOrWhiteSpace(_currentTopicFolderId) ? null : _currentTopicFolderId;
        if (string.Equals(selectedTopicId, topic.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentFolderId, normalizedFolderId, StringComparison.OrdinalIgnoreCase)
            && MediaView.Visibility == Visibility.Visible)
        {
            return;
        }

        try
        {
            _ignoreTopicSelection = true;
            TopicList.SelectedItem = _sidebarTopics.FirstOrDefault(t => string.Equals(t.Id, topic.Id, StringComparison.OrdinalIgnoreCase));
            _ignoreTopicSelection = false;

            SyncTopicCardSelection();
            _currentTopicFolderId = normalizedFolderId;
            _folderSearchScope = FolderSearchScopeCurrent;
            SyncMainWindowState();
            SyncFolderSearchScopeCombo();
            _currentPage = 1;
            ShowMediaView();
            RefreshMediaTagFilterOptions();
            ReloadMedia();
            StatusText.Text = normalizedFolderId == null
                ? $"'{topic.Name}' 주제 루트로 이동했습니다."
                : $"'{topic.Name}' 주제의 폴더로 이동했습니다.";
        }
        finally
        {
            _ignoreTopicSelection = false;
        }
    }

    private void SyncSidebarTopicFolderTreeSelection()
    {
        var previousIgnore = _ignoreSidebarTopicFolderTreeSelection;
        _ignoreSidebarTopicFolderTreeSelection = true;
        try
        {
            foreach (var nodes in _sidebarTopicFolderTrees.Values)
                ClearFolderTreeSelection(nodes);

            var selectedTopic = SelectedTopic;
            if (selectedTopic == null || string.IsNullOrWhiteSpace(_currentTopicFolderId))
                return;

            if (_sidebarTopicFolderTrees.TryGetValue(selectedTopic.Id, out var selectedNodes))
            {
                ExpandFolderTreePath(selectedNodes, _currentTopicFolderId);
                var target = FindFolderTreeItem(selectedNodes, _currentTopicFolderId);
                if (target != null)
                    target.IsSelected = true;
            }
        }
        finally
        {
            _ignoreSidebarTopicFolderTreeSelection = previousIgnore;
        }
    }

    private IEnumerable<Topic> SortSidebarTopics(IEnumerable<Topic> topics)
    {
        return _sidebarTopicController.SortTopics(topics, _sidebarTopicSortMode);
    }

    private static string NormalizeSidebarTopicSortMode(string? mode)
    {
        return new SidebarTopicController().NormalizeSortMode(mode);
    }

    private string GetSidebarTopicSortLabel()
    {
        return _sidebarTopicController.GetSortLabel(_sidebarTopicSortMode);
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

    private bool TopicMatchesSidebarSearch(Topic topic, string query)
    {
        return _sidebarTopicController.TopicMatchesSearch(topic, query);
    }

    private static bool IsFixedUncategorizedTopic(Topic? topic)
    {
        return SidebarTopicController.IsFixedUncategorizedTopic(topic);
    }

    private static bool IsFixedUncategorizedTopic(TopicCardViewModel? topic)
    {
        return SidebarTopicController.IsFixedUncategorizedTopic(topic);
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

    private TopicFolder? CurrentTopicFolder => _folderNavigationController.GetFolder(_currentTopicFolderId);

    private string BuildCurrentFolderPathText()
    {
        return _folderNavigationController.BuildPathText(SelectedTopic, _currentTopicFolderId);
    }

    private void RefreshFolderBreadcrumbs()
    {
        _folderBreadcrumbItems.Clear();
        foreach (var item in _folderNavigationController.BuildBreadcrumbItems(SelectedTopic, _currentTopicFolderId))
            _folderBreadcrumbItems.Add(item);
    }

    private void BreadcrumbFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FolderBreadcrumbItemViewModel item)
            OpenTopicFolder(item.FolderId);
    }

    private void ResetCurrentTopicFolder()
    {
        _currentTopicFolderId = null;
        _folderSearchScope = FolderSearchScopeCurrent;
        SyncMainWindowState();
        SyncFolderSearchScopeCombo();
        ClearSidebarFolderSelectionWithoutNavigation();
    }

    private void ClearSidebarFolderSelectionWithoutNavigation()
    {
        var previousIgnore = _ignoreSidebarTopicFolderTreeSelection;
        _ignoreSidebarTopicFolderTreeSelection = true;
        try
        {
            foreach (var nodes in _sidebarTopicFolderTrees.Values)
                ClearFolderTreeSelection(nodes);
        }
        finally
        {
            _ignoreSidebarTopicFolderTreeSelection = previousIgnore;
        }
    }

    private void InitializeFolderSearchScopeSelector()
    {
        if (FolderSearchScopeCombo == null)
            return;

        _ignoreFolderSearchScopeChange = true;
        try
        {
            foreach (var item in FolderSearchScopeCombo.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), _folderSearchScope, StringComparison.OrdinalIgnoreCase))
                {
                    FolderSearchScopeCombo.SelectedItem = item;
                    break;
                }
            }

            FolderSearchScopeCombo.SelectedIndex = FolderSearchScopeCombo.SelectedIndex < 0 ? 0 : FolderSearchScopeCombo.SelectedIndex;
        }
        finally
        {
            _ignoreFolderSearchScopeChange = false;
        }
    }

    private static string NormalizeFolderSearchScope(string? scope)
    {
        return MediaFilterController.NormalizeFolderSearchScope(scope);
    }

    private string GetFolderSearchScopeLabel()
    {
        return MediaFilterController.GetFolderSearchScopeLabel(_folderSearchScope);
    }

    private void SyncFolderSearchScopeCombo()
    {
        if (FolderSearchScopeCombo == null)
            return;

        _ignoreFolderSearchScopeChange = true;
        try
        {
            var normalized = NormalizeFolderSearchScope(_folderSearchScope);
            foreach (var item in FolderSearchScopeCombo.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    FolderSearchScopeCombo.SelectedItem = item;
                    break;
                }
            }

            FolderSearchScopeCombo.IsEnabled = SelectedTopic != null;
            FolderSearchScopeCombo.ToolTip = SelectedTopic == null
                ? "주제를 선택하면 폴더 검색 범위를 바꿀 수 있습니다."
                : "검색/필터 범위: 현재 폴더, 하위 포함, 주제 전체";
        }
        finally
        {
            _ignoreFolderSearchScopeChange = false;
        }
    }

    private List<MediaItem> GetMediaForCurrentFolderScope(Topic topic, MediaKind? kind)
    {
        return _mediaFilterController.GetMediaForFolderScope(topic, _currentTopicFolderId, _folderSearchScope, kind);
    }

    private void RefreshCurrentFolderUi()
    {
        try
        {
            _topicFolderCards.Clear();

            var topic = SelectedTopic;
            if (topic == null)
            {
                FolderNavigatorPanel.Visibility = Visibility.Collapsed;
                FolderGridList.Visibility = Visibility.Collapsed;
                RefreshFolderTree(null);
                SyncFolderSearchScopeCombo();
                return;
            }

            FolderNavigatorPanel.Visibility = Visibility.Visible;
            ApplyCenterFolderTreeVisibility(hasTopic: true);

            if (!_folderNavigationController.TryValidateCurrentFolder(topic, _currentTopicFolderId, out var validatedFolderId, out var folderWarning))
            {
                if (!string.IsNullOrWhiteSpace(folderWarning))
                    AppLogger.Warn(folderWarning);
                _currentTopicFolderId = validatedFolderId;
                SyncMainWindowState();
            }

            var folders = _context.Database.GetTopicFolders(topic.Id, _currentTopicFolderId);
            foreach (var folder in folders)
                _topicFolderCards.Add(new TopicFolderCardViewModel(folder));

            FolderGridList.Visibility = _topicFolderCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshFolderBreadcrumbs();
            RefreshFolderTree(topic);
            RefreshSidebarTopicFolderTrees();
            FolderRootButton.IsEnabled = !string.IsNullOrWhiteSpace(_currentTopicFolderId);
            FolderUpButton.IsEnabled = !string.IsNullOrWhiteSpace(_currentTopicFolderId);
            CurrentFolderRenameButton.IsEnabled = !string.IsNullOrWhiteSpace(_currentTopicFolderId);
            CurrentFolderDeleteButton.IsEnabled = !string.IsNullOrWhiteSpace(_currentTopicFolderId);
            NewTopicFolderButton.IsEnabled = true;
            SyncFolderSearchScopeCombo();
            UpdateDropZoneHint(topic);
            // Intentionally quiet in release: this path runs frequently during sidebar/folder synchronization.
        }
        catch (Exception ex)
        {
            AppLogger.Error($"RefreshCurrentFolderUi failed selectedTopicId={SelectedTopicId ?? "(none)"}; currentFolderId={_currentTopicFolderId ?? "(root)"}", ex);
            throw;
        }
    }

    private void OpenTopicFolder(string? folderId)
    {
        var topic = SelectedTopic;
        if (topic == null)
            return;

        var normalizedFolderId = _folderNavigationController.NormalizeFolderId(folderId);
        if (!_folderNavigationController.TryValidateCurrentFolder(topic, normalizedFolderId, out normalizedFolderId, out var folderWarning))
        {
            if (!string.IsNullOrWhiteSpace(folderWarning))
                AppLogger.Warn(folderWarning);
        }

        var currentNormalizedId = _folderNavigationController.NormalizeFolderId(_currentTopicFolderId);
        if (string.Equals(currentNormalizedId, normalizedFolderId, StringComparison.OrdinalIgnoreCase))
            return;

        _currentTopicFolderId = normalizedFolderId;
        SyncMainWindowState();
        if (_isOrderEditMode)
            CancelOrderEditMode("폴더 이동으로 순서 편집 모드를 종료했습니다.");
        _currentPage = 1;
        RefreshMediaTagFilterOptions();
        ReloadMedia();
    }


    private void RefreshFolderTree(Topic? topic)
    {
        _ignoreFolderTreeSelection = true;
        try
        {
            _folderTreeItems.Clear();

            if (topic == null)
            {
                ApplyCenterFolderTreeVisibility(hasTopic: false);
                FolderTreeSummaryText.Text = "주제를 선택하면 폴더 트리가 표시됩니다.";
                return;
            }

            var rootMediaCount = _context.Database.GetMediaInFolder(topic.Id, null, null, includeDescendants: false).Count;
            var rootChildFolderCount = _context.Database.GetTopicFolders(topic.Id, null).Count;
            var root = new FolderTreeItemViewModel(
                topic.Name,
                null,
                "⌂",
                "주제 루트",
                isRoot: true,
                canRenameOrDelete: false,
                mediaCount: rootMediaCount,
                childFolderCount: rootChildFolderCount);

            var visitedFolderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddFolderTreeChildren(root, topic.Id, null, 0, visitedFolderIds);
            root.IsExpanded = true;
            _folderTreeItems.Add(root);
            ApplyCenterFolderTreeVisibility(hasTopic: true);
            var totalTopicFileCount = _context.Database.GetMediaInFolder(topic.Id, null, null, includeDescendants: true).Count;
            var totalTopicFolderCount = CountTreeFoldersSafe(root);
            FolderTreeSummaryText.Text = $"{topic.Name} · 파일 {totalTopicFileCount:N0}개 · 폴더 {totalTopicFolderCount:N0}개";
            SyncFolderTreeSelection();
            // Intentionally quiet in release: successful folder tree refreshes are expected and frequent.
        }
        catch (Exception ex)
        {
            AppLogger.Error($"FolderTree.Refresh failed topicId={topic?.Id ?? "(null)"}; currentFolderId={_currentTopicFolderId ?? "(root)"}", ex);
            _folderTreeItems.Clear();
            ApplyCenterFolderTreeVisibility(hasTopic: false);
            FolderTreeSummaryText.Text = "폴더 트리를 불러오지 못했습니다. 로그를 확인하세요.";
            StatusText.Text = "폴더 트리 오류가 기록되었습니다. 앱은 계속 사용할 수 있습니다.";
        }
        finally
        {
            _ignoreFolderTreeSelection = false;
        }
    }

    private void ToggleCenterFolderTree_Click(object sender, RoutedEventArgs e)
    {
        _isCenterFolderTreeVisible = !_isCenterFolderTreeVisible;
        ApplyCenterFolderTreeVisibility(SelectedTopic != null);
        StatusText.Text = _isCenterFolderTreeVisible
            ? "중앙 폴더 트리를 표시합니다. 사이드바 폴더 트리도 계속 사용할 수 있습니다."
            : "중앙 폴더 트리를 숨겼습니다. 왼쪽 사이드바 폴더 트리를 사용하세요.";
    }

    private void ApplyCenterFolderTreeVisibility(bool hasTopic)
    {
        var shouldShow = hasTopic && _isCenterFolderTreeVisible;
        FolderTreePanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        FolderTreeColumn.Width = shouldShow ? new GridLength(252) : new GridLength(0);
        FolderTreeSpacerColumn.Width = shouldShow ? new GridLength(14) : new GridLength(0);

        CenterFolderTreeToggleButton.IsEnabled = hasTopic;
        CenterFolderTreeToggleButton.Content = shouldShow ? "트리 숨김" : "트리 보기";
        CenterFolderTreeToggleButton.ToolTip = hasTopic
            ? (shouldShow
                ? "중앙 폴더 트리를 숨깁니다. 사이드바 폴더 트리는 계속 표시됩니다."
                : "중앙 파일 목록 왼쪽의 보조 폴더 트리를 펼칩니다.")
            : "주제를 선택하면 중앙 폴더 트리를 사용할 수 있습니다.";
    }

    private void AddFolderTreeChildren(FolderTreeItemViewModel parentNode, string topicId, string? parentFolderId, int depth, HashSet<string> visitedFolderIds)
    {
        if (depth > 40)
        {
            AppLogger.Warn($"FolderTree depth limit reached topicId={topicId}; parentFolderId={parentFolderId ?? "(root)"}");
            return;
        }

        var folders = _context.Database.GetTopicFolders(topicId, parentFolderId);

        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder.Id))
            {
                AppLogger.Warn($"FolderTree skipped folder with empty id topicId={topicId}; parentFolderId={parentFolderId ?? "(root)"}");
                continue;
            }

            if (!visitedFolderIds.Add(folder.Id))
            {
                AppLogger.Warn($"FolderTree cycle/duplicate detected. skipped folderId={folder.Id}; parentFolderId={parentFolderId ?? "(root)"}; topicId={topicId}");
                continue;
            }

            var node = new FolderTreeItemViewModel(
                folder.Name,
                folder.Id,
                "📁",
                $"{folder.Name} 폴더 · 클릭하면 이동 · 파일을 드롭하면 이동",
                isRoot: false,
                canRenameOrDelete: true,
                mediaCount: folder.MediaCount,
                childFolderCount: folder.ChildFolderCount);
            parentNode.Children.Add(node);
            AddFolderTreeChildren(node, topicId, folder.Id, depth + 1, visitedFolderIds);
        }
    }

    private static int CountTreeFoldersSafe(FolderTreeItemViewModel root)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return CountTreeFoldersSafe(root, visited, 0);
    }

    private static int CountTreeFoldersSafe(FolderTreeItemViewModel node, HashSet<string> visitedFolderIds, int depth)
    {
        if (depth > 80)
            return 0;

        var count = 0;
        foreach (var child in node.Children)
        {
            if (!string.IsNullOrWhiteSpace(child.FolderId) && !visitedFolderIds.Add(child.FolderId))
                continue;

            count++;
            count += CountTreeFoldersSafe(child, visitedFolderIds, depth + 1);
        }

        return count;
    }

    private void SyncFolderTreeSelection()
    {
        var target = FindFolderTreeItem(_folderTreeItems, _currentTopicFolderId);
        if (target == null)
            return;

        var previousIgnore = _ignoreFolderTreeSelection;
        _ignoreFolderTreeSelection = true;
        try
        {
            ClearFolderTreeSelection(_folderTreeItems);
            ExpandFolderTreePath(_folderTreeItems, target.FolderId);
            target.IsSelected = true;
        }
        finally
        {
            _ignoreFolderTreeSelection = previousIgnore;
        }
    }

    private static FolderTreeItemViewModel? FindFolderTreeItem(IEnumerable<FolderTreeItemViewModel> nodes, string? folderId)
    {
        var normalizedId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
        foreach (var node in nodes)
        {
            if (string.Equals(node.FolderId, normalizedId, StringComparison.OrdinalIgnoreCase))
                return node;

            var child = FindFolderTreeItem(node.Children, normalizedId);
            if (child != null)
                return child;
        }

        return null;
    }

    private static void ClearFolderTreeSelection(IEnumerable<FolderTreeItemViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsSelected = false;
            ClearFolderTreeSelection(node.Children);
        }
    }

    private static bool ExpandFolderTreePath(IEnumerable<FolderTreeItemViewModel> nodes, string? folderId)
    {
        var normalizedId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
        foreach (var node in nodes)
        {
            if (string.Equals(node.FolderId, normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                node.IsExpanded = true;
                return true;
            }

            if (ExpandFolderTreePath(node.Children, normalizedId))
            {
                node.IsExpanded = true;
                return true;
            }
        }

        return false;
    }

    private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_ignoreFolderTreeSelection)
            return;

        if (e.NewValue is FolderTreeItemViewModel node)
            OpenTopicFolder(node.FolderId);
    }

    private void FolderTreeItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _folderDragStartPoint = e.GetPosition(this);
        _folderDragSourceId = (sender as FrameworkElement)?.DataContext is FolderTreeItemViewModel { IsRoot: false } node
            ? node.FolderId
            : null;
    }

    private void FolderTreeItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FolderTreeItemViewModel node || node.IsRoot)
            return;

        StartTopicFolderDragIfNeeded(sender, e, node.FolderId, node.Name);
    }

    private void FolderTreeItem_DragEnter(object sender, DragEventArgs e) => UpdateFolderTreeDropEffect(sender, e);
    private void FolderTreeItem_DragOver(object sender, DragEventArgs e) => UpdateFolderTreeDropEffect(sender, e);

    private void FolderTreeItem_DragLeave(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FolderTreeItemViewModel node)
            StatusText.Text = node.IsRoot ? "주제 루트" : $"'{node.Name}' 폴더";
    }

    private async void FolderTreeItem_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FolderTreeItemViewModel node)
            return;

        if (IsExternalFileDropData(e.Data))
        {
            e.Handled = true;
            var topic = SelectedTopic;
            await ImportExternalDroppedFilesAsync(e.Data, topic, node.FolderId, node.IsRoot ? "루트" : node.Name);
            return;
        }

        if (HasDraggedFolder(e.Data))
            MoveDraggedFolderToParent(e, node.FolderId, node.IsRoot ? "루트" : node.Name);
        else
            MoveDraggedMediaToFolder(e, node.FolderId, node.IsRoot ? "루트" : node.Name);
    }

    private void UpdateFolderTreeDropEffect(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FolderTreeItemViewModel node)
            SetFolderDropEffect(e, node.FolderId, node.IsRoot ? "루트" : node.Name);
        else
            SetFolderDropEffect(e, null, "폴더 트리");
    }

    private void FolderTree_Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is FolderTreeItemViewModel node)
            OpenTopicFolder(node.FolderId);
    }

    private void FolderTree_NewChild_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is not FolderTreeItemViewModel node || SelectedTopic == null)
            return;

        var topic = SelectedTopic;
        var suggestedName = GetUniqueTopicFolderName(topic.Id, node.FolderId, "새 폴더");
        var dlg = new InputDialog("새 하위 폴더", $"'{node.Name}' 아래에 만들 폴더 이름을 입력하세요.", suggestedName) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value))
            return;

        try
        {
            var folderName = GetUniqueTopicFolderName(topic.Id, node.FolderId, dlg.Value.Trim());
            var created = _context.Database.CreateTopicFolder(topic.Id, node.FolderId, folderName);
            _context.ActivityLogs.Add("folder", "하부 폴더 생성", $"{topic.Name} / {created.Name}", created.Id, created.Name);
            RefreshMediaTagFilterOptions();
            OpenTopicFolder(created.Id);
            StatusText.Text = $"'{node.Name}' 아래에 '{created.Name}' 폴더를 만들고 해당 폴더로 이동했습니다.";
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "폴더를 만들 수 없습니다.\n\n" + ex.Message, "새 하위 폴더", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FolderTree_Rename_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is not FolderTreeItemViewModel node || string.IsNullOrWhiteSpace(node.FolderId))
            return;

        var folder = _context.Database.GetTopicFolderById(node.FolderId);
        if (folder != null)
            RenameTopicFolder(folder);
    }

    private void FolderTree_Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is not FolderTreeItemViewModel node || string.IsNullOrWhiteSpace(node.FolderId))
            return;

        var folder = _context.Database.GetTopicFolderById(node.FolderId);
        if (folder != null)
            DeleteTopicFolder(folder, navigateToParent: string.Equals(folder.Id, _currentTopicFolderId, StringComparison.OrdinalIgnoreCase));
    }

    private void FolderRoot_Click(object sender, RoutedEventArgs e) => OpenTopicFolder(null);

    private void FolderUp_Click(object sender, RoutedEventArgs e)
    {
        OpenTopicFolder(_folderNavigationController.GetParentFolderId(_currentTopicFolderId));
    }

    private string GetUniqueTopicFolderName(string topicId, string? parentFolderId, string requestedName)
    {
        return _folderNavigationController.GetUniqueFolderName(topicId, parentFolderId, requestedName);
    }

    private void NewTopicFolder_Click(object sender, RoutedEventArgs e)
    {
        var topic = SelectedTopic;
        if (topic == null)
        {
            MessageDialog.Show(this, "먼저 주제를 선택해 주세요.", "새 폴더", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var suggestedName = GetUniqueTopicFolderName(topic.Id, _currentTopicFolderId, "새 폴더");
        var dlg = new InputDialog("새 폴더", "폴더 이름을 입력하세요.", suggestedName) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value))
            return;

        try
        {
            var parentFolderId = _currentTopicFolderId;
            var folderName = GetUniqueTopicFolderName(topic.Id, parentFolderId, dlg.Value.Trim());
            var created = _context.Database.CreateTopicFolder(topic.Id, parentFolderId, folderName);
            _context.ActivityLogs.Add("folder", "하부 폴더 생성", $"{topic.Name} / {created.Name}", created.Id, created.Name);
            OpenTopicFolder(created.Id);
            StatusText.Text = $"'{created.Name}' 폴더를 만들고 해당 폴더로 이동했습니다.";
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "폴더를 만들 수 없습니다.\n\n" + ex.Message, "새 폴더", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RenameCurrentFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = CurrentTopicFolder;
        if (folder == null)
            return;
        RenameTopicFolder(folder);
    }

    private void DeleteCurrentFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = CurrentTopicFolder;
        if (folder == null)
            return;
        DeleteTopicFolder(folder, navigateToParent: true);
    }

    private void FolderCard_Click(object sender, RoutedEventArgs e)
    {
        if (_isOrderEditMode)
        {
            StatusText.Text = "순서 편집 중에는 폴더를 열지 않습니다. 핸들 또는 카드를 드래그해 폴더 순서를 변경하세요.";
            return;
        }

        if ((sender as FrameworkElement)?.Tag is TopicFolderCardViewModel folder)
            OpenTopicFolder(folder.Id);
    }

    private void FolderCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _folderDragStartPoint = e.GetPosition(this);
        _folderDragSourceId = (sender as FrameworkElement)?.Tag is TopicFolderCardViewModel folder ? folder.Id : null;
    }

    private void FolderCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TopicFolderCardViewModel folder)
            return;

        StartTopicFolderDragIfNeeded(sender, e, folder.Id, folder.Name);
    }

    private void FolderCard_Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is TopicFolderCardViewModel folder)
            OpenTopicFolder(folder.Id);
    }

    private void FolderCard_Rename_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is TopicFolderCardViewModel card)
        {
            var folder = _context.Database.GetTopicFolderById(card.Id);
            if (folder != null)
                RenameTopicFolder(folder);
        }
    }

    private void FolderCard_Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is TopicFolderCardViewModel card)
        {
            var folder = _context.Database.GetTopicFolderById(card.Id);
            if (folder != null)
                DeleteTopicFolder(folder, navigateToParent: false);
        }
    }

    private void RenameTopicFolder(TopicFolder folder)
    {
        var dlg = new InputDialog("폴더 이름 변경", "새 폴더 이름을 입력하세요.", folder.Name) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value))
            return;

        try
        {
            var oldName = folder.Name;
            _context.Database.RenameTopicFolder(folder.Id, dlg.Value.Trim());
            _context.ActivityLogs.Add("folder", "하부 폴더 이름 변경", $"{oldName} → {dlg.Value.Trim()}", folder.Id, dlg.Value.Trim());
            if (string.Equals(folder.Id, _currentTopicFolderId, StringComparison.OrdinalIgnoreCase))
                ReloadMedia();
            else
                RefreshCurrentFolderUi();
            StatusText.Text = $"'{oldName}' 폴더 이름을 변경했습니다.";
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "폴더 이름을 변경할 수 없습니다.\n\n" + ex.Message, "폴더 이름 변경", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteTopicFolder(TopicFolder folder, bool navigateToParent)
    {
        folder = _context.Database.GetTopicFolderById(folder.Id) ?? folder;
        var detail = $"현재 폴더 바로 아래 파일 {folder.MediaCount:N0}개, 하위 폴더 {folder.ChildFolderCount:N0}개가 있습니다.";
        var confirm = MessageDialog.Show(this,
            $"'{folder.Name}' 폴더를 삭제할까요?\n\n{detail}\n\n폴더 안의 파일과 하위 폴더는 삭제하지 않고 상위 폴더로 이동합니다.",
            "폴더 삭제",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var parentId = folder.ParentFolderId;
            _context.Database.DeleteTopicFolderMoveContentsToParent(folder.Id);
            _context.ActivityLogs.Add("folder", "하부 폴더 삭제", folder.Name, folder.Id, folder.Name);
            if (navigateToParent)
                _currentTopicFolderId = parentId;
            RefreshMediaTagFilterOptions();
            ReloadMedia();
            StatusText.Text = $"'{folder.Name}' 폴더를 삭제했습니다. 내부 항목은 상위 폴더로 이동했습니다.";
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "폴더를 삭제할 수 없습니다.\n\n" + ex.Message, "폴더 삭제", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FolderCard_DragEnter(object sender, DragEventArgs e)
    {
        if (_isOrderEditMode && e.Data.GetDataPresent(typeof(TopicFolderCardViewModel)))
            return;

        UpdateFolderDropEffect(sender, e);
    }

    private void FolderCard_DragOver(object sender, DragEventArgs e)
    {
        if (_isOrderEditMode && e.Data.GetDataPresent(typeof(TopicFolderCardViewModel)))
            return;

        UpdateFolderDropEffect(sender, e);
    }
    private void FolderCard_DragLeave(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TopicFolderCardViewModel folder)
            StatusText.Text = $"'{folder.Name}' 폴더";
    }

    private async void FolderCard_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TopicFolderCardViewModel folder)
            return;

        if (IsExternalFileDropData(e.Data))
        {
            e.Handled = true;
            await ImportExternalDroppedFilesAsync(e.Data, SelectedTopic, folder.Id, folder.Name);
            return;
        }

        if (HasDraggedFolder(e.Data))
            MoveDraggedFolderToParent(e, folder.Id, folder.Name);
        else
            MoveDraggedMediaToFolder(e, folder.Id, folder.Name);
    }

    private void FolderRoot_DragOver(object sender, DragEventArgs e)
    {
        SetFolderDropEffect(e, null, "루트");
    }

    private async void FolderRoot_Drop(object sender, DragEventArgs e)
    {
        if (IsExternalFileDropData(e.Data))
        {
            e.Handled = true;
            await ImportExternalDroppedFilesAsync(e.Data, SelectedTopic, null, "루트");
            return;
        }

        if (HasDraggedFolder(e.Data))
            MoveDraggedFolderToParent(e, null, "루트");
        else
            MoveDraggedMediaToFolder(e, null, "루트");
    }

    private void FolderUp_DragOver(object sender, DragEventArgs e)
    {
        var parentId = _folderNavigationController.GetParentFolderId(_currentTopicFolderId);
        var parentName = _folderNavigationController.GetFolderDisplayName(parentId, "루트");
        SetFolderDropEffect(e, parentId, parentName);
    }

    private async void FolderUp_Drop(object sender, DragEventArgs e)
    {
        var parentId = _folderNavigationController.GetParentFolderId(_currentTopicFolderId);
        var parentName = _folderNavigationController.GetFolderDisplayName(parentId, "루트");
        if (IsExternalFileDropData(e.Data))
        {
            e.Handled = true;
            await ImportExternalDroppedFilesAsync(e.Data, SelectedTopic, parentId, parentName);
            return;
        }

        if (HasDraggedFolder(e.Data))
            MoveDraggedFolderToParent(e, parentId, parentName);
        else
            MoveDraggedMediaToFolder(e, parentId, parentName);
    }

    private void UpdateFolderDropEffect(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TopicFolderCardViewModel folder)
            SetFolderDropEffect(e, folder.Id, folder.Name);
        else
            SetFolderDropEffect(e, null, "폴더");
    }

    private void SetFolderDropEffect(DragEventArgs e, string? targetFolderId, string targetName)
    {
        if (IsExternalFileDropData(e.Data))
        {
            SetExternalImportDropEffect(e, SelectedTopic, targetFolderId, targetName);
            return;
        }

        if (HasDraggedFolder(e.Data))
        {
            if (CanMoveDraggedFolderToParent(e.Data, targetFolderId, out var statusText))
            {
                e.Effects = DragDropEffects.Move;
                StatusText.Text = statusText;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                if (!string.IsNullOrWhiteSpace(statusText))
                    StatusText.Text = statusText;
            }

            e.Handled = true;
            return;
        }

        if (TryGetDraggedMediaIds(e.Data).Count > 0 && SelectedTopic != null && !_isOrderEditMode)
        {
            e.Effects = DragDropEffects.Move;
            StatusText.Text = $"놓으면 선택 파일을 '{targetName}' 위치로 이동합니다.";
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void StartTopicFolderDragIfNeeded(object sender, MouseEventArgs e, string? folderId, string folderName)
    {
        if (e.LeftButton != MouseButtonState.Pressed || string.IsNullOrWhiteSpace(folderId) || _isOrderEditMode)
            return;

        if (!string.Equals(_folderDragSourceId, folderId, StringComparison.OrdinalIgnoreCase))
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _folderDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _folderDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var folder = _context.Database.GetTopicFolderById(folderId);
        if (folder == null)
            return;

        var data = _mediaDragDropController.CreateFolderDragData(folder.Id);
        StatusText.Text = $"'{folder.Name}' 폴더 이동 중 · 다른 폴더나 루트에 놓으세요.";
        var result = DragDrop.DoDragDrop(sender as DependencyObject ?? this, data, DragDropEffects.Move);
        if (result != DragDropEffects.Move)
            StatusText.Text = $"'{folder.Name}' 폴더 이동이 취소되었습니다.";
    }

    private bool HasDraggedFolder(IDataObject data) => _mediaDragDropController.HasDraggedFolder(data);

    private string? TryGetDraggedFolderId(IDataObject data) => _mediaDragDropController.GetDraggedFolderId(data);

    private bool CanMoveDraggedFolderToParent(IDataObject data, string? targetParentFolderId, out string statusText)
    {
        var sourceFolderId = TryGetDraggedFolderId(data);
        return CanMoveTopicFolderToParent(sourceFolderId, targetParentFolderId, out statusText);
    }

    private bool CanMoveTopicFolderToParent(string? sourceFolderId, string? targetParentFolderId, out string statusText)
    {
        return _folderNavigationController.CanMoveTopicFolderToParent(SelectedTopic, _isOrderEditMode, sourceFolderId, targetParentFolderId, out statusText);
    }

    private void MoveDraggedFolderToParent(DragEventArgs e, string? targetParentFolderId, string targetName)
    {
        var sourceFolderId = TryGetDraggedFolderId(e.Data);
        if (!CanMoveTopicFolderToParent(sourceFolderId, targetParentFolderId, out var statusText))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(statusText))
                StatusText.Text = statusText;
            return;
        }

        MoveTopicFolderToParent(sourceFolderId!, targetParentFolderId, targetName);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void MoveTopicFolderToParent(string sourceFolderId, string? targetParentFolderId, string targetName)
    {
        var sourceFolder = _context.Database.GetTopicFolderById(sourceFolderId);
        if (sourceFolder == null)
            return;

        if (!CanMoveTopicFolderToParent(sourceFolderId, targetParentFolderId, out var statusText))
        {
            MessageDialog.Show(this, statusText, "폴더 이동", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var normalizedTarget = string.IsNullOrWhiteSpace(targetParentFolderId) ? null : targetParentFolderId;
            _context.Database.MoveTopicFolder(sourceFolderId, normalizedTarget);
            _context.ActivityLogs.Add("folder", "하부 폴더 위치 이동", $"{sourceFolder.Name} → {targetName}", sourceFolder.Id, sourceFolder.Name);
            RefreshMediaTagFilterOptions();
            ReloadMedia();
            StatusText.Text = $"'{sourceFolder.Name}' 폴더를 '{targetName}' 위치로 이동했습니다.";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"MoveTopicFolder failed sourceFolderId={sourceFolderId}; targetParentFolderId={targetParentFolderId ?? "(root)"}", ex);
            MessageDialog.Show(this, "폴더를 이동할 수 없습니다.\n\n" + ex.Message, "폴더 이동", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MoveDraggedMediaToFolder(DragEventArgs e, string? targetFolderId, string targetName)
    {
        var topic = SelectedTopic;
        if (topic == null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        MoveDraggedMediaToTopicFolder(e, topic, targetFolderId, targetName);
    }

    private void MoveDraggedMediaToTopicFolder(DragEventArgs e, Topic targetTopic, string? targetFolderId, string targetName)
    {
        var ids = TryGetDraggedMediaIds(e.Data);
        if (ids.Count == 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!CanMoveMediaToTopicFolder(targetTopic, targetFolderId, out var statusText))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(statusText))
                StatusText.Text = statusText;
            return;
        }

        MoveMediaIdsToTopicFolder(ids, targetTopic, targetFolderId, targetName);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private bool CanMoveMediaToTopicFolder(Topic? targetTopic, string? targetFolderId, out string statusText)
    {
        statusText = string.Empty;
        if (_isOrderEditMode)
        {
            statusText = "순서 편집 중에는 파일을 이동할 수 없습니다.";
            return false;
        }

        if (targetTopic == null)
        {
            statusText = "이동할 주제를 확인할 수 없습니다.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(targetFolderId))
        {
            var targetFolder = _context.Database.GetTopicFolderById(targetFolderId);
            if (targetFolder == null)
            {
                statusText = "이동할 폴더를 찾을 수 없습니다.";
                return false;
            }

            if (!string.Equals(targetFolder.TopicId, targetTopic.Id, StringComparison.OrdinalIgnoreCase))
            {
                statusText = "이 주제에 속하지 않는 폴더로는 이동할 수 없습니다.";
                return false;
            }
        }

        return true;
    }

    private List<string> TryGetDraggedMediaIds(IDataObject data) => _mediaDragDropController.GetDraggedMediaIds(data);

    private bool IsExternalFileDropData(IDataObject data) => _mediaDragDropController.IsExternalFileDropData(data);

    private bool TryGetExternalFileDropPaths(IDataObject data, out List<string> files, out List<string> folders)
    {
        return _mediaDragDropController.TryGetExternalFileDropPaths(data, out files, out folders);
    }

    private void SetExternalImportDropEffect(DragEventArgs e, Topic? targetTopic, string? targetFolderId, string targetName)
    {
        if (!TryGetExternalFileDropPaths(e.Data, out var files, out var folders))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (_isImporting)
        {
            e.Effects = DragDropEffects.None;
            StatusText.Text = "파일 가져오기가 진행 중입니다. 완료 후 다시 시도해주세요.";
            e.Handled = true;
            return;
        }

        if (_isOrderEditMode)
        {
            e.Effects = DragDropEffects.None;
            StatusText.Text = "순서 편집 중에는 외부 파일을 추가할 수 없습니다.";
            e.Handled = true;
            return;
        }

        if (files.Count == 0)
        {
            e.Effects = DragDropEffects.None;
            StatusText.Text = folders.Count > 0
                ? "외부 폴더 드롭은 아직 지원하지 않습니다. 폴더 안의 파일을 선택해 드래그해 주세요."
                : "가져올 수 있는 파일이 없습니다.";
            e.Handled = true;
            return;
        }

        if (targetTopic != null && !string.IsNullOrWhiteSpace(targetFolderId))
        {
            var folder = _context.Database.GetTopicFolderById(targetFolderId);
            if (folder == null || !string.Equals(folder.TopicId, targetTopic.Id, StringComparison.OrdinalIgnoreCase))
            {
                e.Effects = DragDropEffects.None;
                StatusText.Text = "이 주제에 속하지 않는 폴더에는 파일을 추가할 수 없습니다.";
                e.Handled = true;
                return;
            }
        }

        e.Effects = DragDropEffects.Copy;
        StatusText.Text = folders.Count > 0
            ? $"놓으면 파일 {files.Count:N0}개를 '{targetName}'에 추가합니다. 폴더 {folders.Count:N0}개는 제외됩니다."
            : $"놓으면 파일 {files.Count:N0}개를 '{targetName}'에 추가합니다.";
        e.Handled = true;
    }

    private async Task ImportExternalDroppedFilesAsync(IDataObject data, Topic? targetTopic, string? targetFolderId, string targetName, bool openTargetAfterImport = true)
    {
        if (!TryGetExternalFileDropPaths(data, out var files, out var folders))
        {
            StatusText.Text = "가져올 파일이 없습니다.";
            return;
        }

        if (files.Count == 0)
        {
            StatusText.Text = folders.Count > 0
                ? "외부 폴더 드롭은 아직 지원하지 않습니다. 폴더 안의 파일을 선택해 드래그해 주세요."
                : "가져올 파일이 없습니다.";
            return;
        }

        if (folders.Count > 0)
            StatusText.Text = $"외부 폴더 {folders.Count:N0}개는 제외하고 파일 {files.Count:N0}개를 가져옵니다.";

        await ImportFilesAsync(files, targetTopic, targetFolderId, targetName, openTargetAfterImport);
    }

    private string? NormalizeImportTargetFolderId(Topic topic, string? folderId)
    {
        if (string.IsNullOrWhiteSpace(folderId))
            return null;

        var folder = _context.Database.GetTopicFolderById(folderId);
        return folder != null && string.Equals(folder.TopicId, topic.Id, StringComparison.OrdinalIgnoreCase)
            ? folder.Id
            : null;
    }

    private string BuildFolderPathText(Topic topic, string? folderId)
    {
        if (string.IsNullOrWhiteSpace(folderId))
            return $"{topic.Name} / 루트";

        var names = new Stack<string>();
        var currentId = folderId;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(currentId) && guard++ < 40)
        {
            var folder = _context.Database.GetTopicFolderById(currentId);
            if (folder == null || !string.Equals(folder.TopicId, topic.Id, StringComparison.OrdinalIgnoreCase))
                break;

            names.Push(folder.Name);
            currentId = folder.ParentFolderId;
        }

        return names.Count == 0 ? $"{topic.Name} / 루트" : $"{topic.Name} / " + string.Join(" / ", names);
    }

    private string GetCurrentExternalImportTargetName()
    {
        return SelectedTopic == null ? "미분류 / 루트" : BuildCurrentFolderPathText();
    }

    private bool TryHandleExternalTopicExplorerDragOver(object sender, DragEventArgs e)
    {
        if (!IsExternalFileDropData(e.Data))
            return false;

        if (TryGetTopicExplorerDropTarget(sender, e, out var targetTopic))
            SetExternalImportDropEffect(e, targetTopic, null, $"{targetTopic.Name} / 루트");
        else
        {
            e.Effects = DragDropEffects.None;
            StatusText.Text = "주제 카드 위에 파일을 놓으면 해당 주제 루트에 추가됩니다.";
            e.Handled = true;
        }

        return true;
    }

    private bool TryGetTopicExplorerDropTarget(object sender, DragEventArgs e, out Topic topic)
    {
        topic = null!;

        if (e.OriginalSource is DependencyObject source)
        {
            var container = FindAncestor<ListBoxItem>(source);
            if (container?.DataContext is TopicCardViewModel card)
            {
                var matched = _topics.FirstOrDefault(t => string.Equals(t.Id, card.Id, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    topic = matched;
                    return true;
                }
            }
            else if (container?.DataContext is Topic directTopic)
            {
                topic = directTopic;
                return true;
            }
        }

        if (sender is ListBox { SelectedItem: TopicCardViewModel selectedCard })
        {
            var matched = _topics.FirstOrDefault(t => string.Equals(t.Id, selectedCard.Id, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                topic = matched;
                return true;
            }
        }

        return false;
    }

    private List<MediaCardViewModel> GetSelectedMediaCardsOrWarn(string actionTitle)
    {
        var selected = GalleryList.SelectedItems.Cast<MediaCardViewModel>().ToList();
        if (selected.Count == 0)
            MessageDialog.Show(this, "이동할 파일을 먼저 선택하세요.\n여러 파일은 Ctrl 또는 Shift로 함께 선택할 수 있습니다.", actionTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        return selected;
    }

    private void MoveMediaIdsToCurrentTopicFolder(IEnumerable<string> mediaIds, string? targetFolderId, string targetName)
    {
        var topic = SelectedTopic;
        if (topic == null)
        {
            MessageDialog.Show(this, "먼저 주제를 선택해 주세요.", "폴더 이동", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MoveMediaIdsToTopicFolder(mediaIds, topic, targetFolderId, targetName);
    }

    private void MoveMediaIdsToTopicFolder(IEnumerable<string> mediaIds, Topic targetTopic, string? targetFolderId, string targetName)
    {
        if (!CanMoveMediaToTopicFolder(targetTopic, targetFolderId, out var validationMessage))
        {
            MessageDialog.Show(this, validationMessage, "폴더 이동", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ids = mediaIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0)
            return;

        var normalizedTargetFolderId = string.IsNullOrWhiteSpace(targetFolderId) ? null : targetFolderId;
        var moveIds = ids
            .Where(id =>
            {
                var item = _context.Database.GetMediaById(id);
                if (item == null)
                    return false;

                var currentFolderId = string.IsNullOrWhiteSpace(item.FolderId) ? null : item.FolderId;
                return !string.Equals(item.TopicId, targetTopic.Id, StringComparison.OrdinalIgnoreCase)
                       || !string.Equals(currentFolderId, normalizedTargetFolderId, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (moveIds.Count == 0)
        {
            StatusText.Text = $"선택 항목은 이미 '{targetName}' 위치에 있습니다.";
            return;
        }

        try
        {
            _context.Database.MoveMediaToFolder(moveIds, targetTopic.Id, normalizedTargetFolderId);
            _context.ActivityLogs.Add("folder", "파일 폴더 이동", $"{moveIds.Count:N0}개 파일 → {targetName}", normalizedTargetFolderId ?? targetTopic.Id, targetName);

            var currentTopicId = SelectedTopicId;
            ReloadTopics();
            RefreshMediaTagFilterOptions();
            ReloadMedia();

            StatusText.Text = string.Equals(currentTopicId, targetTopic.Id, StringComparison.OrdinalIgnoreCase)
                ? $"폴더 이동 완료: {moveIds.Count:N0}개 파일 → {targetName}"
                : $"주제 이동 완료: {moveIds.Count:N0}개 파일 → {targetName}";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"MoveMediaIdsToTopicFolder failed targetTopicId={targetTopic.Id}; targetFolderId={normalizedTargetFolderId ?? "(root)"}", ex);
            MessageDialog.Show(this, "파일을 이동할 수 없습니다.\n\n" + ex.Message, "폴더 이동", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MoveSelectedMediaToFolder(string? targetFolderId, string targetName)
    {
        var selected = GetSelectedMediaCardsOrWarn("폴더 이동");
        if (selected.Count == 0)
            return;

        MoveMediaIdsToCurrentTopicFolder(selected.Select(card => card.Id), targetFolderId, targetName);
    }

    private void MoveToFolderMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu)
            return;

        menu.Items.Clear();
        var topic = SelectedTopic;
        var selectedCount = GalleryList.SelectedItems.Cast<MediaCardViewModel>().Count();
        if (topic == null || selectedCount == 0)
        {
            var empty = new MenuItem { Header = selectedCount == 0 ? "먼저 파일을 선택하세요" : "먼저 주제를 선택하세요", IsEnabled = false };
            menu.Items.Add(empty);
            return;
        }

        AddMoveToFolderMenuItem(menu, "⌂ 루트로 이동", null, "루트", !string.IsNullOrWhiteSpace(_currentTopicFolderId));

        var current = CurrentTopicFolder;
        if (current != null)
        {
            var parentName = string.IsNullOrWhiteSpace(current.ParentFolderId)
                ? "루트"
                : (_context.Database.GetTopicFolderById(current.ParentFolderId)?.Name ?? "상위 폴더");
            AddMoveToFolderMenuItem(menu, "↑ 상위 폴더로 이동", current.ParentFolderId, parentName);
        }

        var folders = GetFlattenedTopicFolders(topic.Id);
        if (folders.Count > 0)
            menu.Items.Add(new Separator());

        foreach (var folder in folders)
        {
            if (string.Equals(folder.Folder.Id, _currentTopicFolderId, StringComparison.OrdinalIgnoreCase))
                continue;

            AddMoveToFolderMenuItem(menu, "📁 " + folder.Path, folder.Folder.Id, folder.Folder.Name);
        }
    }

    private void AddMoveToFolderMenuItem(MenuItem parent, string header, string? folderId, string targetName, bool isEnabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            Tag = new FolderMoveTarget(folderId, targetName),
            IsEnabled = isEnabled
        };
        item.Click += MoveSelectedMediaToFolderMenu_Click;
        parent.Items.Add(item);
    }

    private void MoveSelectedMediaToFolderMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FolderMoveTarget target)
            MoveSelectedMediaToFolder(target.FolderId, target.DisplayName);
    }

    private void OpenMediaLocation_Click(object sender, RoutedEventArgs e)
    {
        var card = GalleryList.SelectedItems.Cast<MediaCardViewModel>().FirstOrDefault()
                   ?? GalleryList.SelectedItem as MediaCardViewModel;
        if (card == null)
        {
            MessageDialog.Show(this, "위치를 열 파일을 먼저 선택하세요.", "파일 위치", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var item = _context.Database.GetMediaById(card.Id) ?? card.Item;
        var topic = _topics.FirstOrDefault(t => string.Equals(t.Id, item.TopicId, StringComparison.OrdinalIgnoreCase));
        if (topic == null)
        {
            ReloadTopics();
            topic = _topics.FirstOrDefault(t => string.Equals(t.Id, item.TopicId, StringComparison.OrdinalIgnoreCase));
        }

        if (topic == null)
        {
            MessageDialog.Show(this, "파일이 속한 주제를 찾을 수 없습니다.", "파일 위치", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (TopicFolderSearchBox != null && !string.IsNullOrWhiteSpace(TopicFolderSearchBox.Text))
            TopicFolderSearchBox.Text = string.Empty;

        _ignoreTopicSelection = true;
        ApplyTopicFolderSearch(topic.Id);
        TopicList.SelectedItem = _sidebarTopics.FirstOrDefault(t => string.Equals(t.Id, topic.Id, StringComparison.OrdinalIgnoreCase));
        _ignoreTopicSelection = false;
        SyncTopicCardSelection();

        _currentTopicFolderId = string.IsNullOrWhiteSpace(item.FolderId) ? null : item.FolderId;
        _folderSearchScope = FolderSearchScopeCurrent;
        SyncMainWindowState();
        SyncFolderSearchScopeCombo();
        _pendingSelectMediaIdAfterReload = item.Id;
        _currentPage = 1;

        ShowMediaView();
        ReloadMedia();
        StatusText.Text = string.IsNullOrWhiteSpace(item.FolderId)
            ? $"'{item.OriginalName}' 파일 위치: {topic.Name} / 루트"
            : $"'{item.OriginalName}' 파일 위치로 이동했습니다.";
    }

    private void MoveFolderMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu)
            return;

        menu.Items.Clear();
        var sourceFolder = GetSourceFolderFromMoveMenu(menu.Tag);
        if (sourceFolder == null || SelectedTopic == null)
        {
            menu.Items.Add(new MenuItem { Header = "이동할 폴더를 확인할 수 없습니다", IsEnabled = false });
            return;
        }

        AddMoveFolderMenuItem(menu, "⌂ 루트로 이동", sourceFolder.Id, null, "루트");

        var folders = GetFlattenedTopicFolders(sourceFolder.TopicId);
        if (folders.Count > 0)
            menu.Items.Add(new Separator());

        var descendants = _context.Database.GetTopicFolderDescendantIds(sourceFolder.Id, includeSelf: false)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            if (string.Equals(folder.Folder.Id, sourceFolder.Id, StringComparison.OrdinalIgnoreCase))
                continue;
            if (descendants.Contains(folder.Folder.Id))
                continue;

            AddMoveFolderMenuItem(menu, "📁 " + folder.Path, sourceFolder.Id, folder.Folder.Id, folder.Folder.Name);
        }
    }

    private TopicFolder? GetSourceFolderFromMoveMenu(object? source)
    {
        return source switch
        {
            TopicFolderCardViewModel card => _context.Database.GetTopicFolderById(card.Id),
            FolderTreeItemViewModel { IsRoot: false } node when !string.IsNullOrWhiteSpace(node.FolderId) => _context.Database.GetTopicFolderById(node.FolderId),
            TopicFolder folder => _context.Database.GetTopicFolderById(folder.Id),
            string id when !string.IsNullOrWhiteSpace(id) => _context.Database.GetTopicFolderById(id),
            _ => null
        };
    }

    private void AddMoveFolderMenuItem(MenuItem parent, string header, string sourceFolderId, string? targetParentFolderId, string targetName)
    {
        var canMove = CanMoveTopicFolderToParent(sourceFolderId, targetParentFolderId, out var statusText);
        var item = new MenuItem
        {
            Header = header,
            Tag = new FolderToFolderMoveTarget(sourceFolderId, targetParentFolderId, targetName),
            IsEnabled = canMove,
            ToolTip = string.IsNullOrWhiteSpace(statusText) ? null : statusText
        };
        item.Click += MoveFolderToFolderMenu_Click;
        parent.Items.Add(item);
    }

    private void MoveFolderToFolderMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FolderToFolderMoveTarget target)
            MoveTopicFolderToParent(target.SourceFolderId, target.TargetParentFolderId, target.TargetDisplayName);
    }

    private List<FlattenedTopicFolder> GetFlattenedTopicFolders(string topicId)
    {
        var result = new List<FlattenedTopicFolder>();
        AddFlattenedTopicFolders(topicId, null, string.Empty, result, 0);
        return result;
    }

    private void AddFlattenedTopicFolders(string topicId, string? parentFolderId, string parentPath, List<FlattenedTopicFolder> result, int depth)
    {
        if (depth > 40)
            return;

        foreach (var folder in _context.Database.GetTopicFolders(topicId, parentFolderId))
        {
            var path = string.IsNullOrWhiteSpace(parentPath) ? folder.Name : parentPath + " / " + folder.Name;
            result.Add(new FlattenedTopicFolder(folder, path));
            AddFlattenedTopicFolders(topicId, folder.Id, path, result, depth + 1);
        }
    }

    private MediaFilterQuery CreateMediaFilterQuery(bool includeTagFilter = true)
    {
        return new MediaFilterQuery(
            SelectedTopic,
            _currentTopicFolderId,
            _folderSearchScope,
            _kindFilter,
            _favoritesOnly,
            _selectedMediaTagFilterId,
            SearchBox?.Text,
            _topics.ToList(),
            includeTagFilter);
    }

    private List<MediaItem> BuildFilteredItems(bool includeTagFilter = true)
    {
        return _mediaFilterController.BuildFilteredItems(CreateMediaFilterQuery(includeTagFilter));
    }

    private Dictionary<string, int> BuildTagFilterCounts()
    {
        return _mediaFilterController.BuildTagFilterCounts(CreateMediaFilterQuery(includeTagFilter: false));
    }

    private bool ShouldShowMediaLocationText(MediaItem item)
    {
        return _mediaFilterController.ShouldShowMediaLocationText(item, SelectedTopic, _folderSearchScope);
    }

    private string BuildMediaLocationText(MediaItem item, Dictionary<string, string> locationCache)
    {
        if (!ShouldShowMediaLocationText(item))
            return string.Empty;

        var topicId = string.IsNullOrWhiteSpace(item.TopicId) ? string.Empty : item.TopicId.Trim();
        var folderId = string.IsNullOrWhiteSpace(item.FolderId) ? string.Empty : item.FolderId.Trim();
        var cacheKey = topicId + "|" + folderId;
        if (locationCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var topicName = _topics.FirstOrDefault(topic => string.Equals(topic.Id, topicId, StringComparison.OrdinalIgnoreCase))?.Name;
        if (string.IsNullOrWhiteSpace(topicName))
            topicName = SelectedTopic?.Name ?? "주제";

        var pathParts = GetTopicFolderPathNamesSafe(topicId, folderId);
        var path = pathParts.Count == 0
            ? $"위치: {topicName} / 루트"
            : $"위치: {topicName} / {string.Join(" / ", pathParts)}";

        locationCache[cacheKey] = path;
        return path;
    }

    private List<string> GetTopicFolderPathNamesSafe(string topicId, string? folderId)
    {
        var names = new Stack<string>();
        var currentId = string.IsNullOrWhiteSpace(folderId) ? null : folderId.Trim();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var depth = 0;

        while (!string.IsNullOrWhiteSpace(currentId) && depth++ < 80)
        {
            if (!visited.Add(currentId))
            {
                AppLogger.Warn($"Folder path cycle detected topicId={topicId}; folderId={currentId}");
                break;
            }

            var folder = _context.Database.GetTopicFolderById(currentId);
            if (folder == null)
            {
                AppLogger.Warn($"Folder path missing folder topicId={topicId}; folderId={currentId}");
                names.Push("알 수 없는 폴더");
                break;
            }

            if (!string.IsNullOrWhiteSpace(topicId) && !string.Equals(folder.TopicId, topicId, StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Warn($"Folder path topic mismatch mediaTopicId={topicId}; folderTopicId={folder.TopicId}; folderId={currentId}");
                names.Push(folder.Name);
                break;
            }

            names.Push(string.IsNullOrWhiteSpace(folder.Name) ? "이름 없는 폴더" : folder.Name.Trim());
            currentId = folder.ParentFolderId;
        }

        if (depth >= 80)
            AppLogger.Warn($"Folder path depth limit reached topicId={topicId}; folderId={folderId}");

        return names.ToList();
    }

    private async void ReloadMedia()
    {
        if (_isReloadingMedia)
        {
            _reloadMediaRequestedAfterCurrent = true;
            SyncMainWindowState();
            return;
        }

        _isReloadingMedia = true;
        try
        {
            var guard = 0;
            do
            {
                _reloadMediaRequestedAfterCurrent = false;
                try
                {
                    await ReloadMediaAsync();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ReloadMedia failed selectedTopicId={SelectedTopicId ?? "(none)"}; currentFolderId={_currentTopicFolderId ?? "(root)"}; scope={_folderSearchScope}; kind={_kindFilter?.ToString() ?? "all"}; favoritesOnly={_favoritesOnly}", ex);
                    EndContentBusyProgress();
                    StatusText.Text = "미디어 목록을 불러오지 못했습니다. 오류 로그를 저장했습니다.";
                    ShowWarningDialogSafely("미디어 목록", "미디어 목록을 불러오는 중 오류가 발생했습니다.\n\n" + ex.Message + "\n\n로그 위치: " + AppLogger.LogDirectory);
                    return;
                }

                if (_reloadMediaRequestedAfterCurrent)
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            while (_reloadMediaRequestedAfterCurrent && ++guard < 4);

            if (_reloadMediaRequestedAfterCurrent)
            {
                _reloadMediaRequestedAfterCurrent = false;
                AppLogger.Warn("ReloadMedia coalesced too many consecutive requests; latest request was dropped to prevent UI refresh loop.");
            }
        }
        finally
        {
            _isReloadingMedia = false;
        }
    }

    private void ShowWarningDialogSafely(string title, string message)
    {
        void ShowNow()
        {
            try
            {
                if (!IsLoaded)
                    return;

                MessageDialog.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception dialogEx)
            {
                AppLogger.Warn($"Warning dialog could not be shown. title={title}; message={dialogEx.Message}");
            }
        }

        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ShowNow));
        }
        catch (Exception dispatchEx)
        {
            AppLogger.Warn($"Warning dialog scheduling failed. title={title}; message={dispatchEx.Message}");
        }
    }

    private async Task ReloadMediaAsync()
    {
        var version = ++_mediaReloadVersion;
        RefreshCurrentFolderUi();
        _filteredItems = BuildFilteredItems();
        await ApplyPaginationAsync(version);
    }

    private async Task ApplyPaginationAsync(int version, bool scrollToTop = false)
    {
        _mediaCards.Clear();
        _itemsPerPage = NormalizeItemsPerPage(_settings.ItemsPerPage);
        var totalItems = _filteredItems.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)_itemsPerPage));
        _currentPage = Math.Clamp(_currentPage, 1, totalPages);

        if (!string.IsNullOrWhiteSpace(_pendingSelectMediaIdAfterReload))
        {
            var pendingIndex = _filteredItems.FindIndex(item => string.Equals(item.Id, _pendingSelectMediaIdAfterReload, StringComparison.OrdinalIgnoreCase));
            if (pendingIndex >= 0)
                _currentPage = Math.Clamp((pendingIndex / _itemsPerPage) + 1, 1, totalPages);
            else
                _pendingSelectMediaIdAfterReload = null;
        }

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
            var locationCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MediaCardViewModel? pendingSelectionCard = null;
            foreach (var item in pageItems)
            {
                if (version != _mediaReloadVersion)
                    return;

                loaded++;
                if (showBusy)
                    UpdateContentBusyProgress(loaded, pageItems.Count, Path.GetFileName(item.OriginalName));

                var thumb = _context.Media.LoadThumbnail(item);
                var locationText = BuildMediaLocationText(item, locationCache);
                var card = new MediaCardViewModel(item, thumb, locationText);
                _mediaCards.Add(card);

                if (!string.IsNullOrWhiteSpace(_pendingSelectMediaIdAfterReload)
                    && string.Equals(item.Id, _pendingSelectMediaIdAfterReload, StringComparison.OrdinalIgnoreCase))
                {
                    pendingSelectionCard = card;
                }

                if (showBusy && (loaded % 3 == 0 || loaded == pageItems.Count))
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }

            if (version != _mediaReloadVersion)
                return;

            if (pendingSelectionCard != null)
            {
                GalleryList.SelectedItems.Clear();
                GalleryList.SelectedItem = pendingSelectionCard;
                GalleryList.ScrollIntoView(pendingSelectionCard);
                _pendingSelectMediaIdAfterReload = null;
            }

            HeaderText.Text = BuildHeaderText();
            HeaderBadgeText.Text = $"{totalItems}개 파일" + (_topicFolderCards.Count > 0 ? $" · 폴더 {_topicFolderCards.Count}개" : string.Empty) + (totalItems > _itemsPerPage ? $" · {_currentPage}/{totalPages} 페이지" : string.Empty);
            MediaCountText.Text = $"{totalItems}개 파일";
            UpdateEmptyMediaPanelState(totalItems);
            StatusText.Text = $"전체 {_context.Database.GetMedia(null, null).Count}개 항목  |  {GetFolderSearchScopeLabel()} 기준 {_mediaCards.Count}개 표시";
            UpdatePager(totalItems, totalPages);
            UpdateFilterCounts();
            UpdateTopicDetail();
            UpdateSidebarActiveState();

            if (scrollToTop)
                await ScrollGalleryListToTopAsync();
        }
        finally
        {
            if (showBusy && _contentBusyVersion == version)
                EndContentBusyProgress();
        }
    }

    private void UpdateEmptyMediaPanelState(int totalItems)
    {
        var folderCount = _topicFolderCards.Count;
        var shouldShow = totalItems == 0 && folderCount == 0;
        EmptyMediaPanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        if (!shouldShow)
            return;

        var hasSearch = SearchBox != null && !string.IsNullOrWhiteSpace(SearchBox.Text);
        var hasTagFilter = !string.IsNullOrWhiteSpace(_selectedMediaTagFilterId);
        var hasKindFilter = _kindFilter.HasValue || _favoritesOnly;
        var hasFilter = hasSearch || hasTagFilter || hasKindFilter;

        if (SelectedTopic == null)
        {
            EmptyMediaTitleText.Text = hasFilter ? "조건에 맞는 미디어가 없습니다." : "아직 추가된 미디어가 없습니다.";
            EmptyMediaDescriptionText.Text = hasFilter
                ? "검색어 또는 필터를 조정하면 다른 항목이 표시될 수 있습니다."
                : "상단의 파일 추가 또는 드래그 앤 드롭으로 이미지·영상·문서·압축파일을 가져오세요.";
            return;
        }

        if (hasFilter)
        {
            EmptyMediaTitleText.Text = "현재 조건에 맞는 파일이 없습니다.";
            EmptyMediaDescriptionText.Text = $"{GetFolderSearchScopeLabel()} 기준으로 검색/필터가 적용되어 있습니다. 검색어, 라벨, 파일 종류 필터를 조정해 보세요.";
            return;
        }

        var scope = NormalizeFolderSearchScope(_folderSearchScope);
        if (scope == FolderSearchScopeDescendants)
        {
            EmptyMediaTitleText.Text = "현재 폴더와 하위 폴더가 비어 있습니다.";
            EmptyMediaDescriptionText.Text = "파일을 추가하거나 다른 폴더에서 파일을 이 위치로 이동해 보세요.";
        }
        else if (scope == FolderSearchScopeTopic)
        {
            EmptyMediaTitleText.Text = "이 주제에 아직 파일이 없습니다.";
            EmptyMediaDescriptionText.Text = "사이드바 주제나 현재 화면으로 파일을 드래그해 이 주제에 추가할 수 있습니다.";
        }
        else if (string.IsNullOrWhiteSpace(_currentTopicFolderId))
        {
            EmptyMediaTitleText.Text = "주제 루트가 비어 있습니다.";
            EmptyMediaDescriptionText.Text = "새 폴더를 만들거나 파일을 추가하세요. 하위 폴더가 있다면 폴더를 열어 확인할 수 있습니다.";
        }
        else
        {
            EmptyMediaTitleText.Text = "이 폴더가 비어 있습니다.";
            EmptyMediaDescriptionText.Text = "파일을 추가하거나 사이드바/폴더 카드로 다른 파일을 이 폴더에 이동하세요.";
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
        RefreshMediaTagFilterOptions();
        ReloadMedia();
    }

    private void GoToPage(int page)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(_filteredItems.Count / (double)Math.Max(1, _itemsPerPage)));
        _currentPage = Math.Clamp(page, 1, totalPages);
        _ = ApplyPaginationAsync(++_mediaReloadVersion, scrollToTop: true);
    }

    private async Task ScrollGalleryListToTopAsync()
    {
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                GalleryList.UpdateLayout();

                var scrollViewer = FindDescendant<ScrollViewer>(GalleryList);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToTop();
                    scrollViewer.ScrollToHorizontalOffset(0);
                }

                if (_mediaCards.Count > 0)
                    GalleryList.ScrollIntoView(_mediaCards[0]);

                GalleryList.UpdateLayout();
                scrollViewer?.ScrollToTop();
                scrollViewer?.ScrollToHorizontalOffset(0);
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Failed to scroll media list to top after page change.", ex);
        }
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
        var topic = SelectedTopic == null ? "전체 항목" : BuildCurrentFolderPathText();
        var filters = new List<string>();

        if (SelectedTopic != null && _folderSearchScope != FolderSearchScopeCurrent)
            filters.Add(GetFolderSearchScopeLabel());

        if (_kindFilter.HasValue)
            filters.Add(GetKindText(_kindFilter.Value));
        else if (_favoritesOnly)
            filters.Add("즐겨찾기");

        var tagFilter = GetSelectedMediaTagFilterLabel();
        if (!string.IsNullOrWhiteSpace(tagFilter))
            filters.Add(tagFilter);

        return filters.Count == 0 ? topic : $"{topic} · {string.Join(" · ", filters)}";
    }

    private void RefreshMediaTagFilterOptions(bool preserveSelection = true)
    {
        if (MediaTagFilterCombo == null)
            return;

        var previousSelectedId = preserveSelection ? _selectedMediaTagFilterId : null;
        _updatingMediaTagFilterOptions = true;
        try
        {
            var tagCounts = BuildTagFilterCounts();
            var countBaseTotal = BuildFilteredItems(includeTagFilter: false).Count;

            _mediaTagFilterOptions.Clear();
            _mediaTagFilterOptions.Add(MediaTagFilterOption.CreateAll(countBaseTotal));

            foreach (var tag in _context.Tags.GetTags())
            {
                tagCounts.TryGetValue(tag.Id, out var count);
                _mediaTagFilterOptions.Add(new MediaTagFilterOption(tag.Id, tag.Name, tag.Color, count));
            }

            var selected = !string.IsNullOrWhiteSpace(previousSelectedId)
                ? _mediaTagFilterOptions.FirstOrDefault(option => string.Equals(option.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
                : null;

            selected ??= _mediaTagFilterOptions.FirstOrDefault();
            _selectedMediaTagFilterId = selected?.Id;
            MediaTagFilterCombo.SelectedItem = selected;
            MediaTagFilterCombo.IsEnabled = _mediaTagFilterOptions.Count > 1;
            MediaTagFilterCombo.ToolTip = _mediaTagFilterOptions.Count > 1
                ? "태그/라벨을 선택하면 해당 라벨이 적용된 파일만 표시됩니다."
                : "태그/라벨을 먼저 만들어 주세요.";
        }
        finally
        {
            _updatingMediaTagFilterOptions = false;
        }
    }

    private string? GetSelectedMediaTagFilterLabel()
    {
        if (string.IsNullOrWhiteSpace(_selectedMediaTagFilterId))
            return null;

        return _mediaTagFilterOptions
            .FirstOrDefault(option => string.Equals(option.Id, _selectedMediaTagFilterId, StringComparison.OrdinalIgnoreCase))
            ?.Name;
    }

    private void MediaTagFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingMediaTagFilterOptions)
            return;

        if (MediaTagFilterCombo.SelectedItem is not MediaTagFilterOption option)
            return;

        if (_isOrderEditMode)
            CancelOrderEditMode("태그/라벨 필터 변경으로 순서 편집 모드를 종료했습니다.");

        _selectedMediaTagFilterId = option.Id;
        _currentPage = 1;
        ReloadMedia();
        StatusText.Text = string.IsNullOrWhiteSpace(option.Id)
            ? "태그/라벨 필터를 해제했습니다."
            : $"'{option.Name}' 태그/라벨이 적용된 파일만 표시합니다.";
    }

    private void FolderSearchScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreFolderSearchScopeChange)
            return;

        if (FolderSearchScopeCombo.SelectedItem is not ComboBoxItem item)
            return;

        var newScope = NormalizeFolderSearchScope(item.Tag?.ToString());
        if (string.Equals(_folderSearchScope, newScope, StringComparison.OrdinalIgnoreCase))
            return;

        if (_isOrderEditMode)
            CancelOrderEditMode("검색 범위 변경으로 순서 편집 모드를 종료했습니다.");

        _folderSearchScope = newScope;
        _currentPage = 1;
        SyncMainWindowState();
        RefreshMediaTagFilterOptions();
        ReloadMedia();
        StatusText.Text = $"검색/필터 범위를 '{GetFolderSearchScopeLabel()}' 기준으로 변경했습니다.";
    }

    private void UpdateFilterCounts()
    {
        // 주제 폴더가 선택된 상태에서는 사이드바 필터 개수도 해당 주제 기준으로 표시합니다.
        // 주제가 선택되지 않은 상태에서는 전체 보관함 기준입니다.
        var topic = SelectedTopic;
        var all = topic != null
            ? GetMediaForCurrentFolderScope(topic, null)
            : _context.Database.GetMedia(null, null);
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

        // 미디어 필터는 전체 보관함과 선택된 주제 폴더 모두에서 유지됩니다.
        // 따라서 주제 선택 여부와 관계없이 현재 필터 버튼을 활성 표시합니다.
        SetSidebarButtonActive(AllButton, mediaVisible && _kindFilter == null && !_favoritesOnly);
        SetSidebarButtonActive(ImagesButton, mediaVisible && _kindFilter == MediaKind.Image && !_favoritesOnly);
        SetSidebarButtonActive(VideosButton, mediaVisible && _kindFilter == MediaKind.Video && !_favoritesOnly);
        SetSidebarButtonActive(DocumentsButton, mediaVisible && _kindFilter == MediaKind.Document && !_favoritesOnly);
        SetSidebarButtonActive(ArchivesButton, mediaVisible && _kindFilter == MediaKind.Archive && !_favoritesOnly);
        SetSidebarButtonActive(OtherFilesButton, mediaVisible && _kindFilter == MediaKind.Other && !_favoritesOnly);
        SetSidebarButtonActive(FavoritesButton, mediaVisible && _favoritesOnly);
        SetSidebarButtonActive(TopicDetailVideosButton, mediaVisible && _kindFilter == MediaKind.Video && !_favoritesOnly);
        SetSidebarButtonActive(TopicDetailImagesButton, mediaVisible && _kindFilter == MediaKind.Image && !_favoritesOnly);
        SetSidebarButtonActive(TopicDetailFavoritesButton, mediaVisible && _favoritesOnly);
        SetSidebarButtonActive(ManageTopicsButton, topicManagerVisible);

        SetSidebarButtonActive(CollapsedAllButton, mediaVisible && _kindFilter == null && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedImagesButton, mediaVisible && _kindFilter == MediaKind.Image && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedVideosButton, mediaVisible && _kindFilter == MediaKind.Video && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedDocumentsButton, mediaVisible && _kindFilter == MediaKind.Document && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedArchivesButton, mediaVisible && _kindFilter == MediaKind.Archive && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedOtherFilesButton, mediaVisible && _kindFilter == MediaKind.Other && !_favoritesOnly);
        SetSidebarButtonActive(CollapsedFavoritesButton, mediaVisible && _favoritesOnly);
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
        UpdateTopicDetailFilterCounts(topic);
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

    private void UpdateTopicDetailFilterCounts(Topic? topic = null)
    {
        if (TopicDetailImagesCountText == null || TopicDetailVideosCountText == null || TopicDetailFavoritesCountText == null)
            return;

        topic ??= SelectedTopic;
        var items = _context.Database.GetMedia(topic?.Id, null);
        TopicDetailVideosCountText.Text = items.Count(i => i.Kind == MediaKind.Video).ToString();
        TopicDetailImagesCountText.Text = items.Count(i => i.Kind == MediaKind.Image).ToString();
        TopicDetailFavoritesCountText.Text = items.Count(i => i.Favorite).ToString();
    }

    private void UpdateDropZoneHint(Topic? topic = null)
    {
        if (DropZoneTitleText == null || DropZoneSubtitleText == null || DropZoneFallbackText == null)
            return;

        topic ??= SelectedTopic;
        DropZoneTitleText.Text = "파일 드래그&드롭 추가";
        var folderPath = topic == null ? "미분류" : BuildCurrentFolderPathText();
        DropZoneSubtitleText.Text = $"저장 위치: {folderPath}";
        DropZoneFallbackText.Text = topic == null ? "주제 미선택 상태라 미분류에 저장됩니다" : "파일·폴더를 놓으면 현재 위치에 저장됩니다";
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
        ResetCurrentTopicFolder();
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
        // Topic Explorer must behave like a folder-root page: no topic is pre-opened.
        // 현재 선택된 미디어 필터는 유지합니다. 예: 이미지 필터 상태에서 다른 주제 폴더를 열면
        // 새 주제에도 이미지 필터가 그대로 적용됩니다.
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
        if (_ignoreTopicSelection)
            return;

        try
        {
            AppLogger.Info($"TopicList.SelectionChanged start selectedTopicId={SelectedTopicId ?? "(none)"}; selectedTopicName={SelectedTopic?.Name ?? "(none)"}; orderEdit={_isOrderEditMode}");
            if (_isOrderEditMode)
            {
                SyncTopicCardSelection();
                UpdateTopicDetail();
                AppLogger.Info("TopicList.SelectionChanged finished in order edit mode");
                return;
            }
            ResetCurrentTopicFolder();
            ShowMediaView();
            SyncTopicCardSelection();
            ResetPageAndReload();
            AppLogger.Info($"TopicList.SelectionChanged finished selectedTopicId={SelectedTopicId ?? "(none)"}");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"TopicList.SelectionChanged failed selectedTopicId={SelectedTopicId ?? "(none)"}; currentFolderId={_currentTopicFolderId ?? "(root)"}", ex);
            StatusText.Text = "주제 선택 중 오류가 발생해 로그를 저장했습니다.";
            MessageDialog.Show(this, "주제 선택 중 오류가 발생했습니다.\n\n" + ex.Message + "\n\n로그 위치: " + AppLogger.LogDirectory, "주제 선택", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        if (_ignoreTopicCardSelection)
            return;
        if (sender is not ListBox lb || lb.SelectedItem is not TopicCardViewModel card)
            return;

        try
        {
            AppLogger.Info($"TopicCard.SelectionChanged start topicId={card.Id}; topicName={card.Name}; orderEdit={_isOrderEditMode}");
            if (_isOrderEditMode)
            {
                _ignoreTopicSelection = true;
                TopicList.SelectedItem = _topics.FirstOrDefault(t => t.Id == card.Id);
                _ignoreTopicSelection = false;
                UpdateTopicDetail();
                AppLogger.Info("TopicCard.SelectionChanged finished in order edit mode");
                return;
            }

            _ignoreTopicSelection = true;
            _ignoreTopicCardSelection = true;
            TopicList.SelectedItem = _topics.FirstOrDefault(t => t.Id == card.Id);
            TopicGridList.SelectedItem = card;
            TopicLinearList.SelectedItem = card;
            _ignoreTopicCardSelection = false;
            _ignoreTopicSelection = false;

            // Explorer-like behavior: clicking a topic folder opens that topic's media immediately.
            // 기존 미디어 필터는 유지하고, 주제 내부 가상 폴더는 루트에서 시작합니다.
            ResetCurrentTopicFolder();
            ShowMediaView();
            ResetPageAndReload();
            AppLogger.Info($"TopicCard.SelectionChanged finished topicId={card.Id}");
        }
        catch (Exception ex)
        {
            _ignoreTopicSelection = false;
            _ignoreTopicCardSelection = false;
            AppLogger.Error($"TopicCard.SelectionChanged failed topicId={card.Id}; currentFolderId={_currentTopicFolderId ?? "(root)"}", ex);
            StatusText.Text = "주제 카드 선택 중 오류가 발생해 로그를 저장했습니다.";
            MessageDialog.Show(this, "주제 선택 중 오류가 발생했습니다.\n\n" + ex.Message + "\n\n로그 위치: " + AppLogger.LogDirectory, "주제 선택", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


    private void ToggleOrderEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_isOrderEditMode)
        {
            CancelOrderEditMode(revertChanges: true, statusMessage: "\uC21C\uC11C \uD3B8\uC9D1\uC744 \uCDE8\uC18C\uD588\uC2B5\uB2C8\uB2E4. \uD3B8\uC9D1 \uC2DC\uC791 \uC2DC\uC810\uC73C\uB85C \uB418\uB3CC\uB9AC\uACE0 \uC885\uB8CC\uD588\uC2B5\uB2C8\uB2E4.");
            return;
        }

        EnterOrderEditMode("순서 편집 모드: 핸들을 드래그하여 위치를 변경합니다. ESC 또는 ✕ 종료로 빠져나올 수 있습니다.");
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
        _folderOrderSnapshot = null;
        ClearReorderDropCue();
        StopReorderAutoScroll();
        UpdateOrderEditVisual();
        StatusText.Text = "순서 편집 완료: 현재 순서가 저장되었습니다.";
    }
    private void CancelOrderEdit_Click(object sender, RoutedEventArgs e)
    {
        CancelOrderEditMode(revertChanges: true, statusMessage: "\uC21C\uC11C \uD3B8\uC9D1\uC744 \uCDE8\uC18C\uD588\uC2B5\uB2C8\uB2E4. \uD3B8\uC9D1 \uC2DC\uC791 \uC2DC\uC810\uC73C\uB85C \uB418\uB3CC\uB9AC\uACE0 \uC885\uB8CC\uD588\uC2B5\uB2C8\uB2E4.");
    }
    private void MainWindow_OrderEditPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isOrderEditMode || e.Key != Key.Escape)
            return;

        CancelOrderEditMode(revertChanges: true, statusMessage: "\uC21C\uC11C \uD3B8\uC9D1\uC744 \uCDE8\uC18C\uD588\uC2B5\uB2C8\uB2E4. \uD3B8\uC9D1 \uC2DC\uC791 \uC2DC\uC810\uC73C\uB85C \uB418\uB3CC\uB9AC\uACE0 \uC885\uB8CC\uD588\uC2B5\uB2C8\uB2E4.");
        e.Handled = true;
    }
    private void CancelOrderEditMode(string? statusMessage = null)
    {
        CancelOrderEditMode(revertChanges: false, statusMessage);
    }

    private void CancelOrderEditMode(bool revertChanges, string? statusMessage = null)
    {
        if (!_isOrderEditMode)
            return;

        var restored = revertChanges && RestoreOrderSnapshots();

        _isOrderEditMode = false;
        _topicOrderSnapshot = null;
        _mediaOrderSnapshot = null;
        _folderOrderSnapshot = null;
        _reorderDragSource = null;
        _externalDragSource = null;
        EndReorderDragVisual();
        ClearReorderDropCue();
        StopReorderAutoScroll();

        if (restored)
            ReloadAll();

        UpdateOrderEditVisual();
        Dispatcher.BeginInvoke(new Action(UpdateOrderEditVisual), System.Windows.Threading.DispatcherPriority.Background);

        StatusText.Text = statusMessage
            ?? (restored ? "\uC21C\uC11C \uD3B8\uC9D1\uC744 \uCDE8\uC18C\uD558\uACE0 \uD3B8\uC9D1 \uC2DC\uC791 \uC2DC\uC810\uC73C\uB85C \uB418\uB3CC\uB838\uC2B5\uB2C8\uB2E4." : "\uC21C\uC11C \uD3B8\uC9D1 \uBAA8\uB4DC\uB97C \uC885\uB8CC\uD588\uC2B5\uB2C8\uB2E4.");
    }

    private bool RestoreOrderSnapshots()
    {
        var result = _orderEditController.RestoreSnapshot(
            _topicOrderSnapshot,
            _mediaOrderSnapshot,
            _folderOrderSnapshot,
            _topics.Select(t => t.Id),
            _filteredItems.Select(i => i.Id),
            _topicFolderCards.Select(i => i.Id));

        return result.AnyChanged;
    }
    private void UndoOrderEdit_Click(object sender, RoutedEventArgs e)
    {
        if (!_isOrderEditMode)
            return;

        if (!RestoreOrderSnapshots())
        {
            CancelOrderEditMode(revertChanges: false, statusMessage: "\uB418\uB3CC\uB9B4 \uC815\uB82C \uBCC0\uACBD\uC774 \uC5C6\uC5B4 \uD3B8\uC9D1 \uBAA8\uB4DC\uB9CC \uC885\uB8CC\uD588\uC2B5\uB2C8\uB2E4.");
            return;
        }

        _isOrderEditMode = false;
        _topicOrderSnapshot = null;
        _mediaOrderSnapshot = null;
        _folderOrderSnapshot = null;
        _reorderDragSource = null;
        _externalDragSource = null;
        EndReorderDragVisual();
        ClearReorderDropCue();
        StopReorderAutoScroll();
        ReloadAll();
        UpdateOrderEditVisual();
        Dispatcher.BeginInvoke(new Action(UpdateOrderEditVisual), System.Windows.Threading.DispatcherPriority.Background);
        StatusText.Text = "\uC815\uB82C \uBCC0\uACBD\uC744 \uD3B8\uC9D1 \uC2DC\uC791 \uC2DC\uC810\uC73C\uB85C \uB418\uB3CC\uB9AC\uACE0 \uD3B8\uC9D1 \uBAA8\uB4DC\uB97C \uC885\uB8CC\uD588\uC2B5\uB2C8\uB2E4.";
    }

    private void ResetOrderEdit_Click(object sender, RoutedEventArgs e)
    {
        if (!_isOrderEditMode)
            EnterOrderEditMode();

        if (TopicManagementView.Visibility == Visibility.Visible)
        {
            var topicIds = _orderEditController.BuildDefaultTopicOrder(_topics, IsFixedUncategorizedTopic);
            if (topicIds.Count > 0)
                _context.Database.UpdateTopicSortOrders(topicIds);
            ReloadTopics();
            StatusText.Text = "주제 순서를 생성일 기준 기본 순서로 복원했습니다.";
            return;
        }

        if (SelectedTopic != null && _topicFolderCards.Count > 0)
        {
            var folderIds = _orderEditController.BuildDefaultFolderOrder(_context.Database.GetTopicFolders(SelectedTopic.Id, _currentTopicFolderId));
            if (folderIds.Count > 0)
                _context.Database.UpdateTopicFolderSortOrders(folderIds);
        }

        var mediaIds = _orderEditController.BuildDefaultMediaOrder(_filteredItems);
        if (mediaIds.Count > 0)
            _context.Database.UpdateMediaSortOrders(mediaIds);
        ReloadMedia();
        StatusText.Text = _topicFolderCards.Count > 0
            ? "현재 폴더의 하위 폴더 순서와 미디어/파일 순서를 기본 정렬로 복원했습니다."
            : "현재 미디어/파일 순서를 최신순 기본 정렬로 복원했습니다.";
    }

    private void CaptureOrderSnapshots(bool force = false)
    {
        if (!force && _topicOrderSnapshot != null && _mediaOrderSnapshot != null && _folderOrderSnapshot != null)
            return;

        var snapshot = _orderEditController.CaptureSnapshot(
            _topics.Select(t => t.Id),
            _filteredItems.Select(i => i.Id),
            _topicFolderCards.Select(i => i.Id));

        if (force || _topicOrderSnapshot == null)
            _topicOrderSnapshot = snapshot.TopicIds;

        if (force || _mediaOrderSnapshot == null)
            _mediaOrderSnapshot = snapshot.MediaIds;

        if (force || _folderOrderSnapshot == null)
            _folderOrderSnapshot = snapshot.FolderIds;
    }

    private void EnsureOrderEditCancelButtons()
    {
        AddOrderEditCancelButton(MediaOrderEditBanner);
        AddOrderEditCancelButton(TopicOrderEditBanner);
    }

    private void AddOrderEditCancelButton(DependencyObject? root)
    {
        if (root == null || FindTaggedOrderEditCancelButton(root) != null)
            return;

        var targetPanel = FindOrderEditButtonPanel(root);
        if (targetPanel == null)
            return;

        var cancelButton = new Button
        {
            Content = "\u2715 \uCDE8\uC18C",
            Width = 82,
            MinWidth = 72,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "순서 편집 모드 종료. 이미 적용된 순서는 유지됩니다.",
            Tag = "OrderEditCancelButton"
        };
        cancelButton.SetResourceReference(StyleProperty, "GhostButton");
        cancelButton.Click += CancelOrderEdit_Click;
        targetPanel.Children.Add(cancelButton);
    }

    private static Button? FindTaggedOrderEditCancelButton(DependencyObject root)
    {
        if (root is Button button && Equals(button.Tag, "OrderEditCancelButton"))
            return button;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var result = FindTaggedOrderEditCancelButton(VisualTreeHelper.GetChild(root, i));
            if (result != null)
                return result;
        }

        return null;
    }

    private static Panel? FindOrderEditButtonPanel(DependencyObject root)
    {
        if (root is Panel panel && panel.Children.OfType<Button>().Any(button => (button.Content?.ToString() ?? string.Empty).Contains("완료")))
            return panel;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var result = FindOrderEditButtonPanel(VisualTreeHelper.GetChild(root, i));
            if (result != null)
                return result;
        }

        return null;
    }

    private void UpdateOrderEditVisual()
    {
        if (MediaOrderEditButton == null || TopicOrderEditButton == null)
            return;

        EnsureOrderEditCancelButtons();
        SetSidebarButtonActive(MediaOrderEditButton, _isOrderEditMode);
        SetSidebarButtonActive(TopicOrderEditButton, _isOrderEditMode);
        MediaOrderEditButton.Content = _isOrderEditMode ? "\u2715 \uD3B8\uC9D1 \uCDE8\uC18C" : "✎ 순서 편집";
        TopicOrderEditButton.Content = _isOrderEditMode ? "\u2715 \uD3B8\uC9D1 \uCDE8\uC18C" : "✎ 순서 편집";

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
        if (dataContext is not Topic && dataContext is not TopicCardViewModel && dataContext is not MediaCardViewModel && dataContext is not TopicFolderCardViewModel)
            return;

        _reorderDragStartPoint = e.GetPosition(null);
        _externalDragStartPoint = e.GetPosition(null);
        _reorderDragSource = null;
        _externalDragSource = null;

        if (!_isOrderEditMode)
        {
            if (dataContext is MediaCardViewModel mediaCard)
            {
                PrepareMediaHandleDrag(mediaCard);
                e.Handled = true;
                return;
            }

            // 일반 모드에서 주제/폴더 핸들을 건드렸다고 바로 순서 편집 모드로 들어가면
            // 클릭/드래그 오차만으로 편집 모드가 켜지는 문제가 생깁니다.
            // 순서 변경은 반드시 상단의 '순서 편집' 버튼을 누른 뒤에만 허용합니다.
            StatusText.Text = "순서 변경은 상단의 '순서 편집'을 누른 뒤 핸들을 드래그하세요.";
            e.Handled = true;
            return;
        }

        if (dataContext is Topic fixedTopic && IsFixedUncategorizedTopic(fixedTopic))
        {
            _reorderDragSource = null;
            StatusText.Text = "미분류는 항상 상단에 고정되어 순서를 변경할 수 없습니다.";
            e.Handled = true;
            return;
        }

        if (dataContext is TopicCardViewModel fixedTopicCard && IsFixedUncategorizedTopic(fixedTopicCard))
        {
            _reorderDragSource = null;
            StatusText.Text = "미분류는 항상 상단에 고정되어 순서를 변경할 수 없습니다.";
            e.Handled = true;
            return;
        }

        _reorderDragSource = dataContext;
        StatusText.Text = "순서 편집 중 · 핸들을 드래그해서 위치를 바꾸세요.";
        e.Handled = true;
    }

    private void ReorderHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isOrderEditMode)
        {
            StartExternalMediaDrag(e, sender as DependencyObject);
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed || _reorderDragSource == null)
            return;

        var current = e.GetPosition(null);
        if (!HasMovedEnoughForDrag(_reorderDragStartPoint, current))
            return;

        if (sender is not DependencyObject dragSource)
            return;

        var data = _reorderDragSource;
        _reorderDragSource = null;
        ExecuteReorderDrag(dragSource, data, FindAncestor<ListBoxItem>(dragSource));
        e.Handled = true;
    }

    private void PrepareMediaHandleDrag(MediaCardViewModel mediaCard)
    {
        _externalDragSource = mediaCard;
        _reorderDragSource = null;

        // 핸들에서 드래그를 시작할 때, 해당 파일이 선택되어 있지 않으면 단일 선택으로 맞춥니다.
        // 이미 여러 파일이 선택된 상태에서 그 중 하나의 핸들을 잡으면 다중 이동을 유지합니다.
        if (!GalleryList.SelectedItems.Cast<MediaCardViewModel>().Any(card => string.Equals(card.Id, mediaCard.Id, StringComparison.OrdinalIgnoreCase)))
        {
            GalleryList.SelectedItems.Clear();
            GalleryList.SelectedItem = mediaCard;
        }

        StatusText.Text = "파일 드래그 준비 · 사이드바 주제/폴더로 이동하거나 탐색기로 복사할 수 있습니다.";
    }

    private static bool HasMovedEnoughForDrag(Point start, Point current) => MediaDragDropController.HasMovedEnoughForDrag(start, current);

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
            // 순서 편집 모드에서도 카드 전체가 아니라 명시된 ⠿ 핸들에서만 순서 드래그를 시작합니다.
            // 이렇게 해야 폴더 열기/파일 열기/선택/더블클릭과 순서 이동이 충돌하지 않습니다.
            if (!IsDragHandleEventSource(source))
            {
                _reorderDragSource = null;
                return;
            }

            if (dataContext is Topic fixedTopic && IsFixedUncategorizedTopic(fixedTopic))
            {
                _reorderDragSource = null;
                StatusText.Text = "미분류는 항상 상단에 고정되어 순서를 변경할 수 없습니다.";
                return;
            }

            if (dataContext is TopicCardViewModel fixedTopicCard && IsFixedUncategorizedTopic(fixedTopicCard))
            {
                _reorderDragSource = null;
                StatusText.Text = "미분류는 항상 상단에 고정되어 순서를 변경할 수 없습니다.";
                return;
            }

            _reorderDragSource = dataContext;
            return;
        }

        // 일반 보기에서는 카드 전체를 드래그 시작점으로 쓰지 않습니다.
        // 카드 전체에서 드래그를 감지하면 더블클릭 열기, 파일 열기, 마우스 오버 선택 시각효과와 충돌합니다.
        // 파일 이동/내보내기 드래그는 카드 좌상단/리스트 좌측의 ⠿ 핸들에서만 시작합니다.
        if (ReferenceEquals(sender, GalleryList))
            _externalDragSource = null;
    }

    private static bool IsDragHandleEventSource(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is FrameworkElement element && Equals(element.Tag, "DragHandle"))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
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

        // 일반 모드에서는 GalleryList 전체 MouseMove로 드래그를 시작하지 않습니다.
        // 아주 작은 마우스 이동만으로도 더블클릭/열기 동작이 취소되고, 카드 hover가 깜빡이는 문제가 생깁니다.
        // 외부/사이드바 이동 드래그는 ReorderHandle_PreviewMouseMove에서만 시작합니다.
    }

    private void ReorderFolderList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isOrderEditMode)
            StartReorderDrag(sender, e, typeof(TopicFolderCardViewModel));
    }

    private void StartExternalMediaDrag(MouseEventArgs e, DependencyObject? dragSource)
    {
        if (_isOrderEditMode || _isImporting || e.LeftButton != MouseButtonState.Pressed || _externalDragSource == null)
            return;

        var current = e.GetPosition(null);
        if (!HasMovedEnoughForDrag(_externalDragStartPoint, current))
            return;

        var source = _externalDragSource;
        _externalDragSource = null;
        try
        {
            Mouse.Capture(null);
            BeginExternalMediaDrag(source, dragSource ?? GalleryList);
        }
        finally
        {
            e.Handled = true;
        }
    }

    private void BeginExternalMediaDrag(MediaCardViewModel source, DependencyObject dragSource)
    {
        try
        {
            Cursor = Cursors.Wait;
            StatusText.Text = "외부 드래그 내보내기 준비 중: " + source.Title;

            var selectedCards = GalleryList.SelectedItems.Cast<MediaCardViewModel>().ToList();
            if (!selectedCards.Any(card => card.Id == source.Id))
                selectedCards = [source];

            if (!ConfirmSecuritySensitiveExport(selectedCards.Select(card => card.Item).ToList(), "외부 드래그 내보내기"))
            {
                StatusText.Text = "외부 드래그 내보내기를 취소했습니다.";
                return;
            }

            var data = _mediaDragDropController.CreateMediaDragData(selectedCards.Select(card => card.Id));

            var fileDropList = new StringCollection();
            foreach (var card in selectedCards)
            {
                var tempPath = _context.Media.ExportMediaToExternalDragTemp(card.Item);
                fileDropList.Add(tempPath);
            }

            data.SetFileDropList(fileDropList);
            data.SetData(DataFormats.FileDrop, fileDropList.Cast<string>().ToArray());

            Cursor = null;
            StatusText.Text = selectedCards.Count == 1
                ? "탐색기 폴더에 놓으면 복호화된 파일이 복사됩니다."
                : $"선택한 {selectedCards.Count}개 파일을 탐색기 폴더에 놓으면 복사됩니다.";

            var dropResult = DragDrop.DoDragDrop(dragSource, data, DragDropEffects.Copy | DragDropEffects.Move);
            if (dropResult == DragDropEffects.Move)
            {
                StatusText.Text = "선택 파일을 폴더로 이동했습니다.";
            }
            else
            {
                StatusText.Text = "외부 드래그 내보내기 완료";
                _context.ActivityLogs.Add("export", "외부 드래그 내보내기", $"{selectedCards.Count}개 파일");
            }
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
            TopicFolderCardViewModel folder => ("폴더", folder.Name, folder.SummaryText, null),
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

    private void ReorderTopicCardList_DragOver(object sender, DragEventArgs e)
    {
        if (TryHandleExternalTopicExplorerDragOver(sender, e))
            return;

        HandleReorderDragOver<TopicCardViewModel>(sender, e, GetListSurfaceKind(sender));
    }

    private void ReorderFolderList_DragOver(object sender, DragEventArgs e)
    {
        if (IsExternalFileDropData(e.Data))
        {
            SetExternalImportDropEffect(e, SelectedTopic, _currentTopicFolderId, GetCurrentExternalImportTargetName());
            return;
        }

        HandleReorderDragOver<TopicFolderCardViewModel>(sender, e, ReorderSurfaceVisualKind.Grid);
    }

    private void ReorderMediaList_DragOver(object sender, DragEventArgs e)
    {
        if (IsExternalFileDropData(e.Data))
        {
            SetExternalImportDropEffect(e, SelectedTopic, _currentTopicFolderId, GetCurrentExternalImportTargetName());
            return;
        }

        HandleReorderDragOver<MediaCardViewModel>(sender, e, GetListSurfaceKind(sender));
    }

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

        if (ReferenceEquals(listBox, GalleryList) || ReferenceEquals(listBox, FolderGridList))
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

        if (ReferenceEquals(sender, FolderGridList))
            return ReorderSurfaceVisualKind.Grid;

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

    private async void ReorderTopicCardList_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (IsExternalFileDropData(e.Data) && TryGetTopicExplorerDropTarget(sender, e, out var targetTopic))
            {
                e.Handled = true;
                await ImportExternalDroppedFilesAsync(e.Data, targetTopic, null, $"{targetTopic.Name} / 루트");
                return;
            }

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

    private async void ReorderFolderList_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (IsExternalFileDropData(e.Data))
            {
                e.Handled = true;
                await ImportExternalDroppedFilesAsync(e.Data, SelectedTopic, _currentTopicFolderId, GetCurrentExternalImportTargetName());
                return;
            }

            if (!_isOrderEditMode || !e.Data.GetDataPresent(typeof(TopicFolderCardViewModel)))
                return;

            var source = (TopicFolderCardViewModel)e.Data.GetData(typeof(TopicFolderCardViewModel))!;
            if (TryGetCurrentDropTarget<TopicFolderCardViewModel>(out var target, out var placement))
                MoveFolderOrder(source.Id, target.Id, placement);
        }
        finally
        {
            ClearReorderDropCue();
            e.Handled = true;
        }
    }

    private async void ReorderMediaList_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (IsExternalFileDropData(e.Data))
            {
                e.Handled = true;
                await ImportExternalDroppedFilesAsync(e.Data, SelectedTopic, _currentTopicFolderId, GetCurrentExternalImportTargetName());
                return;
            }

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
        if (sourceId == targetId)
            return;

        var sourceTopic = _topics.FirstOrDefault(topic => string.Equals(topic.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        var targetTopic = _topics.FirstOrDefault(topic => string.Equals(topic.Id, targetId, StringComparison.OrdinalIgnoreCase));

        if (IsFixedUncategorizedTopic(sourceTopic) || IsFixedUncategorizedTopic(targetTopic))
        {
            StatusText.Text = "미분류는 항상 상단에 고정되어 순서를 변경할 수 없습니다.";
            return;
        }

        var fixedIds = _topics
            .Where(IsFixedUncategorizedTopic)
            .OrderBy(topic => topic.CreatedUtc)
            .ThenBy(topic => topic.Id, StringComparer.OrdinalIgnoreCase)
            .Select(topic => topic.Id)
            .ToList();

        var ids = _topics
            .Where(topic => !IsFixedUncategorizedTopic(topic))
            .Select(topic => topic.Id)
            .ToList();

        MoveId(ids, sourceId, targetId, placement);
        _context.Database.UpdateTopicSortOrders(fixedIds.Concat(ids).ToList());
        _sidebarTopicSortMode = SidebarTopicSortCustom;
        _settings.SidebarTopicSortMode = _sidebarTopicSortMode;
        AppSettingsService.Save(_settings);
        ReloadTopics();
        StatusText.Text = "주제 순서가 저장되었습니다. 미분류는 항상 상단에 고정됩니다.";
    }

    private void MoveFolderOrder(string sourceId, string targetId, ReorderDropPlacement placement)
    {
        if (sourceId == targetId) return;
        var ids = _topicFolderCards.Select(i => i.Id).ToList();
        MoveId(ids, sourceId, targetId, placement);
        _context.Database.UpdateTopicFolderSortOrders(ids);
        RefreshCurrentFolderUi();
        StatusText.Text = "현재 위치의 하위 폴더 순서가 저장되었습니다.";
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
        if (_isOrderEditMode)
            CancelOrderEditMode("순서 편집 모드를 종료했습니다.");
        else
        {
            ClearReorderDropCue();
            UpdateOrderEditVisual();
        }
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
        RefreshMediaTagFilterOptions();
        ShowMediaView();
        ResetPageAndReload();
        StatusText.Text = "갤러리 화면으로 돌아왔습니다.";
    }

    private void All_Click(object sender, RoutedEventArgs e)
    {
        _kindFilter = null;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void Images_Click(object sender, RoutedEventArgs e)
    {
        _kindFilter = MediaKind.Image;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void Videos_Click(object sender, RoutedEventArgs e)
    {
        _kindFilter = MediaKind.Video;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void Documents_Click(object sender, RoutedEventArgs e)
    {
        _kindFilter = MediaKind.Document;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void Archives_Click(object sender, RoutedEventArgs e)
    {
        _kindFilter = MediaKind.Archive;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void OtherFiles_Click(object sender, RoutedEventArgs e)
    {
        _kindFilter = MediaKind.Other;
        _favoritesOnly = false;
        ShowMediaView();
        ResetPageAndReload();
    }

    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
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

    private Task ImportFilesAsync(IEnumerable<string> paths)
        => ImportFilesAsync(paths, null, null, null, openTargetAfterImport: false);

    private async Task ImportFilesAsync(IEnumerable<string> paths, Topic? explicitTopic, string? explicitFolderId, string? explicitTargetName, bool openTargetAfterImport)
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
            var topic = explicitTopic ?? ResolveImportTargetTopic();
            var targetFolderId = explicitTopic != null
                ? NormalizeImportTargetFolderId(topic, explicitFolderId)
                : SelectedTopic != null && string.Equals(topic.Id, SelectedTopic.Id, StringComparison.OrdinalIgnoreCase)
                    ? NormalizeImportTargetFolderId(topic, _currentTopicFolderId)
                    : null;
            var targetName = !string.IsNullOrWhiteSpace(explicitTargetName)
                ? explicitTargetName
                : targetFolderId == null ? $"{topic.Name} / 루트" : BuildFolderPathText(topic, targetFolderId);

            var result = await _mediaImportController.ImportFilesAsync(
                new MediaImportRequest
                {
                    Paths = pathList,
                    Topic = topic,
                    FolderId = targetFolderId,
                    TargetName = targetName
                },
                new MediaImportCallbacks
                {
                    SetStatus = message => StatusText.Text = message,
                    ConfigureProgress = ConfigureImportProgress,
                    UpdateProgress = UpdateImportProgress,
                    ShowScanWarning = message => MessageDialog.Show(this, message, "가져오기", MessageBoxButton.OK, MessageBoxImage.Warning),
                    ConfirmSecuritySensitiveImport = ConfirmSecuritySensitiveImport,
                    ShowFileFailure = (file, ex) =>
                    {
                        Cursor = null;
                        MessageDialog.Show(this, $"가져오기 실패: {Path.GetFileName(file)}\n\n{ex.Message}", "가져오기", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Cursor = Cursors.Wait;
                    },
                    ConfirmDuplicate = (file, duplicates) =>
                    {
                        Cursor = null;
                        var choice = ConfirmDuplicateImport(file, duplicates);
                        Cursor = Cursors.Wait;
                        return choice;
                    },
                    RunStaAsync = RunStaAsync
                });

            if (openTargetAfterImport)
            {
                ReloadTopics();
                var refreshedTopic = _topics.FirstOrDefault(t => string.Equals(t.Id, result.Topic.Id, StringComparison.OrdinalIgnoreCase)) ?? result.Topic;
                OpenTopicFromSidebarTree(refreshedTopic, result.FolderId);
                RefreshMediaTagFilterOptions();
                ReloadMedia();
            }
            else
            {
                ShowMediaView();
                ReloadAll();
            }

            StatusText.Text = result.ToStatusMessage();
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

    private bool ConfirmSecuritySensitiveImport(IReadOnlyList<string> files)
    {
        if (files.Count == 0)
            return true;

        Cursor = null;
        try
        {
            var sample = string.Join("\n", files.Take(8).Select(path => "• " + Path.GetFileName(path)));
            var more = files.Count > 8 ? $"\n외 {files.Count - 8}개" : string.Empty;
            var message =
                "Windows에서 실행성 동작을 할 수 있는 파일 형식이 포함되어 있습니다.\n\n" +
                sample + more +
                "\n\n이 파일들은 Vault에 보관할 수는 있지만, 앱에서 바로 열기는 제한됩니다. 내보내기 후 직접 확인할 때도 주의해야 합니다.\n\n" +
                "이 파일들도 함께 가져올까요?";

            var result = MessageDialog.ShowOptions(
                this,
                message,
                "실행 주의 파일 가져오기",
                MessageBoxImage.Warning,
                new MessageDialogOption("주의 파일 건너뛰기", "skip", true, isCancel: true),
                new MessageDialogOption("함께 가져오기", "import", false, isDefault: true));

            return string.Equals(result, "import", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cursor = Cursors.Wait;
        }
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
        SyncMainWindowState();
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
        SyncMainWindowState();
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
        var isValid = IsExternalFileDropData(e.Data);
        SetDropZoneDragState(isValid);
        e.Effects = isValid ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void StorageDropZone_DragOver(object sender, DragEventArgs e)
    {
        var isValid = IsExternalFileDropData(e.Data);
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
        await ImportExternalDroppedFilesAsync(e.Data, SelectedTopic, _currentTopicFolderId, GetCurrentExternalImportTargetName(), openTargetAfterImport: SelectedTopic != null);
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

        e.Effects = IsExternalFileDropData(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (IsExternalFileDropData(e.Data))
            await ImportExternalDroppedFilesAsync(e.Data, SelectedTopic, _currentTopicFolderId, GetCurrentExternalImportTargetName(), openTargetAfterImport: SelectedTopic != null);
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
                AppLogger.Info($"OpenViewerDialogStart id={card.Item.Id}; kind={card.Item.Kind}");
                var viewer = new ViewerWindow(_context, card.Item) { Owner = this };
                viewer.ShowDialog();
                AppLogger.Info($"OpenViewerDialogEnd id={card.Item.Id}; kind={card.Item.Kind}; hasChanges={viewer.HasChanges}");

                // When the instant-lock hotkey closes a modal viewer, ShowDialog returns into this
                // method while the old MainWindow is already being hidden/disposed. Do not touch
                // the old vault context in that case; the new unlocked MainWindow will reload data.
                if (_locking || _isLockTransitionRunning || !IsVisible)
                    return;

                // Viewing-only open/close should not reload the whole media list. Reload only when
                // the viewer actually changed vault data, such as favorite, topic, thumbnail, or delete.
                if (viewer.HasChanges)
                    ReloadAll();

                return;
            }

            if (MediaVaultService.IsDefaultOpenBlocked(card.Item))
            {
                MessageDialog.Show(
                    this,
                    $"'{card.Item.OriginalName}' 파일은 보안상 Windows 기본 앱으로 바로 열 수 없습니다.\n\n{MediaVaultService.GetSecurityWarningText(card.Item)}\n\n필요하면 파일을 내보낸 뒤 직접 확인해 주세요.",
                    "파일 열기 제한",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
            AppLogger.Error("OpenSelectedMedia failed", ex);
            if (_locking || _isLockTransitionRunning)
                return;

            MessageDialog.Show(this, "항목을 열 수 없습니다. 오류 로그를 저장했습니다.\n\n" + ex.Message + "\n\n로그 위치: " + AppLogger.LogDirectory, "열기", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ConfirmSecuritySensitiveExport(IReadOnlyList<MediaItem> items, string actionTitle)
    {
        var risky = items.Where(MediaVaultService.IsDefaultOpenBlocked).ToList();
        if (risky.Count == 0)
            return true;

        var sample = string.Join("\n", risky.Take(8).Select(item => "• " + item.OriginalName));
        var more = risky.Count > 8 ? $"\n외 {risky.Count - 8}개" : string.Empty;
        var message =
            "실행성 동작을 할 수 있는 파일 형식이 포함되어 있습니다.\n\n" +
            sample + more +
            "\n\n복호화된 파일로 내보낸 뒤 사용자가 직접 실행/열람할 수 있습니다. 신뢰할 수 있는 파일인지 확인한 뒤 진행하세요.\n\n" +
            "계속 진행할까요?";

        return MessageDialog.Show(
            this,
            message,
            actionTitle + " 주의",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
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
        if (!ConfirmSecuritySensitiveExport([item], "내보내기"))
            return;

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

        var confirm = MessageDialog.Show(this, $"선택한 항목 {selected.Count}개를 Vault에서 삭제할까요?\n삭제하면 복구할 수 없습니다.", "미디어 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
                // Best effort: immediately remove sensitive child windows from view.
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
                // Best effort: the vault lock must continue even if a child window is already closing.
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

            // Hide first so videos, images, and modal dialogs disappear immediately before heavier media cleanup runs.
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

                // Keep explicit shutdown mode until the old hidden window is fully closed.
                // Restoring OnMainWindowClose before closing the old owner can terminate the app
                // during modal viewer handoff on some WPF/MediaElement paths.
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
            StatusText.Text = "자동 잠금 시간이 지나 Vault를 잠급니다.";
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



    private static string BuildFolderItemSummary(int mediaCount, int childFolderCount)
    {
        var parts = new List<string>();
        if (mediaCount > 0)
            parts.Add($"파일 {mediaCount:N0}개");
        if (childFolderCount > 0)
            parts.Add($"하위 폴더 {childFolderCount:N0}개");
        return parts.Count == 0 ? "빈 폴더" : string.Join(" · ", parts);
    }

    private static string BuildFolderTreeCountText(int mediaCount, int childFolderCount)
    {
        if (mediaCount == 0 && childFolderCount == 0)
            return "비어 있음";

        var parts = new List<string>();
        if (mediaCount > 0)
            parts.Add($"파일 {mediaCount:N0}");
        if (childFolderCount > 0)
            parts.Add($"폴더 {childFolderCount:N0}");
        return string.Join(" · ", parts);
    }

    public sealed class FolderTreeItemViewModel : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private bool _isSelected;

        public string Name { get; }
        public string? FolderId { get; }
        public string Icon { get; }
        public string ToolTip { get; }
        public bool IsRoot { get; }
        public bool CanRenameOrDelete { get; }
        public string CountText { get; }
        public ObservableCollection<FolderTreeItemViewModel> Children { get; } = [];

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public FolderTreeItemViewModel(string name, string? folderId, string icon, string toolTip, bool isRoot, bool canRenameOrDelete, int mediaCount, int childFolderCount)
        {
            Name = string.IsNullOrWhiteSpace(name) ? (isRoot ? "루트" : "이름 없는 폴더") : name.Trim();
            FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
            Icon = string.IsNullOrWhiteSpace(icon) ? (isRoot ? "⌂" : "📁") : icon;
            var summary = BuildFolderItemSummary(mediaCount, childFolderCount);
            ToolTip = string.IsNullOrWhiteSpace(toolTip) ? $"{Name} · {summary}" : $"{toolTip} · {summary}";
            IsRoot = isRoot;
            CanRenameOrDelete = canRenameOrDelete;
            CountText = BuildFolderTreeCountText(mediaCount, childFolderCount);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class FlattenedTopicFolder
    {
        public TopicFolder Folder { get; }
        public string Path { get; }

        public FlattenedTopicFolder(TopicFolder folder, string path)
        {
            Folder = folder;
            Path = string.IsNullOrWhiteSpace(path) ? folder.Name : path;
        }
    }

    private sealed class FolderMoveTarget
    {
        public string? FolderId { get; }
        public string DisplayName { get; }

        public FolderMoveTarget(string? folderId, string displayName)
        {
            FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "루트" : displayName;
        }
    }

    private sealed class FolderToFolderMoveTarget
    {
        public string SourceFolderId { get; }
        public string? TargetParentFolderId { get; }
        public string TargetDisplayName { get; }

        public FolderToFolderMoveTarget(string sourceFolderId, string? targetParentFolderId, string targetDisplayName)
        {
            SourceFolderId = sourceFolderId;
            TargetParentFolderId = string.IsNullOrWhiteSpace(targetParentFolderId) ? null : targetParentFolderId;
            TargetDisplayName = string.IsNullOrWhiteSpace(targetDisplayName) ? "루트" : targetDisplayName;
        }
    }

    public sealed class TopicFolderCardViewModel
    {
        public string Id { get; }
        public string Name { get; }
        public string SummaryText { get; }
        public string ToolTipText { get; }

        public TopicFolderCardViewModel(TopicFolder folder)
        {
            Id = folder.Id;
            Name = string.IsNullOrWhiteSpace(folder.Name) ? "이름 없는 폴더" : folder.Name;
            SummaryText = BuildFolderItemSummary(folder.MediaCount, folder.ChildFolderCount);
            ToolTipText = $"{Name} · {SummaryText}\n클릭하면 열기 · 파일/폴더를 드롭하면 이 폴더로 이동";
        }
    }

    public sealed class MediaTagFilterOption
    {
        public string? Id { get; }
        public string Name { get; }
        public string DisplayName { get; }
        public Brush ColorBrush { get; }

        public MediaTagFilterOption(string? id, string name, string color, int itemCount, bool showCountForAll = false)
        {
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? "이름 없음" : name.Trim();
            DisplayName = string.IsNullOrWhiteSpace(id) && !showCountForAll ? Name : $"{Name} ({itemCount})";
            ColorBrush = CreateBrush(color);
        }

        public static MediaTagFilterOption CreateAll(int itemCount) => new(null, "전체 라벨", "#64748B", itemCount, showCountForAll: true);

        private static Brush CreateBrush(string color)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(color) && ColorConverter.ConvertFromString(color.Trim()) is Color parsed)
                {
                    var brush = new SolidColorBrush(parsed);
                    brush.Freeze();
                    return brush;
                }
            }
            catch
            {
                // Fall through to a safe neutral color.
            }

            var fallback = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            fallback.Freeze();
            return fallback;
        }
    }

}




