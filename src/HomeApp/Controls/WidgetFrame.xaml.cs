using HomeApp.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Microsoft.UI.Text;

namespace HomeApp.Controls;

public sealed partial class WidgetFrame : UserControl
{
    private TextBlock? _time;
    private TextBlock? _date;
    private bool _calendarMonthView = true;
    private DateTimeOffset _calendarDate = DateTimeOffset.Now;
    private Point _startPoint;
    private WidgetState? _startGeometry;
    private bool _resizing;
    private UIElement? _capturedHandle;

    public WidgetState Model { get; }
    public UIElement CoordinateSpace { get; set; } = null!;
    public bool IsEditing { get; private set; }
    public bool SnapEnabled { get; set; }
    public double SnapSize { get; set; } = 16;
    public DisplayDensity Density { get; private set; }
    public event EventHandler? EditRequested;
    public event EventHandler? DuplicateRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? GestureStarted;
    public event EventHandler? GeometryChanged;
    public event EventHandler? GestureCompleted;
    public event EventHandler? NoteChanged;
    public event EventHandler<SampleEntry>? DetailRequested;
    public event EventHandler? JsonEditRequested;

    public WidgetFrame(WidgetState model, DisplayDensity density)
    {
        InitializeComponent();
        Model = model;
        Density = density;
        WidgetIcon.Glyph = SampleData.Glyph(model.Kind);
        RefreshGeometry();
        RefreshContent();
        Unloaded += (_, _) => CancelContentRequest();
        MoveHandle.PointerEntered += (_, _) => { if (IsEditing) ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll); };
        MoveHandle.PointerExited += (_, _) => ProtectedCursor = null;
        ResizeHandle.PointerEntered += (_, _) => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthwestSoutheast);
        ResizeHandle.PointerExited += (_, _) => ProtectedCursor = null;
    }

    private double FontSizeForDensity => Density switch { DisplayDensity.Compact => 12, DisplayDensity.Spacious => 16, _ => 14 };
    private double Spacing => Density switch { DisplayDensity.Compact => 5, DisplayDensity.Spacious => 14, _ => 9 };
    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    public void SetEditing(bool editing, bool selected)
    {
        IsEditing = editing;
        DragIndicator.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ResizeHandle.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        Card.BorderBrush = ThemeBrush(selected && editing ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush");
        AutomationProperties.SetName(MoveHandle, editing ? $"{Model.Title} を移動" : Model.Title);
        ToolTipService.SetToolTip(MoveHandle, editing ? "ドラッグして移動。細かな調整は「位置・サイズ・設定」から。" : Model.Title);
    }

    public void RefreshGeometry()
    {
        Width = Model.Width;
        Height = Model.Height;
        Canvas.SetLeft(this, Model.X);
        Canvas.SetTop(this, Model.Y);
        TitleText.Text = Model.Title;
        AutomationProperties.SetName(this, Model.Title);
        AutomationProperties.SetName(MoreButton, $"{Model.Title} の操作");
    }

    private TextBlock Text(string text, double? size = null, bool muted = false) => new()
    {
        Text = text,
        FontSize = size ?? FontSizeForDensity,
        TextWrapping = TextWrapping.Wrap,
        Foreground = ThemeBrush(muted ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush")
    };

    public void RefreshContent()
    {
        CancelContentRequest();
        TitleText.Text = Model.Title;
        StatusText.Text = SampleData.IsSample(Model.Kind) ? "サンプルデータ · 未接続" : Model.Kind == WidgetKind.Clock ? "このPCの時刻" : "このPCに保存";
        _time = null;
        _date = null;
        Body.Content = Model.Kind switch
        {
            WidgetKind.Clock => BuildClock(),
            WidgetKind.Calendar => BuildCalendar(),
            WidgetKind.Mail => BuildMail(),
            WidgetKind.Notifications => BuildEntries(SampleData.Notifications, "最近のお知らせ", "3 件"),
            WidgetKind.Rss => BuildRss(),
            WidgetKind.Json => BuildJson(),
            _ => BuildNote()
        };
    }

    private UIElement BuildClock()
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 8 };
        _time = Text("", Model.ShowSeconds ? 48 : 64);
        _time.FontFamily = new FontFamily("Segoe UI Variable Display");
        _time.FontWeight = FontWeights.SemiBold;
        _time.CharacterSpacing = -40;
        _date = Text("", 14, true);
        panel.Children.Add(_time);
        panel.Children.Add(_date);
        UpdateClock();
        return new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    public void UpdateClock()
    {
        if (_time is null || _date is null) return;
        var now = DateTime.Now;
        var format = Model.Use24HourClock ? "HH:mm" : "hh:mm";
        if (Model.ShowSeconds) format += ":ss";
        var value = now.ToString(format);
        if (_time.Text != value) _time.Text = value;
        _date.Text = now.ToString("M月d日 dddd", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")) +
                     (Model.Use24HourClock ? "" : now.Hour < 12 ? " · 午前" : " · 午後");
    }

    private UIElement BuildCalendar()
    {
        var grid = new Grid { RowSpacing = Spacing };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var mode = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "月表示", "予定一覧" },
            SelectedIndex = _calendarMonthView ? 0 : 1
        };
        AutomationProperties.SetName(mode, "カレンダーの表示形式");
        grid.Children.Add(mode);
        var content = new StackPanel { Spacing = Spacing };
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        var picker = new CalendarDatePicker { Date = _calendarDate, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(picker, "予定を表示する日");
        content.Children.Add(picker);
        var agenda = new ContentControl { MinHeight = 90, MaxHeight = 240, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var calendar = new CalendarView
        {
            SelectionMode = CalendarViewSelectionMode.Single,
            DisplayMode = CalendarViewDisplayMode.Month,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0,
            Height = 300
        };
        AutomationProperties.SetName(calendar, "月表示カレンダー");
        calendar.SetDisplayDate(_calendarDate);
        calendar.SelectedDates.Add(_calendarDate);
        content.Children.Add(calendar);
        var selectedDay = Text($"{_calendarDate:M月d日}の予定", 12, true);
        content.Children.Add(selectedDay);
        var refresh = new Button { Content = "予定を更新" };
        refresh.Click += async (_, _) => await LoadCalendarAsync(agenda, _calendarDate);
        content.Children.Add(refresh);
        content.Children.Add(agenda);
        agenda.Loaded += async (_, _) => await LoadCalendarAsync(agenda, _calendarDate);
        void UpdateMode()
        {
            calendar.Visibility = _calendarMonthView ? Visibility.Visible : Visibility.Collapsed;
            picker.Visibility = _calendarMonthView ? Visibility.Collapsed : Visibility.Visible;
        }
        async Task UpdateDateAsync(DateTimeOffset date)
        {
            if (_calendarDate.Date == date.Date) return;
            _calendarDate = date;
            selectedDay.Text = $"{date:M月d日}の予定";
            await LoadCalendarAsync(agenda, date);
        }
        picker.DateChanged += async (_, _) =>
        {
            if (picker.Date is not { } date) return;
            if (calendar.SelectedDates.Count != 1 || calendar.SelectedDates[0].Date != date.Date)
            {
                calendar.SelectedDates.Clear();
                calendar.SelectedDates.Add(date);
                calendar.SetDisplayDate(date);
            }
            await UpdateDateAsync(date);
        };
        calendar.SelectedDatesChanged += async (_, args) =>
        {
            if (args.AddedDates.Count == 0) return;
            var date = args.AddedDates[0];
            picker.Date = date;
            await UpdateDateAsync(date);
        };
        mode.SelectionChanged += (_, _) =>
        {
            _calendarMonthView = mode.SelectedIndex == 0;
            UpdateMode();
        };
        UpdateMode();
        return grid;
    }

    private UIElement BuildEntries(SampleEntry[] entries, string caption, string count, bool unread = false)
    {
        var grid = new Grid { RowSpacing = 8 };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(Text(caption, 12, true));
        var number = Text(count, 12, true);
        Grid.SetColumn(number, 1);
        heading.Children.Add(number);
        grid.Children.Add(heading);
        var list = EntryList(entries, false, unread);
        Grid.SetRow(list, 1);
        grid.Children.Add(list);
        return grid;
    }

    private ListView EntryList(IEnumerable<SampleEntry> entries, bool calendar = false, bool unread = false)
    {
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            Padding = new(0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var entry in entries)
        {
            var panel = new StackPanel { Spacing = 4, Padding = new(0, Spacing, 0, Spacing) };
            if (calendar)
            {
                var time = Text(entry.Meta, 12);
                time.Foreground = ThemeBrush("AccentTextFillColorPrimaryBrush");
                time.FontWeight = FontWeights.SemiBold;
                panel.Children.Add(time);
            }
            var heading = Text(entry.Heading);
            heading.FontWeight = unread || calendar ? FontWeights.SemiBold : FontWeights.Normal;
            heading.MaxLines = 2;
            heading.TextTrimming = TextTrimming.CharacterEllipsis;
            panel.Children.Add(heading);
            var detail = Text(calendar ? entry.Detail : entry.Meta, 11, true);
            detail.MaxLines = 1;
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            panel.Children.Add(detail);
            list.Items.Add(new ListViewItem { Content = panel, Tag = entry, Padding = new(4, 0, 4, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        list.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is ListViewItem { Tag: SampleEntry entry }) DetailRequested?.Invoke(this, entry);
        };
        return list;
    }

    private UIElement BuildNote()
    {
        var note = new TextBox
        {
            Text = Model.Note,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "メモを書いてください…",
            FontSize = FontSizeForDensity,
            VerticalAlignment = VerticalAlignment.Stretch,
            MaxLength = 10000,
            Padding = new(10),
            BorderThickness = new(0)
        };
        AutomationProperties.SetName(note, $"{Model.Title} の内容");
        note.TextChanged += (_, _) =>
        {
            Model.Note = note.Text;
            NoteChanged?.Invoke(this, EventArgs.Empty);
        };
        return note;
    }

    private UIElement BuildJson()
    {
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var items = new StackPanel { Spacing = Spacing + 3 };
        try
        {
            var rows = SampleData.ParseJson(Model.Json);
            if (rows.Count == 0) items.Children.Add(Text("表示する項目がありません。", 14, true));
            foreach (var item in rows)
            {
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = new(1.3, GridUnitType.Star) });
                var label = Text(item.Label, 12, true);
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(label);
                var value = Text(item.Value, 22);
                value.FontWeight = FontWeights.SemiBold;
                value.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                items.Children.Add(row);
            }
        }
        catch (FormatException ex) { items.Children.Add(Text(ex.Message, 13, true)); }
        root.Children.Add(new ScrollViewer { Content = items, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var edit = new Button { Content = "JSONを編集", HorizontalAlignment = HorizontalAlignment.Left, Padding = new(10, 5, 10, 5) };
        edit.Click += (_, _) => JsonEditRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetRow(edit, 1);
        root.Children.Add(edit);
        return root;
    }

    private void BeginGesture(object sender, PointerRoutedEventArgs e, bool resizing)
    {
        if (!IsEditing || !e.GetCurrentPoint(CoordinateSpace).Properties.IsLeftButtonPressed) return;
        GestureStarted?.Invoke(this, EventArgs.Empty);
        _startPoint = e.GetCurrentPoint(CoordinateSpace).Position;
        _startGeometry = Model.Copy();
        _resizing = resizing;
        _capturedHandle = (UIElement)sender;
        _capturedHandle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Move_Pressed(object sender, PointerRoutedEventArgs e) => BeginGesture(sender, e, false);
    private void Resize_Pressed(object sender, PointerRoutedEventArgs e) => BeginGesture(sender, e, true);

    private void Handle_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_startGeometry is null) return;
        var point = e.GetCurrentPoint(CoordinateSpace).Position;
        var dx = point.X - _startPoint.X;
        var dy = point.Y - _startPoint.Y;
        if (_resizing)
        {
            Model.Width = LayoutMath.Snap(_startGeometry.Width + dx, SnapEnabled, SnapSize);
            Model.Height = LayoutMath.Snap(_startGeometry.Height + dy, SnapEnabled, SnapSize);
        }
        else
        {
            Model.X = LayoutMath.Snap(_startGeometry.X + dx, SnapEnabled, SnapSize);
            Model.Y = LayoutMath.Snap(_startGeometry.Y + dy, SnapEnabled, SnapSize);
        }
        LayoutMath.Normalize(Model);
        RefreshGeometry();
        GeometryChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void Handle_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_startGeometry is null) return;
        _capturedHandle?.ReleasePointerCapture(e.Pointer);
        EndGesture();
        e.Handled = true;
    }

    private void Handle_CaptureLost(object sender, PointerRoutedEventArgs e) => EndGesture();

    private void EndGesture()
    {
        if (_startGeometry is null) return;
        _startGeometry = null;
        _capturedHandle = null;
        GestureCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => EditRequested?.Invoke(this, EventArgs.Empty);
    private void Duplicate_Click(object sender, RoutedEventArgs e) => DuplicateRequested?.Invoke(this, EventArgs.Empty);
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
}
