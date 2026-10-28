using System.Globalization;

namespace JinRiJian.Core;

public enum WorkStatus { Pending, Running, Completed }
public enum DisplayMode { Standard, Compact }

public sealed record WorkItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required DateOnly Date { get; init; }
    public required string Title { get; init; }
    public string Note { get; init; } = "";
    public TimeOnly? PlannedStart { get; init; }
    public TimeOnly? PlannedEnd { get; init; }
    public WorkStatus Status { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string StartTimeZone { get; init; } = "";
    public string CompletionTimeZone { get; init; } = "";
    public int SortOrder { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public long Version { get; init; }
    public double? ElapsedMinutes => StartedAt is { } start && CompletedAt is { } end
        ? Math.Round((end - start).TotalMinutes, 2) : null;
    public string StatusText => Status switch { WorkStatus.Pending => "待开始", WorkStatus.Running => "进行中", _ => "已完成" };
    public string PlannedText => (PlannedStart, PlannedEnd) switch
    {
        ({ } s, { } e) => $"计划 {s:HH:mm} — {e:HH:mm}",
        ({ } s, null) => $"预计 {s:HH:mm} 开始",
        (null, { } e) => $"预计 {e:HH:mm} 结束",
        _ => ""
    };
    public string ActualText => StartedAt is not { } start ? "尚未开始" : CompletedAt is { } end
        ? $"{start:MM-dd HH:mm} — {end:MM-dd HH:mm}" : $"{start:MM-dd HH:mm} 开始";
}

public sealed record WorkInput(string Title, TimeOnly? PlannedStart = null, TimeOnly? PlannedEnd = null, string Note = "")
{
    public WorkInput Validate()
    {
        var title = Title.Trim();
        if (title.Length == 0) throw new ArgumentException("请填写事项名称。");
        if (title.Length > 200) throw new ArgumentException("事项名称不能超过 200 字。");
        if (Note.Length > 10000) throw new ArgumentException("备注不能超过 10000 字。");
        if (PlannedStart is { } start && PlannedEnd is { } end && end <= start)
            throw new ArgumentException("预计结束应晚于预计开始，也可以留空。");
        return this with { Title = title, Note = Note.Trim() };
    }
    public static TimeOnly? ParseTime(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (TimeOnly.TryParseExact(text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value)) return value;
        throw new ArgumentException("预计时间请按 HH:mm 填写，例如 09:30，或留空。");
    }
}

public interface IClock
{
    DateTimeOffset Now { get; }
    TimeZoneInfo Zone { get; }
    DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
    public TimeZoneInfo Zone => TimeZoneInfo.Local;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}

public sealed record WindowBounds(double Left, double Top, double Width, double Height, string Monitor = "", double Dpi = 1);
public sealed record UserSettings
{
    public DisplayMode Mode { get; init; } = DisplayMode.Standard;
    public WindowBounds? StandardBounds { get; init; }
    public WindowBounds? CompactBounds { get; init; }
    public bool Topmost { get; init; }
    public bool AutoStart { get; init; }
    public string Theme { get; init; } = "system";
    public string LastExportDirectory { get; init; } = "";
    public string LastAutomaticBackupDate { get; init; } = "";
}

public sealed record WorkEvent(long Id, string TaskId, string Type, DateTimeOffset At, string? Before, string After);
public sealed class ConcurrentEditException() : InvalidOperationException("事项已在其他操作中更新，请刷新后再修改。");
