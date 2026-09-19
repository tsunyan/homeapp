using System.Text.Json;
using HomeApp.Controls;
using HomeApp.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace HomeApp;

public sealed partial class DashboardPage : Page
{
    private readonly WorkspaceStore _store;
    private readonly Dictionary<Guid, WidgetFrame> _frames = [];
    private readonly Stack<BoardState> _undo = new();
    private readonly DispatcherQueueTimer _clock;
    private readonly DispatcherQueueTimer _autoSave;
    private WorkspaceState _state;
    private BoardState? _editSnapshot;
    private WidgetFrame? _selected;
    private string? _pendingWarning;

    // Set while the code updates a control whose change event would otherwise
    // be read back as user input and written to the model.
    private bool _syncing;

    public DashboardPage()
    {
        InitializeComponent();

        // Qualified: Microsoft.UI.Xaml.Shapes.Path is also in scope here.
        _store = new WorkspaceStore(Services.AppDataPaths.WorkspaceDirectory);
        _google = new(new Services.EncryptedGoogleStore(System.IO.Path.Combine(_store.DirectoryPath, "GoogleAuth")));
        var loaded = _store.Load();
        _state = loaded.State;
        _pendingWarning = loaded.Warning;

        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => { foreach (var frame in _frames.Values) frame.UpdateClock(); };

        // Saving is debounced: a drag raises a geometry change per pointer move.
        _autoSave = DispatcherQueue.CreateTimer();
        _autoSave.Interval = TimeSpan.FromMilliseconds(600);
        _autoSave.IsRepeating = false;
        _autoSave.Tick += async (_, _) => await SaveAsync();

        Loaded += OnLoaded;
        Unloaded += (_, _) => { _clock.Stop(); _autoSave.Stop(); };
    }

