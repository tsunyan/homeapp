using System.Text.Json;

namespace HomeApp.Core;

public sealed record WidgetOption(WidgetKind Kind, string Title, string Description, string Glyph);
public sealed record SampleEntry(string Heading, string Detail, string Meta);
public sealed record JsonItem(string Label, string Value);

public static class SampleData
{
    public const string DefaultJson = """
        {
          "title": "WuWa",
          "items": [
            { "label": "スタミナ", "value": "187 / 240" },
            { "label": "全回復", "value": "16:32" },
            { "label": "デイリー", "value": "3 / 4" }
          ]
        }
        """;

    public static readonly WidgetOption[] Options =
    [
        new(WidgetKind.Clock, "時計", "いまの時刻と日付。秒表示もお好みで。", "\uE823"),
        new(WidgetKind.Calendar, "カレンダー", "一日の予定を、見やすいタイムラインで。", "\uE787"),
        new(WidgetKind.Mail, "メール", "気になるメールを、まとめて確認。", "\uE715"),
        new(WidgetKind.Notifications, "Windows通知", "アプリからのお知らせをひとつの場所に。", "\uE7F4"),
        new(WidgetKind.Rss, "RSS / ニュース", "いつもの情報源から、新しい読みもの。", "\uE8A1"),
        new(WidgetKind.Json, "カスタムデータ", "JSONのラベルと値を、自分のパネルに。", "\uE943"),
        new(WidgetKind.Note, "メモ", "思いついたことを、その場で書き留める。", "\uE70B")
    ];

    public static string Title(WidgetKind kind) => Options.First(o => o.Kind == kind).Title;
    public static string Glyph(WidgetKind kind) => Options.First(o => o.Kind == kind).Glyph;
    public static bool IsSample(WidgetKind kind) => kind is not WidgetKind.Clock and not WidgetKind.Note;

    public static WidgetState Create(WidgetKind kind) => new()
    {
        Kind = kind,
        Title = Title(kind),
        Height = kind switch
        {
            WidgetKind.Clock => 224,
            WidgetKind.Calendar => 408,
            WidgetKind.Mail => 360,
            WidgetKind.Notifications => 328,
            WidgetKind.Rss => 344,
            WidgetKind.Json => 288,
            _ => 280
        },
        Note = kind == WidgetKind.Note ? "今日やりたいこと\n\n・デスクまわりを整える\n・気になった記事を読む\n\nここは自由に書き換えられます。" : ""
    };

    public static WorkspaceState CreateWorkspace()
    {
        var home = new BoardState
        {
            Widgets = new[] { WidgetKind.Clock, WidgetKind.Calendar, WidgetKind.Mail, WidgetKind.Notifications, WidgetKind.Note, WidgetKind.Json, WidgetKind.Rss }.Select(Create).ToList()
        };
        var focus = new BoardState
        {
            Name = "フォーカス",
            Widgets = new[] { WidgetKind.Clock, WidgetKind.Note, WidgetKind.Calendar }.Select(Create).ToList()
        };
        return new() { Boards = [home, focus], ActiveBoardId = home.Id };
    }

    public static SampleEntry[] Calendar =>
    [
        new("今日のプランを整理", "一日の優先順位を決める", "09:30 – 10:00"),
        new("デザインのアイデアをまとめる", "ホーム画面の使い方を考える", "13:00 – 14:00"),
        new("少し外を歩く", "画面から離れてリフレッシュ", "16:30 – 17:00")
    ];

    public static SampleEntry[] Mail =>
    [
        new("ホーム画面のアイデア", "気になった配置をメモにまとめました。", "HomeApp チーム · 09:42"),
        new("週末の読みもの", "今週のピックアップをお届けします。", "サンプルレター · 08:15"),
        new("ミーティングのお知らせ", "今日の予定をご確認ください。", "サンプル事務局 · 昨日")
    ];

    public static SampleEntry[] Notifications =>
    [
        new("まもなく予定の時間です", "デザインのアイデアをまとめる", "カレンダー · 5分前"),
        new("作業内容を同期しました", "すべての変更が保存されています。", "サンプルアプリ · 18分前"),
        new("ひと休みしませんか", "少し体を動かす時間です。", "フォーカス · 35分前")
    ];

    public static SampleEntry[] Rss =>
    [
        new("小さく始める、心地よいデスクづくり", "身のまわりの道具と余白を見直す。", "暮らしのノート · サンプル記事"),
        new("自分に合う情報の並べ方を探す", "見たい情報を、見たい場所に。", "Digital Life · サンプル記事"),
        new("週末に試したい、小さなものづくり", "ひとつのアイデアを形にする楽しさ。", "ものづくり通信 · サンプル記事")
    ];

    public static IReadOnlyList<JsonItem> ParseJson(string json)
    {
        if (json.Length > 100_000) throw new FormatException("JSONは100,000文字以内にしてください。");
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new FormatException("items配列を指定してください。");
            if (items.GetArrayLength() > 100) throw new FormatException("項目は100件以内にしてください。");
            var result = new List<JsonItem>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.String || !item.TryGetProperty("value", out var value))
                    throw new FormatException("各項目に文字列のlabelとvalueが必要です。");
                var text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString() ?? "",
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "—",
                    _ => throw new FormatException("valueには文字列・数値・真偽値・nullを指定してください。")
                };
                result.Add(new(label.GetString()!, text));
            }
            return result;
        }
        catch (JsonException ex) { throw new FormatException("JSONの書式を確認してください。", ex); }
    }
}
