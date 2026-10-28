namespace JinRiJian.Core;

public sealed class WorkService(WorkRepository repository, IClock clock)
{
    public WorkRepository Repository { get; } = repository;
    public IClock Clock { get; } = clock;
    public DateOnly Today => DateOnly.FromDateTime(Clock.Now.DateTime);

    public WorkItem Add(WorkInput input,DateOnly? date=null)
    {
        input=input.Validate();var now=Clock.Now;var day=date??DateOnly.FromDateTime(now.DateTime);
        var sort=Repository.All(true).Where(t=>t.Date==day).Select(t=>t.SortOrder).DefaultIfEmpty(-1).Max()+1;
        return Repository.Insert(new WorkItem {Date=day,Title=input.Title,Note=input.Note,PlannedStart=input.PlannedStart,PlannedEnd=input.PlannedEnd,
            CreatedAt=now,UpdatedAt=now,SortOrder=sort,Version=1});
    }

    public WorkItem Edit(string id,WorkInput input,long? expectedVersion=null)
    {
        input=input.Validate();
        return Repository.Mutate(id,"edited",t=>t with {Title=input.Title,Note=input.Note,PlannedStart=input.PlannedStart,PlannedEnd=input.PlannedEnd,UpdatedAt=Clock.Now},expectedVersion);
    }

    public WorkItem Start(string id)=>Repository.Mutate(id,"started",t=>
    {
        if(t.Status==WorkStatus.Running)return t;
        if(t.Status!=WorkStatus.Pending)throw new InvalidOperationException("已完成事项不能再次开始，请先重新打开事项。");
        var now=Clock.Now;return t with {Status=WorkStatus.Running,StartedAt=now,StartTimeZone=Clock.Zone.Id,UpdatedAt=now};
    });

    public WorkItem Complete(string id)=>Repository.Mutate(id,"completed",t=>
    {
        if(t.Status==WorkStatus.Completed)return t;
        if(t.StartedAt is null)throw new InvalidOperationException("请先点击开始，或在详情中补记真实开始时间。");
        var now=Clock.Now;if(now<t.StartedAt)throw new InvalidOperationException("系统时间早于开始时间，请检查电脑时钟，或修正实际时间。");
        return t with {Status=WorkStatus.Completed,CompletedAt=now,CompletionTimeZone=Clock.Zone.Id,UpdatedAt=now};
    });

    public WorkItem Reopen(string id,long? expectedVersion=null)=>Repository.Mutate(id,"completion_undone",t=>
    {
        if(t.Status!=WorkStatus.Completed)throw new InvalidOperationException("只有已完成事项才能撤销完成。");
        return t with {Status=WorkStatus.Running,CompletedAt=null,CompletionTimeZone="",UpdatedAt=Clock.Now};
    },expectedVersion);

    public WorkItem ResetToPending(string id)=>Repository.Mutate(id,"start_reset",t=>t with {Status=WorkStatus.Pending,StartedAt=null,CompletedAt=null,StartTimeZone="",CompletionTimeZone="",UpdatedAt=Clock.Now});

    public WorkItem CorrectTimes(string id,DateTimeOffset started,DateTimeOffset? completed,long? expectedVersion=null)
    {
        if(completed is { } end&&end<started)throw new ArgumentException("实际完成时间不能早于开始时间。");
        return Repository.Mutate(id,"times_corrected",t=>t with {Status=completed is null?WorkStatus.Running:WorkStatus.Completed,
            StartedAt=started,CompletedAt=completed,StartTimeZone=Clock.Zone.Id,CompletionTimeZone=completed is null?"":Clock.Zone.Id,UpdatedAt=Clock.Now},expectedVersion);
    }

    public WorkItem EditAndCorrectTimes(string id,WorkInput input,DateTimeOffset started,DateTimeOffset? completed,long? expectedVersion=null)
    {
        input=input.Validate();
        if(completed is { } end&&end<started)throw new ArgumentException("实际完成时间不能早于开始时间。");
        return Repository.Mutate(id,"times_corrected",t=>t with {Title=input.Title,Note=input.Note,PlannedStart=input.PlannedStart,PlannedEnd=input.PlannedEnd,
            Status=completed is null?WorkStatus.Running:WorkStatus.Completed,StartedAt=started,CompletedAt=completed,
            StartTimeZone=Clock.Zone.Id,CompletionTimeZone=completed is null?"":Clock.Zone.Id,UpdatedAt=Clock.Now},expectedVersion);
    }

    public WorkItem Delete(string id)=>Repository.Mutate(id,"deleted",t=>t with {DeletedAt=Clock.Now,UpdatedAt=Clock.Now});
    public WorkItem Restore(string id)=>Repository.Mutate(id,"restored",t=>t with {DeletedAt=null,UpdatedAt=Clock.Now},includeDeleted:true);

    public void Move(string id,int direction)
    {
        var task=Repository.Get(id);var group=Repository.All().Where(t=>t.Date==task.Date).ToList();var index=group.FindIndex(t=>t.Id==id);var next=index+Math.Sign(direction);
        if(next<0||next>=group.Count||index<0)return;
        // Reindex the same-day list, keeping stable unique values even when several items were created together.
        (group[index],group[next])=(group[next],group[index]);
        for(var i=0;i<group.Count;i++){var order=i;Repository.Mutate(group[i].Id,"reordered",t=>t.SortOrder==order?t:t with{SortOrder=order,UpdatedAt=Clock.Now});}
    }

    public IReadOnlyList<WorkItem> ForToday()
    {
        var today=Today;
        return Repository.All().Where(t=>t.Date==today || t.Date<today &&
            (t.Status!=WorkStatus.Completed || t.CompletedAt is { } completed && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(completed,Clock.Zone).DateTime)==today))
            .OrderBy(t=>t.SortOrder).ThenBy(t=>t.CreatedAt).ToList();
    }

    public IReadOnlyList<WorkItem> ForMonth(int year,int month,bool includeDeleted=false)
    {
        var start=new DateOnly(year,month,1);var end=start.AddMonths(1);
        return Repository.All(includeDeleted).Where(t=>t.Date>=start&&t.Date<end).ToList();
    }

    public void ExportMonth(int year,int month,string path)=>MonthlyExporter.Export(ForMonth(year,month),year,month,Clock.Now,path);

    public string? TryAutomaticBackup(string directory)
    {
        var settings=Repository.LoadSettings();var day=Today.ToString("yyyy-MM-dd");
        if(settings.LastAutomaticBackupDate==day||Repository.All(true).Count==0)return null;
        Directory.CreateDirectory(directory);var path=Path.Combine(directory,$"daily-{day}.db");
        Repository.BackupTo(path);Repository.SaveSettings(settings with{LastAutomaticBackupDate=day});
        foreach(var old in Directory.GetFiles(directory,"daily-????-??-??.db").OrderByDescending(Path.GetFileName,StringComparer.Ordinal).Skip(30))File.Delete(old);
        return path;
    }
}
