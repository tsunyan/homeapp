using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeApp.Core;

public enum WidgetKind { Clock, Calendar, Mail, Notifications, Rss, Json, Note }
public enum AppTheme { System, Light, Dark }
public enum DisplayDensity { Comfortable, Compact, Spacious }

public sealed class WidgetState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WidgetKind Kind { get; set; }
    public string Title { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 300;
    public string Note { get; set; } = "";
    public string Json { get; set; } = SampleData.DefaultJson;
    public bool ShowSeconds { get; set; }
    public bool Use24HourClock { get; set; } = true;

    public WidgetState Copy() => WorkspaceJson.Clone(this);
}

public sealed class BoardState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "マイホーム";
    public List<WidgetState> Widgets { get; set; } = [];
    public double Zoom { get; set; } = 1;
    public bool HasInitialLayout { get; set; }
}

public sealed class WorkspaceState
{
    public int SchemaVersion { get; set; } = 1;
    public List<BoardState> Boards { get; set; } = [];
    public Guid ActiveBoardId { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;
    public DisplayDensity Density { get; set; } = DisplayDensity.Comfortable;
    public bool SnapToGrid { get; set; } = true;
    public double GridSize { get; set; } = 16;
    public bool ShowGrid { get; set; } = true;

    [JsonIgnore]
    public BoardState ActiveBoard => Boards.First(b => b.Id == ActiveBoardId);

    public WorkspaceState Copy() => WorkspaceJson.Clone(this);

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("この版では開けない保存形式です。");
        if (Boards is null || Boards.Count is < 1 or > 30) throw new InvalidDataException("ボード数が不正です。");
        if (Boards.Any(b => b is null) || Boards.Select(b => b.Id).Distinct().Count() != Boards.Count)
            throw new InvalidDataException("ボードの識別子が不正です。");
        if (!Boards.Any(b => b.Id == ActiveBoardId)) ActiveBoardId = Boards[0].Id;
        if (!Enum.IsDefined(Theme) || !Enum.IsDefined(Density)) throw new InvalidDataException("表示設定が不正です。");
        GridSize = LayoutMath.ClampFinite(GridSize, 4, 64, 16);
        foreach (var board in Boards)
        {
            if (string.IsNullOrWhiteSpace(board.Name)) board.Name = "ボード";
            board.Zoom = LayoutMath.ClampFinite(board.Zoom, .35, 2, 1);
            if (board.Widgets is null || board.Widgets.Count > 100 || board.Widgets.Any(w => w is null))
                throw new InvalidDataException("ウィジェットの構成が不正です。");
            if (board.Widgets.Select(w => w.Id).Distinct().Count() != board.Widgets.Count)
                throw new InvalidDataException("ウィジェットの識別子が重複しています。");
            foreach (var widget in board.Widgets)
            {
                if (!Enum.IsDefined(widget.Kind)) throw new InvalidDataException("未対応のウィジェットです。");
                LayoutMath.Normalize(widget);
                widget.Title = string.IsNullOrWhiteSpace(widget.Title) ? SampleData.Title(widget.Kind) : widget.Title;
                widget.Note ??= "";
                widget.Json ??= SampleData.DefaultJson;
            }
        }
    }
}

public static class WorkspaceJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}

public static class LayoutMath
{
    public const double MinimumWidth = 240;
    public const double MinimumHeight = 180;
    public const double MaximumCoordinate = 100_000;
    public const double MaximumSize = 8_192;

    public static double ClampFinite(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public static double Snap(double value, bool enabled, double gridSize) =>
        enabled ? Math.Round(value / gridSize, MidpointRounding.AwayFromZero) * gridSize : value;

    public static void Normalize(WidgetState widget)
    {
        widget.X = ClampFinite(widget.X, 0, MaximumCoordinate, 0);
        widget.Y = ClampFinite(widget.Y, 0, MaximumCoordinate, 0);
        widget.Width = ClampFinite(widget.Width, MinimumWidth, MaximumSize, 360);
        widget.Height = ClampFinite(widget.Height, MinimumHeight, MaximumSize, 300);
    }

    public static (double Width, double Height) Extent(IEnumerable<WidgetState> widgets, double viewportWidth, double viewportHeight, double zoom)
    {
        var list = widgets.ToList();
        zoom = ClampFinite(zoom, .35, 2, 1);
        return (Math.Max(viewportWidth / zoom, list.Count == 0 ? 0 : list.Max(w => w.X + w.Width) + 48),
                Math.Max(viewportHeight / zoom, list.Count == 0 ? 0 : list.Max(w => w.Y + w.Height) + 48));
    }

    // Reflow is an explicit user action. A window or monitor resize never mutates saved geometry.
    public static void Arrange(IList<WidgetState> widgets, double availableWidth, double gap = 16)
    {
        var width = Math.Max(MinimumWidth + gap * 2, availableWidth);
        double x = gap, y = gap, rowHeight = 0;
        foreach (var widget in widgets)
        {
            Normalize(widget);
            if (x > gap && x + widget.Width + gap > width)
            {
                x = gap;
                y += rowHeight + gap;
                rowHeight = 0;
            }
            widget.X = x;
            widget.Y = y;
            x += widget.Width + gap;
            rowHeight = Math.Max(rowHeight, widget.Height);
        }
    }

    public static void InitialLayout(BoardState board, double availableWidth)
    {
        var columns = Math.Clamp((int)(availableWidth / 360), 1, 8);
        var width = Math.Clamp((availableWidth - 16 * (columns + 1)) / columns, 280, 520);
        var bottoms = new double[columns];
        Array.Fill(bottoms, 16);
        foreach (var widget in board.Widgets)
        {
            var col = Array.IndexOf(bottoms, bottoms.Min());
            widget.Width = width;
            widget.X = 16 + col * (width + 16);
            widget.Y = bottoms[col];
            bottoms[col] += widget.Height + 16;
        }
        board.HasInitialLayout = true;
    }
}