    private BoardState Board => _state.ActiveBoard;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildNavigation();
        ApplyTheme();
        RenderBoard();
        _clock.Start();
        if (_pendingWarning is not null)
        {
            Notify(_pendingWarning, InfoBarSeverity.Warning);
            _pendingWarning = null;
        }
        await RestoreGoogleAsync();
    }

    private void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        MessageBar.Severity = severity;
        MessageBar.Message = message;
        MessageBar.IsOpen = true;
    }

    private void ApplyTheme() => RequestedTheme = _state.Theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    // ---- navigation ----

    private void BuildNavigation()
    {
        _syncing = true;
        Navigation.MenuItems.Clear();
        Navigation.MenuItems.Add(new NavigationViewItemHeader { Content = "マイボード" });
        foreach (var board in _state.Boards)
        {
            var item = new NavigationViewItem
            {
                Content = board.Name,
                Tag = board.Id,
                Icon = new SymbolIcon(Symbol.Home)
            };
            Navigation.MenuItems.Add(item);
            if (board.Id == _state.ActiveBoardId) Navigation.SelectedItem = item;
        }
        _syncing = false;
    }

    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncing) return;
        if (args.IsSettingsSelected) { await ShowSettingsAsync(); return; }
        if (args.SelectedItem is not NavigationViewItem item) return;

        switch (item.Tag)
        {
            case Guid id when id != _state.ActiveBoardId:
                _state.ActiveBoardId = id;
                LeaveEditMode();
                RenderBoard();
                await SaveAsync();
                break;
            case "add-board":
                await AddBoardAsync();
                break;
            case "help":
                await ShowHelpAsync();
                break;
        }
    }

    private async Task AddBoardAsync()
    {
        var name = await PromptAsync("ボードを追加", "ボード名", $"ボード {_state.Boards.Count + 1}");
        if (name is null)
        {
            // Restore the selection the cancelled footer item stole from the active board.
            SelectActiveBoardItem();
            return;
        }
        if (_state.Boards.Count >= 30)
        {
            Notify("ボードは30個までです。", InfoBarSeverity.Warning);
            SelectActiveBoardItem();
            return;
        }

        var board = new BoardState { Name = name };
        _state.Boards.Add(board);
        _state.ActiveBoardId = board.Id;
        LeaveEditMode();
        BuildNavigation();
        RenderBoard();
        await SaveAsync();
    }

    private void SelectActiveBoardItem()
    {
        _syncing = true;
        Navigation.SelectedItem = Navigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => i.Tag is Guid id && id == _state.ActiveBoardId);
        _syncing = false;
    }

    // ---- board rendering ----

    private void RenderBoard()
    {
        foreach (var frame in _frames.Values) BoardCanvas.Children.Remove(frame);
        _frames.Clear();
        _selected = null;
        InspectorSplit.IsPaneOpen = false;

        BoardTitle.Text = Board.Name;
        DayCaption.Text = DateTime.Now.ToString("yyyy年M月d日 dddd",
            System.Globalization.CultureInfo.GetCultureInfo("ja-JP"));

        if (!Board.HasInitialLayout && Board.Widgets.Count > 0)
            LayoutMath.InitialLayout(Board, Math.Max(Viewport.ActualWidth, 960));

        foreach (var widget in Board.Widgets) AddFrame(widget);

        _syncing = true;
        ZoomSlider.Value = Board.Zoom * 100;
        SnapToggle.IsOn = _state.SnapToGrid;
        _syncing = false;

        Viewport.ChangeView(null, null, (float)Board.Zoom, true);
        UpdateChrome();
    }

    private void AddFrame(WidgetState widget)
    {
        var frame = new WidgetFrame(widget, _state.Density)
        {
            CoordinateSpace = BoardCanvas,
            SnapEnabled = _state.SnapToGrid,
            SnapSize = _state.GridSize
        };
        frame.EditRequested += (s, _) => Select((WidgetFrame)s!);
        frame.DuplicateRequested += async (s, _) => await DuplicateAsync((WidgetFrame)s!);
        frame.DeleteRequested += async (s, _) => await DeleteAsync((WidgetFrame)s!);
        frame.GestureStarted += (s, _) => { PushUndo(); Select((WidgetFrame)s!); };
        frame.GeometryChanged += (s, _) => SyncInspector((WidgetFrame)s!);
        frame.GestureCompleted += (_, _) => { UpdateChrome(); QueueSave(); };
        frame.NoteChanged += (_, _) => QueueSave();
        frame.DetailRequested += async (_, entry) => await ShowDetailAsync(entry);
        frame.JsonEditRequested += async (s, _) => await EditJsonAsync((WidgetFrame)s!);
        frame.RssSettingsRequested += async (s, _) => await EditRssAsync((WidgetFrame)s!);
        frame.ConnectionRequested += async (_, _) => await ShowGoogleConnectionAsync();
        frame.ConnectedDetailRequested += async (_, detail) => await ShowConnectedDetailAsync(detail);
        frame.SetProviders(_google.Provider, _google.Provider);
        frame.SetEditing(EditButton.IsChecked == true, false);
        BoardCanvas.Children.Add(frame);
        _frames[widget.Id] = frame;
    }

    private void UpdateChrome()
    {
        var count = Board.Widgets.Count;
        WidgetCount.Text = count == 0 ? "ウィジェットなし" : $"{count} 個のウィジェット";
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var (width, height) = LayoutMath.Extent(Board.Widgets, Viewport.ActualWidth, Viewport.ActualHeight, Board.Zoom);
        BoardCanvas.Width = width;
        BoardCanvas.Height = height;
        DrawGrid(width, height);
    }

    private void DrawGrid(double width, double height)
    {
        GridLines.Children.Clear();
        if (!_state.ShowGrid || EditButton.IsChecked != true) return;

        var brush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        var zoom = LayoutMath.ClampFinite(Viewport.ZoomFactor, .35, 2, 1);
        var left = Math.Max(0, Viewport.HorizontalOffset / zoom);
        var top = Math.Max(0, Viewport.VerticalOffset / zoom);
        var right = Math.Min(width, left + Viewport.ActualWidth / zoom + _state.GridSize);
        var bottom = Math.Min(height, top + Viewport.ActualHeight / zoom + _state.GridSize);
        var firstX = Math.Max(_state.GridSize, Math.Floor(left / _state.GridSize) * _state.GridSize);
        var firstY = Math.Max(_state.GridSize, Math.Floor(top / _state.GridSize) * _state.GridSize);

        // Only draw the visible grid. A widget may live far across the free-form board,
        // and materializing thousands of off-screen Line elements would stall editing.
        for (var x = firstX; x < right; x += _state.GridSize)
            GridLines.Children.Add(new Line { X1 = x, Y1 = top, X2 = x, Y2 = bottom, Stroke = brush, StrokeThickness = .5, Opacity = .4 });
        for (var y = firstY; y < bottom; y += _state.GridSize)
            GridLines.Children.Add(new Line { X1 = left, Y1 = y, X2 = right, Y2 = y, Stroke = brush, StrokeThickness = .5, Opacity = .4 });
    }

    // ---- selection and inspector ----

    private void Select(WidgetFrame frame)
    {
        _selected = frame;
        foreach (var other in _frames.Values)
        {
            var selected = other == frame;
            other.SetEditing(EditButton.IsChecked == true, selected);
            Canvas.SetZIndex(other, selected ? 1 : 0);
        }
        SyncInspector(frame);
        InspectorSplit.IsPaneOpen = true;
    }

    private void SyncInspector(WidgetFrame frame)
    {
        if (_selected != frame) return;
        _syncing = true;
        var model = frame.Model;
        TitleBox.Text = model.Title;
        XBox.Value = model.X;
        YBox.Value = model.Y;
        WidthBox.Value = model.Width;
        HeightBox.Value = model.Height;
        ClockSettings.Visibility = model.Kind == WidgetKind.Clock ? Visibility.Visible : Visibility.Collapsed;
        SecondsToggle.IsOn = model.ShowSeconds;
        HourToggle.IsOn = model.Use24HourClock;
        _syncing = false;
    }

    private void CloseInspector_Click(object sender, RoutedEventArgs e) => InspectorSplit.IsPaneOpen = false;

    private void Title_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_syncing || _selected is null) return;
        var title = TitleBox.Text.Trim();
        _selected.Model.Title = string.IsNullOrEmpty(title) ? SampleData.Title(_selected.Model.Kind) : title;
        _selected.RefreshGeometry();
        QueueSave();
    }

    private void Geometry_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || _selected is null || !double.IsFinite(args.NewValue)) return;
        var model = _selected.Model;
        if (sender == XBox) model.X = args.NewValue;
        else if (sender == YBox) model.Y = args.NewValue;
        else if (sender == WidthBox) model.Width = args.NewValue;
        else if (sender == HeightBox) model.Height = args.NewValue;
        LayoutMath.Normalize(model);
        _selected.RefreshGeometry();
        UpdateChrome();
        QueueSave();
    }

    private void Clock_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing || _selected is null) return;
        _selected.Model.ShowSeconds = SecondsToggle.IsOn;
        _selected.Model.Use24HourClock = HourToggle.IsOn;
        _selected.RefreshContent();
        QueueSave();
    }

    private async void DuplicateSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) await DuplicateAsync(_selected);
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) await DeleteAsync(_selected);
    }

    private async Task DuplicateAsync(WidgetFrame frame)
    {
        if (Board.Widgets.Count >= 100) { Notify("ウィジェットは100個までです。", InfoBarSeverity.Warning); return; }
        PushUndo();
        var copy = frame.Model.Copy();
        copy.Id = Guid.NewGuid();
        copy.X += 24;
        copy.Y += 24;
        LayoutMath.Normalize(copy);
        Board.Widgets.Add(copy);
        AddFrame(copy);
        UpdateChrome();
        await SaveAsync();
    }

    private async Task DeleteAsync(WidgetFrame frame)
    {
        PushUndo();
        Board.Widgets.Remove(frame.Model);
        BoardCanvas.Children.Remove(frame);
        _frames.Remove(frame.Model.Id);
        if (_selected == frame) { _selected = null; InspectorSplit.IsPaneOpen = false; }
        UpdateChrome();
        await SaveAsync();
    }

    // ---- edit mode ----

    private void Edit_Checked(object sender, RoutedEventArgs e)
    {
        _editSnapshot = WorkspaceJson.Clone(Board);
        _undo.Clear();
        EditBar.Visibility = Visibility.Visible;
        UndoButton.Visibility = Visibility.Visible;
        SaveButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        foreach (var frame in _frames.Values) frame.SetEditing(true, frame == _selected);
        UpdateChrome();
    }

    private void Edit_Unchecked(object sender, RoutedEventArgs e) => LeaveEditMode();

    private void LeaveEditMode()
    {
        _editSnapshot = null;
        _undo.Clear();
        EditBar.Visibility = Visibility.Collapsed;
        UndoButton.Visibility = Visibility.Collapsed;
        SaveButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        if (EditButton.IsChecked == true) EditButton.IsChecked = false;
        foreach (var frame in _frames.Values) frame.SetEditing(false, false);
        UpdateChrome();
    }

    private void PushUndo()
    {
        if (EditButton.IsChecked != true || _undo.Count >= 50) return;
        _undo.Push(WorkspaceJson.Clone(Board));
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) { Notify("元に戻せる操作はありません。"); return; }
        RestoreBoard(_undo.Pop());
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        LeaveEditMode();
        await SaveAsync();
        Notify("配置を保存しました。", InfoBarSeverity.Success);
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_editSnapshot is not null) RestoreBoard(_editSnapshot);
        LeaveEditMode();
        await SaveAsync();
    }

    private void RestoreBoard(BoardState snapshot)
    {
        var index = _state.Boards.FindIndex(b => b.Id == snapshot.Id);
        if (index < 0) return;
        _state.Boards[index] = snapshot;
        _state.ActiveBoardId = snapshot.Id;
        RenderBoard();
    }

    private void Snap_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _state.SnapToGrid = SnapToggle.IsOn;
        foreach (var frame in _frames.Values) frame.SnapEnabled = _state.SnapToGrid;
        QueueSave();
    }

    private async void Arrange_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        LayoutMath.Arrange(Board.Widgets, Math.Max(Viewport.ActualWidth / Board.Zoom, 360));
        foreach (var frame in _frames.Values) frame.RefreshGeometry();
        UpdateChrome();
        await SaveAsync();
    }

    // ---- zoom ----

    private void Zoom_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        Viewport.ChangeView(null, null, (float)(e.NewValue / 100));
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e) => Viewport.ChangeView(null, null, 1f);

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (Board.Widgets.Count == 0 || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
        {
            Viewport.ChangeView(null, null, 1f);
            return;
        }
        var width = Board.Widgets.Max(w => w.X + w.Width) + 32;
        var height = Board.Widgets.Max(w => w.Y + w.Height) + 32;
        var factor = LayoutMath.ClampFinite(
            Math.Min(Viewport.ActualWidth / width, Viewport.ActualHeight / height), .35, 2, 1);
        Viewport.ChangeView(0, 0, (float)factor);
    }

    private void Viewport_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        Board.Zoom = LayoutMath.ClampFinite(Viewport.ZoomFactor, .35, 2, 1);
        _syncing = true;
        ZoomSlider.Value = Board.Zoom * 100;
        _syncing = false;
        ZoomReset.Content = $"{Board.Zoom * 100:0}%";
        UpdateChrome();
        QueueSave();
    }

    // A resize changes how much of the board is visible. It never moves widgets.
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateChrome();

    // ---- dialogs ----

    private async void AddWidget_Click(object sender, RoutedEventArgs e)
    {
        if (Board.Widgets.Count >= 100) { Notify("ウィジェットは100個までです。", InfoBarSeverity.Warning); return; }

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, Height = 320 };
        foreach (var option in SampleData.Options)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new(0, 6, 0, 6) };
            row.Children.Add(new FontIcon { Glyph = option.Glyph, FontSize = 18 });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = option.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = option.Description, FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(text);
            list.Items.Add(new ListViewItem { Content = row, Tag = option.Kind });
        }
        list.SelectedIndex = 0;

        var dialog = NewDialog("ウィジェットを追加", list, "追加");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (list.SelectedItem is not ListViewItem { Tag: WidgetKind kind }) return;

        PushUndo();
        var widget = SampleData.Create(kind);
        widget.X = 16;
        widget.Y = Board.Widgets.Count == 0 ? 16 : Board.Widgets.Max(w => w.Y + w.Height) + 16;
        LayoutMath.Normalize(widget);
        Board.Widgets.Add(widget);
        AddFrame(widget);
        UpdateChrome();
        await SaveAsync();
    }

    private async void RenameBoard_Click(object sender, RoutedEventArgs e)
    {
        var name = await PromptAsync("ボード名を変更", "ボード名", Board.Name);
        if (name is null) return;
        Board.Name = name;
        BoardTitle.Text = name;
        BuildNavigation();
        await SaveAsync();
    }

    private async void Settings_Click(object sender, RoutedEventArgs e) => await ShowSettingsAsync();

    private async Task ShowSettingsAsync()
    {
        var theme = new ComboBox { Header = "テーマ", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var name in new[] { "システムに合わせる", "ライト", "ダーク" }) theme.Items.Add(name);
        theme.SelectedIndex = (int)_state.Theme;

        var density = new ComboBox { Header = "表示の間隔", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var name in new[] { "標準", "コンパクト", "ゆったり" }) density.Items.Add(name);
        density.SelectedIndex = (int)_state.Density;

        var grid = new ToggleSwitch { Header = "グリッドを表示", IsOn = _state.ShowGrid };

        var panel = new StackPanel { Spacing = 14, Width = 300 };
        panel.Children.Add(theme);
        panel.Children.Add(density);
        panel.Children.Add(grid);
        var connection = new Button { Content = "Google接続設定" };
        panel.Children.Add(connection);

        // The settings item is not a board; leave the board selection where it was.
        SelectActiveBoardItem();

        var dialog = NewDialog("表示設定", panel, "適用");
        var openConnection = false;
        connection.Click += (_, _) => { openConnection = true; dialog.Hide(); };
        var result = await dialog.ShowAsync();
        if (openConnection) { await ShowGoogleConnectionAsync(); return; }
        if (result != ContentDialogResult.Primary) return;

        _state.Theme = (AppTheme)theme.SelectedIndex;
        _state.Density = (DisplayDensity)density.SelectedIndex;
        _state.ShowGrid = grid.IsOn;
        ApplyTheme();
        RenderBoard();
        await SaveAsync();
    }

    private async Task ShowHelpAsync()
    {
        SelectActiveBoardItem();
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Width = 340,
            Text = "「追加」から時計やメモを置けます。\n\n"
                 + "「配置を編集」を入れると、見出しをドラッグして移動、右下をつかんでサイズ変更ができます。\n\n"
                 + "変更は自動で保存されます。編集中は「キャンセル」で編集前に戻せます。"
        };
        await NewDialog("使い方", text, null).ShowAsync();
    }

    private async Task ShowDetailAsync(SampleEntry entry)
    {
        var panel = new StackPanel { Spacing = 8, Width = 320 };
        panel.Children.Add(new TextBlock { Text = entry.Detail, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = entry.Meta, FontSize = 12, Opacity = .7 });
        panel.Children.Add(new TextBlock { Text = "これはサンプルデータです。", FontSize = 12, Opacity = .7 });
        await NewDialog(entry.Heading, panel, null).ShowAsync();
    }

    private async Task EditJsonAsync(WidgetFrame frame)
    {
        var box = new TextBox
        {
            Text = frame.Model.Json,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 260,
            Width = 360,
            FontFamily = new FontFamily("Cascadia Mono")
        };
        var error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(box);
        panel.Children.Add(error);

        var dialog = NewDialog("JSONを編集", panel, "適用");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            // Keep the dialog open on malformed input rather than discarding the edit.
            try { SampleData.ParseJson(box.Text); }
            catch (FormatException ex)
            {
                error.Text = ex.Message;
                error.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        frame.Model.Json = box.Text;
        frame.RefreshContent();
        await SaveAsync();
    }

    private async Task<string?> PromptAsync(string title, string header, string value)
    {
        var box = new TextBox { Header = header, Text = value, MaxLength = 60, Width = 300 };
        if (await NewDialog(title, box, "決定").ShowAsync() != ContentDialogResult.Primary) return null;
        var text = box.Text.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private ContentDialog NewDialog(string title, object content, string? primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = primary is null ? "閉じる" : "キャンセル",
            DefaultButton = primary is null ? ContentDialogButton.Close : ContentDialogButton.Primary
        };
        if (primary is not null) dialog.PrimaryButtonText = primary;
        return dialog;
    }

    // ---- persistence ----

    private void QueueSave()
    {
        SaveStatus.Text = "変更あり";
        _autoSave.Start();
    }

    private async Task SaveAsync()
    {
        _autoSave.Stop();
        try
        {
            await _store.SaveAsync(_state);
            SaveStatus.Text = "このPCに保存済み";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            SaveStatus.Text = "保存できません";
            Notify($"保存に失敗しました。{ex.Message}", InfoBarSeverity.Error);
        }
    }
}
