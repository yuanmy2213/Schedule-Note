using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using JinRiJian.Core;
using Microsoft.Data.Sqlite;

namespace JinRiJian.Tests;

internal sealed class TestClock : IClock
{
    public DateTimeOffset Now { get; set; }=new(2026,10,28,9,30,0,TimeSpan.FromHours(8));
    public TimeZoneInfo Zone { get; set; }=TimeZoneInfo.CreateCustomTimeZone("Test/CST",TimeSpan.FromHours(8),"CST","CST");
    public DateOnly Today=>DateOnly.FromDateTime(Now.DateTime);
    public void Advance(TimeSpan value)=>Now+=value;
}
internal sealed class Fixture : IDisposable
{
    public string Dir { get; }=Path.Combine(Path.GetTempPath(),"JinRiJian-tests-"+Guid.NewGuid().ToString("N"));
    public TestClock Clock { get; }=new();
    public WorkRepository Repo { get; }
    public WorkService Service { get; }
    public Fixture(){Directory.CreateDirectory(Dir);Repo=new(Path.Combine(Dir,"daily.db"));Service=new(Repo,Clock);}
    public WorkItem Add(string title="编写评审材料",DateOnly? date=null)=>Service.Add(new WorkInput(title),date);
    public void Sql(string sql){using var db=new SqliteConnection("Data Source="+Repo.DatabasePath+";Pooling=False");db.Open();using var cmd=db.CreateCommand();cmd.CommandText=sql;cmd.ExecuteNonQuery();}
    public void Dispose(){SqliteConnection.ClearAllPools();try{Directory.Delete(Dir,true);}catch(IOException){}}
}
internal static class Program
{
    private sealed record Result(string Name,bool Passed,double Milliseconds,string? Error=null);
    private static readonly List<(string Name,Action<Fixture> Run)> Cases=[];
    private static void Test(string name,Action<Fixture> run)=>Cases.Add((name,run));
    private static void Eq<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Expected {expected}; actual {actual}");}
    private static void Yes(bool value,string reason="Assertion failed"){if(!value)throw new Exception(reason);}
    private static void Throws<T>(Action run) where T:Exception{try{run();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static XDocument Xml(string path,string part){using var zip=ZipFile.OpenRead(path);using var s=zip.GetEntry(part)!.Open();return XDocument.Load(s);}
    private static readonly XNamespace Ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static int Main(string[] args)
    {
        Register();var results=new List<Result>();
        foreach(var test in Cases){var watch=Stopwatch.StartNew();try{using var f=new Fixture();test.Run(f);results.Add(new(test.Name,true,watch.Elapsed.TotalMilliseconds));Console.WriteLine("PASS "+test.Name);}catch(Exception e){results.Add(new(test.Name,false,watch.Elapsed.TotalMilliseconds,e.ToString()));Console.WriteLine("FAIL "+test.Name+": "+e.Message);}}
        var report=args.Length>0?args[0]:Path.Combine("artifacts","core-tests.json");Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report,JsonSerializer.Serialize(new{platform=Environment.OSVersion.ToString(),framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,passed=results.Count(r=>r.Passed),failed=results.Count(r=>!r.Passed),tests=results},new JsonSerializerOptions{WriteIndented=true}));
        var junit=new XDocument(new XElement("testsuite",new XAttribute("name","JinRiJian.Core"),new XAttribute("tests",results.Count),new XAttribute("failures",results.Count(r=>!r.Passed)),results.Select(r=>new XElement("testcase",new XAttribute("name",r.Name),new XAttribute("time",r.Milliseconds/1000),r.Passed?null:new XElement("failure",r.Error)))));
        junit.Save(Path.ChangeExtension(report,"xml"));Console.WriteLine($"{results.Count(r=>r.Passed)}/{results.Count} passed");return results.All(r=>r.Passed)?0:1;
    }

    private static void Register()
    {
        Test("empty-profile-defaults-to-standard",f=>{Eq(0,f.Repo.All().Count);Eq(DisplayMode.Standard,f.Repo.LoadSettings().Mode);Yes(!f.Repo.LoadSettings().AutoStart);});
        Test("title-only-null-plan-times",f=>{var t=f.Add();Eq(null,t.PlannedStart);Eq(null,t.PlannedEnd);Eq(WorkStatus.Pending,t.Status);Eq(null,t.StartedAt);});
        Test("title-trimmed-and-empty-rejected",f=>{Eq("工作",f.Service.Add(new("  工作  ")).Title);Throws<ArgumentException>(()=>f.Service.Add(new(" \t ")));});
        Test("title-length-boundary",f=>{Eq(200,f.Add(new string('中',200)).Title.Length);Throws<ArgumentException>(()=>f.Add(new string('中',201)));});
        Test("only-start-optional",f=>{var t=f.Service.Add(new("只设开始",new(9,0)));Eq(new TimeOnly(9,0),t.PlannedStart);Eq(null,t.PlannedEnd);});
        Test("only-end-optional",f=>{var t=f.Service.Add(new("只设结束",PlannedEnd:new(17,0)));Eq(null,t.PlannedStart);Eq(new TimeOnly(17,0),t.PlannedEnd);});
        Test("valid-plan-time-pair",f=>{var t=f.Service.Add(new("计划",new(9,0),new(10,0)));Yes(t.PlannedText.Contains("09:00 — 10:00"));});
        Test("equal-or-reversed-plan-rejected",f=>{Throws<ArgumentException>(()=>f.Service.Add(new("计划",new(10,0),new(10,0))));Throws<ArgumentException>(()=>f.Service.Add(new("计划",new(11,0),new(10,0))));Eq(0,f.Repo.All().Count);});
        Test("time-text-validation",_=>{Eq(null,WorkInput.ParseTime(" "));Eq(new TimeOnly(9,30),WorkInput.ParseTime("9:30"));Throws<ArgumentException>(()=>WorkInput.ParseTime("25:00"));});
        Test("start-records-actual-and-offset",f=>{var t=f.Service.Start(f.Add().Id);Eq(f.Clock.Now,t.StartedAt);Eq(TimeSpan.FromHours(8),f.Repo.Get(t.Id).StartedAt!.Value.Offset);Eq("Test/CST",t.StartTimeZone);});
        Test("duplicate-start-does-not-overwrite-or-audit-twice",f=>{var id=f.Add().Id;var first=f.Service.Start(id);f.Clock.Advance(TimeSpan.FromHours(1));var repeated=f.Service.Start(id);Eq(first.StartedAt,repeated.StartedAt);Eq(2L,repeated.Version);Eq(2,f.Repo.Events(id).Count);});
        Test("complete-records-time-and-duration",f=>{var id=f.Add().Id;f.Service.Start(id);f.Clock.Advance(TimeSpan.FromMinutes(35));var done=f.Service.Complete(id);Eq(35d,done.ElapsedMinutes);Eq(f.Clock.Now,done.CompletedAt);Eq(WorkStatus.Completed,done.Status);});
        Test("duplicate-completion-does-not-overwrite",f=>{var id=f.Add().Id;f.Service.Start(id);f.Clock.Advance(TimeSpan.FromMinutes(1));var first=f.Service.Complete(id);f.Clock.Advance(TimeSpan.FromMinutes(2));Eq(first.CompletedAt,f.Service.Complete(id).CompletedAt);Eq(3,f.Repo.Events(id).Count);});
        Test("pending-cannot-complete-with-fabricated-start",f=>{var id=f.Add().Id;Throws<InvalidOperationException>(()=>f.Service.Complete(id));Eq(WorkStatus.Pending,f.Repo.Get(id).Status);Eq(null,f.Repo.Get(id).StartedAt);});
        Test("completed-cannot-start-without-reopen",f=>{var id=f.Add().Id;f.Service.Start(id);f.Service.Complete(id);Throws<InvalidOperationException>(()=>f.Service.Start(id));});
        Test("clock-rollback-rejects-negative-duration",f=>{var id=f.Add().Id;var start=f.Service.Start(id);f.Clock.Advance(TimeSpan.FromMinutes(-10));Throws<InvalidOperationException>(()=>f.Service.Complete(id));Eq(start.StartedAt,f.Repo.Get(id).StartedAt);Eq(WorkStatus.Running,f.Repo.Get(id).Status);});
        Test("metadata-edit-preserves-actual-times",f=>{var id=f.Add().Id;var start=f.Service.Start(id).StartedAt;f.Clock.Advance(TimeSpan.FromMinutes(2));var end=f.Service.Complete(id).CompletedAt;var t=f.Service.Edit(id,new("新标题",new(8,0),new(9,0),"备注"));Eq(start,t.StartedAt);Eq(end,t.CompletedAt);});
        Test("stale-editor-version-is-rejected",f=>{var item=f.Add();f.Service.Start(item.Id);Throws<ConcurrentEditException>(()=>f.Service.Edit(item.Id,new("过期修改"),item.Version));Eq(item.Title,f.Repo.Get(item.Id).Title);});
        Test("undo-complete-retains-start",f=>{var id=f.Add().Id;var start=f.Service.Start(id).StartedAt;f.Service.Complete(id);var reopened=f.Service.Reopen(id);Eq(start,reopened.StartedAt);Eq(null,reopened.CompletedAt);Eq(WorkStatus.Running,reopened.Status);});
        Test("undo-requires-completed-status",f=>{var id=f.Add().Id;Throws<InvalidOperationException>(()=>f.Service.Reopen(id));});
        Test("reset-start-to-pending-clears-times",f=>{var id=f.Add().Id;f.Service.Start(id);var t=f.Service.ResetToPending(id);Eq(WorkStatus.Pending,t.Status);Eq(null,t.StartedAt);Eq(null,t.CompletedAt);});
        Test("manual-completion-records-audit",f=>{var id=f.Add().Id;var t=f.Service.CorrectTimes(id,f.Clock.Now.AddHours(-1),f.Clock.Now);Eq(WorkStatus.Completed,t.Status);Eq(60d,t.ElapsedMinutes);Eq("times_corrected",f.Repo.Events(id).Last().Type);});
        Test("manual-running-time-can-omit-end",f=>{var id=f.Add().Id;var t=f.Service.CorrectTimes(id,f.Clock.Now.AddHours(-1),null);Eq(WorkStatus.Running,t.Status);Eq(null,t.CompletedAt);});
        Test("manual-negative-time-rejected-atomically",f=>{var id=f.Add().Id;Throws<ArgumentException>(()=>f.Service.CorrectTimes(id,f.Clock.Now,f.Clock.Now.AddSeconds(-1)));Eq(WorkStatus.Pending,f.Repo.Get(id).Status);Eq(1,f.Repo.Events(id).Count);});
        Test("metadata-and-time-correction-are-one-transaction",f=>{var t=f.Add("原标题");f.Sql("CREATE TRIGGER fail_correct BEFORE INSERT ON task_events WHEN NEW.event_type='times_corrected' BEGIN SELECT RAISE(ABORT,'test-correction-failure'); END;");Throws<SqliteException>(()=>f.Service.EditAndCorrectTimes(t.Id,new("新标题"),f.Clock.Now.AddMinutes(-5),f.Clock.Now,t.Version));Eq("原标题",f.Repo.Get(t.Id).Title);Eq(WorkStatus.Pending,f.Repo.Get(t.Id).Status);});
        Test("task-persists-after-reopen",f=>{var t=f.Service.Start(f.Add().Id);var reopened=new WorkRepository(f.Repo.DatabasePath);Eq(t.StartedAt,reopened.Get(t.Id).StartedAt);Eq(WorkStatus.Running,reopened.Get(t.Id).Status);});
        Test("carryover-keeps-original-id-and-date",f=>{var t=f.Add(date:f.Clock.Today.AddDays(-1));Eq(t.Id,f.Service.ForToday().Single().Id);Eq(t.Date,f.Repo.Get(t.Id).Date);Eq(1,f.Repo.All().Count);});
        Test("future-task-not-shown-today",f=>{f.Add(date:f.Clock.Today.AddDays(1));Eq(0,f.Service.ForToday().Count);});
        Test("old-completed-task-not-carried",f=>{var t=f.Add(date:f.Clock.Today.AddDays(-1));f.Service.CorrectTimes(t.Id,f.Clock.Now.AddDays(-1),f.Clock.Now.AddDays(-1).AddMinutes(5));Eq(0,f.Service.ForToday().Count);});
        Test("carryover-completed-today-remains-visible-today",f=>{var t=f.Add(date:f.Clock.Today.AddDays(-1));f.Service.Start(t.Id);f.Service.Complete(t.Id);Eq(t.Id,f.Service.ForToday().Single().Id);});
        Test("month-boundary-finish-retains-original-month",f=>{var t=f.Add(date:new(2026,9,30));f.Service.CorrectTimes(t.Id,new(2026,9,30,23,0,0,TimeSpan.FromHours(8)),new(2026,10,1,1,0,0,TimeSpan.FromHours(8)));Eq(t.Id,f.Service.ForMonth(2026,9).Single().Id);Eq(0,f.Service.ForMonth(2026,10).Count);});
        Test("actual-offset-survives-timezone-change",f=>{var t=f.Service.Start(f.Add().Id);f.Clock.Now=f.Clock.Now.ToOffset(TimeSpan.FromHours(-4)).AddHours(1);var end=f.Service.Complete(t.Id);var loaded=f.Repo.Get(t.Id);Eq(TimeSpan.FromHours(8),loaded.StartedAt!.Value.Offset);Eq(TimeSpan.FromHours(-4),loaded.CompletedAt!.Value.Offset);Eq(60d,end.ElapsedMinutes);});
        Test("soft-delete-and-restore",f=>{var t=f.Add();f.Service.Delete(t.Id);Eq(0,f.Repo.All().Count);Eq(1,f.Repo.All(true).Count);Throws<KeyNotFoundException>(()=>f.Repo.Get(t.Id));var restored=f.Service.Restore(t.Id);Eq(t.Id,restored.Id);Eq(null,restored.DeletedAt);});
        Test("deleted-task-excluded-from-export-month",f=>{var t=f.Add();f.Service.Delete(t.Id);Eq(0,f.Service.ForMonth(2026,10).Count);Eq(1,f.Service.ForMonth(2026,10,true).Count);});
        Test("parallel-start-is-idempotent",f=>{var id=f.Add().Id;Parallel.For(0,25,_=>f.Service.Start(id));Eq(2L,f.Repo.Get(id).Version);Eq(2,f.Repo.Events(id).Count);});
        Test("parallel-complete-is-idempotent",f=>{var id=f.Add().Id;f.Service.Start(id);Parallel.For(0,25,_=>f.Service.Complete(id));Eq(3L,f.Repo.Get(id).Version);Eq(3,f.Repo.Events(id).Count);});
        Test("audit-failure-rolls-back-state-and-time",f=>{var id=f.Add().Id;f.Sql("CREATE TRIGGER fail_audit BEFORE INSERT ON task_events WHEN NEW.event_type='started' BEGIN SELECT RAISE(ABORT,'test-audit-failure'); END;");Throws<SqliteException>(()=>f.Service.Start(id));Eq(WorkStatus.Pending,f.Repo.Get(id).Status);Eq(null,f.Repo.Get(id).StartedAt);Eq(1,f.Repo.Events(id).Count);});
        Test("duplicate-id-does-not-create-event",f=>{var t=f.Add();Throws<SqliteException>(()=>f.Repo.Insert(t));Eq(1,f.Repo.Events(t.Id).Count);});
        Test("sql-injection-title-is-data",f=>{var title="'); DROP TABLE tasks; --";var t=f.Add(title);Eq(title,f.Repo.Get(t.Id).Title);f.Add("第二项");Eq(2,f.Repo.All().Count);});
        Test("move-up-down-and-boundaries",f=>{var a=f.Add("A");var b=f.Add("B");f.Service.Move(a.Id,-1);Eq(a.Id,f.Repo.All().First().Id);f.Service.Move(b.Id,-1);Eq(b.Id,f.Repo.All().First().Id);f.Service.Move(b.Id,1);Eq(a.Id,f.Repo.All().First().Id);});
        Test("backup-live-wal-is-consistent",f=>{var t=f.Service.Start(f.Add().Id);var p=Path.Combine(f.Dir,"backup.db");f.Repo.BackupTo(p);WorkRepository.ValidateBackup(p);var backed=new WorkRepository(p);Eq(t.StartedAt,backed.Get(t.Id).StartedAt);Eq(2,backed.Events(t.Id).Count);});
        Test("restore-creates-safety-backup",f=>{var first=f.Add("旧数据");var p=Path.Combine(f.Dir,"old.db");f.Repo.BackupTo(p);var second=f.Add("新数据");var safety=f.Repo.RestoreFrom(p,Path.Combine(f.Dir,"backups"));Eq(1,f.Repo.All().Count);Eq(first.Id,f.Repo.All().Single().Id);Eq(2,new WorkRepository(safety).All().Count);Yes(new WorkRepository(safety).Get(second.Id) is not null);});
        Test("invalid-restore-does-not-modify-current",f=>{var t=f.Add();var p=Path.Combine(f.Dir,"bad.db");File.WriteAllText(p,"not sqlite");Throws<SqliteException>(()=>f.Repo.RestoreFrom(p,Path.Combine(f.Dir,"backups")));Eq(t.Id,f.Repo.All().Single().Id);});
        Test("foreign-sqlite-backup-is-rejected",f=>{var p=Path.Combine(f.Dir,"foreign.db");using(var db=new SqliteConnection("Data Source="+p)){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="CREATE TABLE unrelated(id)";cmd.ExecuteNonQuery();}Throws<InvalidDataException>(()=>f.Repo.RestoreFrom(p,Path.Combine(f.Dir,"backups")));});
        Test("backup-cannot-overwrite-live-database",f=>{f.Add();Throws<ArgumentException>(()=>f.Repo.BackupTo(f.Repo.DatabasePath));Eq(1,f.Repo.All().Count);});
        Test("automatic-backup-once-per-date",f=>{f.Add();var dir=Path.Combine(f.Dir,"backups");Yes(f.Service.TryAutomaticBackup(dir) is not null);Eq(null,f.Service.TryAutomaticBackup(dir));f.Clock.Advance(TimeSpan.FromDays(1));Yes(f.Service.TryAutomaticBackup(dir) is not null);Eq(2,Directory.GetFiles(dir,"daily-*.db").Length);});
        Test("automatic-backup-retention-preserves-manual-files",f=>{f.Add();var dir=Path.Combine(f.Dir,"backups");Directory.CreateDirectory(dir);for(var i=0;i<35;i++)File.WriteAllText(Path.Combine(dir,$"daily-{f.Clock.Today.AddDays(-i-1):yyyy-MM-dd}.db"),"old");var manual=Path.Combine(dir,"manual.db");File.WriteAllText(manual,"keep");f.Service.TryAutomaticBackup(dir);Eq(30,Directory.GetFiles(dir,"daily-*.db").Length);Yes(File.Exists(manual));});
        Test("empty-profile-does-not-create-empty-daily-backup",f=>{Eq(null,f.Service.TryAutomaticBackup(Path.Combine(f.Dir,"backups")));});
        Test("mode-and-two-layouts-persist-independently",f=>{var settings=new UserSettings{Mode=DisplayMode.Compact,StandardBounds=new(100,100,430,590),CompactBounds=new(200,200,300,280),Topmost=true};f.Repo.SaveSettings(settings);var loaded=new WorkRepository(f.Repo.DatabasePath).LoadSettings();Eq(settings,loaded);Eq(0,f.Repo.All().Count);});
        Test("corrupt-preferences-fallback-to-standard",f=>{f.Sql("INSERT INTO settings(key,value) VALUES('user','{bad')");Eq(DisplayMode.Standard,f.Repo.LoadSettings().Mode);});
        Test("future-schema-is-not-silently-downgraded",f=>{f.Sql("PRAGMA user_version=99");Throws<InvalidDataException>(()=>new WorkRepository(f.Repo.DatabasePath));});
        Test("valid-json-invalid-preferences-fallback-safely",f=>{f.Sql("INSERT INTO settings(key,value) VALUES('user','{\"Mode\":999,\"Theme\":\"unknown\",\"CompactBounds\":{\"Left\":0,\"Top\":0,\"Width\":1,\"Height\":1}}')");var p=f.Repo.LoadSettings();Eq(DisplayMode.Standard,p.Mode);Eq("system",p.Theme);Eq(null,p.CompactBounds);});
        Test("opening-unmarked-existing-database-is-rejected",f=>{var p=Path.Combine(f.Dir,"unmarked.db");using(var db=new SqliteConnection("Data Source="+p+";Pooling=False")){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="CREATE TABLE valuable(value TEXT);INSERT INTO valuable VALUES('keep')";cmd.ExecuteNonQuery();}Throws<InvalidDataException>(()=>new WorkRepository(p));});
        Test("incomplete-backup-schema-does-not-replace-current",f=>{var t=f.Add();var p=Path.Combine(f.Dir,"incomplete.db");f.Repo.BackupTo(p);using(var db=new SqliteConnection("Data Source="+p+";Pooling=False")){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="DROP TABLE task_events";cmd.ExecuteNonQuery();}Throws<SqliteException>(()=>f.Repo.RestoreFrom(p,Path.Combine(f.Dir,"backups")));Eq(t.Id,f.Repo.All().Single().Id);});
        Test("xlsx-is-valid-zip-with-two-named-sheets",f=>{f.Add();var p=Path.Combine(f.Dir,"report.xlsx");f.Service.ExportMonth(2026,10,p);var w=Xml(p,"xl/workbook.xml");Eq("事项明细",w.Descendants(Ns+"sheet").First().Attribute("name")!.Value);Eq(2,w.Descendants(Ns+"sheet").Count());using var z=ZipFile.OpenRead(p);foreach(var e in z.Entries.Where(e=>e.FullName.EndsWith(".xml")||e.FullName.EndsWith(".rels"))){using var s=e.Open();XDocument.Load(s);}});
        Test("xlsx-empty-month-keeps-headers-and-empty-label",f=>{var p=Path.Combine(f.Dir,"empty.xlsx");f.Service.ExportMonth(2026,10,p);var x=Xml(p,"xl/worksheets/sheet1.xml");Yes(x.Descendants(Ns+"t").Any(t=>t.Value=="事项名称"));Yes(x.Descendants(Ns+"t").Any(t=>t.Value=="本月无事项"));});
        Test("xlsx-blank-planned-times-remain-empty",f=>{f.Add();var p=Path.Combine(f.Dir,"blank.xlsx");f.Service.ExportMonth(2026,10,p);var x=Xml(p,"xl/worksheets/sheet1.xml");Yes(!x.Descendants(Ns+"c").Any(c=>c.Attribute("r")?.Value is "D2" or "E2" or "F2" or "G2"));});
        Test("xlsx-formula-text-never-becomes-formula",f=>{f.Add("=HYPERLINK(\"https://example.invalid\")");var p=Path.Combine(f.Dir,"formula.xlsx");f.Service.ExportMonth(2026,10,p);var x=Xml(p,"xl/worksheets/sheet1.xml");Eq(0,x.Descendants(Ns+"f").Count());Yes(x.Descendants(Ns+"c").Single(c=>c.Attribute("r")?.Value=="B2").Attribute("t")?.Value=="inlineStr");});
        Test("xlsx-chinese-emoji-and-xml-characters-preserved",f=>{var title="中文事项 😀 & <评审>";f.Service.Add(new(title,Note:"备注\n第二行 & < >"));var p=Path.Combine(f.Dir,"unicode.xlsx");f.Service.ExportMonth(2026,10,p);Yes(Xml(p,"xl/worksheets/sheet1.xml").Descendants(Ns+"t").Any(t=>t.Value==title),"Unicode text changed");});
        Test("xlsx-invalid-control-character-sanitized",f=>{f.Add("文字\0控制符");var p=Path.Combine(f.Dir,"control.xlsx");f.Service.ExportMonth(2026,10,p);Yes(Xml(p,"xl/worksheets/sheet1.xml").Descendants(Ns+"t").Any(t=>t.Value=="文字控制符"));});
        Test("xlsx-completed-duration-and-daily-ratio",f=>{var t=f.Add();f.Service.Start(t.Id);f.Clock.Advance(TimeSpan.FromMinutes(30));f.Service.Complete(t.Id);f.Add("未开始");var p=Path.Combine(f.Dir,"summary.xlsx");f.Service.ExportMonth(2026,10,p);var d=Xml(p,"xl/worksheets/sheet1.xml");Eq("30",d.Descendants(Ns+"c").Single(c=>c.Attribute("r")?.Value=="J2").Element(Ns+"v")!.Value);var s=Xml(p,"xl/worksheets/sheet2.xml");Eq("0.5",s.Descendants(Ns+"c").Single(c=>c.Attribute("r")?.Value=="F2").Element(Ns+"v")!.Value);});
        Test("xlsx-first-row-frozen-and-text-wrapped",f=>{f.Add();var p=Path.Combine(f.Dir,"style.xlsx");f.Service.ExportMonth(2026,10,p);Eq("frozen",Xml(p,"xl/worksheets/sheet1.xml").Descendants(Ns+"pane").Single().Attribute("state")!.Value);Yes(Xml(p,"xl/styles.xml").Descendants(Ns+"alignment").Any(a=>a.Attribute("wrapText")?.Value=="1"));});
        Test("xlsx-atomic-replacement-and-no-temp-leftovers",f=>{f.Add();var p=Path.Combine(f.Dir,"atomic.xlsx");File.WriteAllText(p,"old");f.Service.ExportMonth(2026,10,p);using var z=ZipFile.OpenRead(p);Yes(z.Entries.Count>0);Eq(0,Directory.GetFiles(f.Dir,"*.tmp").Length);});
        Test("month-query-supports-leap-day-and-year-boundary",f=>{f.Add("闰日",new(2024,2,29));f.Add("年末",new(2025,12,31));f.Add("年初",new(2026,1,1));Eq("闰日",f.Service.ForMonth(2024,2).Single().Title);Eq("年末",f.Service.ForMonth(2025,12).Single().Title);Eq("年初",f.Service.ForMonth(2026,1).Single().Title);});
        Test("long-note-boundary",f=>{f.Service.Add(new("长备注",Note:new string('a',10000)));Throws<ArgumentException>(()=>f.Service.Add(new("过长",Note:new string('a',10001))));});
        Test("fixture-export-for-independent-parser",f=>{var t=f.Service.Add(new("独立读取校验 😀",PlannedEnd:new(17,0),Note:"第一行\n第二行"));f.Service.Start(t.Id);f.Clock.Advance(TimeSpan.FromMinutes(42));f.Service.Complete(t.Id);f.Add("待开始");var dir=Path.GetFullPath("artifacts");Directory.CreateDirectory(dir);f.Service.ExportMonth(2026,10,Path.Combine(dir,"verification.xlsx"));});
    }
}
