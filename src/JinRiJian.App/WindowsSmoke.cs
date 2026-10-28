using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JinRiJian.App;

internal sealed class WindowsSmokeClock : IClock
{
    public DateTimeOffset Now { get; set; }=DateTimeOffset.Now;
    public TimeZoneInfo Zone => TimeZoneInfo.Local;
}

internal static class WindowsSmoke
{
    private sealed record Result(string Name,bool Passed,string? Error=null);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]private static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr hwnd);

    public static async Task<int> Run(NoteWindow note,string report)
    {
        var rows=new List<Result>();var stopwatch=Stopwatch.StartNew();
        async Task Check(string name,Func<Task> run){try{await run();rows.Add(new(name,true));}catch(Exception e){rows.Add(new(name,false,e.ToString()));}}
        Task Sync(Action action){action();return Task.CompletedTask;}
        static void Require(bool condition,string text){if(!condition)throw new InvalidOperationException(text);}
        async Task Settle(){await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);await Task.Delay(120);}
        WorkItem? task=null;DateTimeOffset? started=null;
        await Check("default-standard-original-layout",()=>Sync(()=>{Require(note.Mode==DisplayMode.Standard,"Fresh profile must use Standard mode");Require(Texts(note).Any(t=>t.Text=="▤  今日笺"),"Original brand missing");Require(Texts(note).Any(t=>t.FontSize==22),"Original date size missing");Require(Find(note,"OpenPlan") is not null&&Find(note,"OpenHistory") is not null,"Original navigation missing");}));
        await Check("native-visible-resizable-window",()=>Sync(()=>{var hwnd=new WindowInteropHelper(note).Handle;Require(hwnd!=IntPtr.Zero&&IsWindowVisible(hwnd),"Window HWND not visible");Require((GetWindowLong(hwnd,-16)&0x40000)!=0,"Native resize frame missing");}));
        await Check("tray-created",()=>Sync(()=>Require(note.TrayVisible,"Tray icon not enabled")));
        await Check("quick-add-through-native-control",async()=>{note.QuickInput.Text="准备方案评审材料";Click(note,"QuickAdd");await Settle();task=note.Service.ForToday().Single();Require(task.Title=="准备方案评审材料"&&task.PlannedStart is null&&task.PlannedEnd is null,"Quick entry did not persist nullable plan times");});
        await Check("start-through-native-control",async()=>{Click(note,"Start-"+task!.Id);await Settle();var saved=note.Service.Repository.Get(task.Id);started=saved.StartedAt;Require(saved.Status==WorkStatus.Running&&started is not null,"Start did not persist");});
        await Check("standard-resize-and-save",async()=>{note.Width=430;note.Height=590;await Settle();note.SaveGeometry();Require(Math.Abs(note.Service.Repository.LoadSettings().StandardBounds!.Width-430)<2,"Standard width not persisted");});
        await Check("switch-compact-native-button-preserves-time",async()=>{note.QuickInput.Text="尚未提交的草稿";Click(note,"SwitchCompact");await Settle();Require(note.Mode==DisplayMode.Compact,"Mode did not switch");Require(note.Service.Repository.Get(task!.Id).StartedAt==started,"Mode switch reset start time");Require(note.QuickInput.Text=="尚未提交的草稿","Quick-entry draft lost");});
        await Check("compact-independent-size",async()=>{note.Width=300;note.Height=280;await Settle();note.SaveGeometry();Require(Math.Abs(note.Service.Repository.LoadSettings().CompactBounds!.Width-300)<2,"Compact width not persisted");});
        await Check("complete-in-compact",async()=>{Click(note,"Complete-"+task!.Id);await Settle();Require(note.Service.Repository.Get(task.Id).Status==WorkStatus.Completed,"Completion missing");});
        await Check("switch-standard-restores-size-and-data",async()=>{note.ChangeMode(DisplayMode.Standard);await Settle();Require(Math.Abs(note.Width-430)<2&&Math.Abs(note.Height-590)<2,"Standard bounds not restored");Require(note.Service.Repository.Get(task!.Id).Status==WorkStatus.Completed,"Shared completion state lost");});
        await Check("undo-completion-through-native-control",async()=>{Click(note,"UndoCompletion");await Settle();var saved=note.Service.Repository.Get(task!.Id);Require(saved.Status==WorkStatus.Running&&saved.StartedAt==started&&saved.CompletedAt is null,"Undo failed");});
        await Check("compact-bounds-restored",async()=>{note.ChangeMode(DisplayMode.Compact);await Settle();Require(Math.Abs(note.Width-300)<2&&Math.Abs(note.Height-280)<2,"Compact bounds not restored");note.ChangeMode(DisplayMode.Standard);await Settle();});
        await Check("native-fold-and-unfold",async()=>{Click(note,"Fold");await Settle();Require(note.Height<=50,"Fold did not shrink window");Click(note,"Fold");await Settle();Require(note.Height>=360,"Unfold did not restore window");});
        await Check("management-plan-history-repeated-navigation",async()=>{note.ShowManagement("plan");await Settle();var w=Application.Current.Windows.OfType<ManagementWindow>().Single();for(var i=0;i<4;i++){w.ShowPage("history");w.UpdateLayout();await Settle();Require(Texts(w).Any(t=>t.Text=="做过的事，都有记录。"),"Original history page missing");w.ShowPage("plan");w.UpdateLayout();await Settle();Require(Texts(w).Any(t=>t.Text=="今天，把重要的事做好。"),"Original plan page missing");}Capture(w,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"plan-native.png"));w.ShowPage("history");w.UpdateLayout();await Settle();Capture(w,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"history-native.png"));w.Close();});
        await Check("plan-native-entry-allows-end-only",async()=>{note.ShowManagement("plan");await Settle();var w=Application.Current.Windows.OfType<ManagementWindow>().Single();Descendants(w).OfType<TextBox>().Single(t=>AutomationProperties.GetAutomationId(t)=="PlanTitle").Text="仅设结束时间";Descendants(w).OfType<TextBox>().Single(t=>AutomationProperties.GetAutomationId(t)=="PlanEnd").Text="17:30";Click(w,"PlanSave");await Settle();var added=note.Service.Repository.All().Single(t=>t.Title=="仅设结束时间");Require(added.PlannedStart is null&&added.PlannedEnd==new TimeOnly(17,30),"Plan UI did not preserve nullable start");w.Close();note.Service.Delete(added.Id);note.Refresh();});
        await Check("settings-and-editor-open-without-layout-errors",async()=>{var editor=new EditWindow(note,note.Service.Repository.Get(task!.Id),true);editor.Show();await Settle();editor.Close();var settings=new SettingsWindow(note);settings.Show();await Settle();settings.Close();});
        await Check("native-topmost-toggle",async()=>{Click(note,"Pin");await Settle();Require(note.Topmost,"Topmost was not enabled");Click(note,"Pin");Require(!note.Topmost,"Topmost was not disabled");});
        await Check("hide-and-reveal-does-not-complete",async()=>{note.Hide();await Settle();Require(!note.IsVisible&&note.TrayVisible,"Hidden state invalid");note.Reveal();await Settle();Require(note.IsVisible&&note.Service.Repository.Get(task!.Id).Status==WorkStatus.Running,"Reveal changed task state");});
        await Check("second-process-signals-existing-instance",async()=>{note.Hide();using var child=new Process{StartInfo=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false}};child.StartInfo.ArgumentList.Add("--data-dir");child.StartInfo.ArgumentList.Add(note.DataDirectory.ToUpperInvariant());child.Start();await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));await Settle();Require(child.ExitCode==0&&note.IsVisible,"Second process did not reveal the existing instance");});
        await Check("smoke-mode-refuses-existing-user-data",async()=>{var folder=Path.Combine(note.DataDirectory,"protected-profile");Directory.CreateDirectory(folder);var file=Path.Combine(folder,"daily.db");note.Service.Repository.BackupTo(file);var before=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file));using var child=new Process{StartInfo=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false}};child.StartInfo.ArgumentList.Add("--smoke-test");child.StartInfo.ArgumentList.Add("--data-dir");child.StartInfo.ArgumentList.Add(folder);child.Start();await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Require(child.ExitCode!=0&&before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))),"Smoke mode touched an existing database");});
        await Check("export-produced-from-native-session",()=>Sync(()=>{var file=Path.Combine(note.DataDirectory,"native-session.xlsx");note.Service.ExportMonth(note.Service.Today.Year,note.Service.Today.Month,file);Require(File.Exists(file)&&new FileInfo(file).Length>1000,"XLSX was not written");}));
        await Check("native-backup-and-reopen-persistence",()=>Sync(()=>{var file=Path.Combine(note.DataDirectory,"smoke-backup.db");note.Service.Repository.BackupTo(file);var reopened=new WorkRepository(file);Require(reopened.Get(task!.Id).StartedAt==started,"SQLite backup did not retain timestamps");}));
        await Check("screenshots-standard-and-compact",async()=>{var folder=Path.GetDirectoryName(Path.GetFullPath(report))!;Directory.CreateDirectory(folder);note.ChangeMode(DisplayMode.Standard);note.Width=390;note.Height=565;note.QuickInput.Clear();note.Service.Add(new WorkInput("整理沟通纪要",new TimeOnly(14,0),new TimeOnly(15,0)));note.Service.Add(new WorkInput("更新工作清单"));note.Refresh();await Settle();Capture(note,Path.Combine(folder,"standard-native.png"));note.ChangeMode(DisplayMode.Compact);note.Width=340;note.Height=360;await Settle();Capture(note,Path.Combine(folder,"compact-native.png"));note.ChangeMode(DisplayMode.Standard);});
        await Check("window-layout-260px-wide",async()=>{note.Width=260;note.Height=360;await Settle();Require(note.ActualWidth>=259&&note.QuickInput.ActualWidth>80,"Narrow window layout broken");note.Width=390;note.Height=565;});
        await Check("reopen-user-settings",()=>Sync(()=>{note.SaveGeometry();var repo=new WorkRepository(note.Service.Repository.DatabasePath);Require(repo.LoadSettings().Mode==note.Mode,"Display mode not persisted");Require(repo.LoadSettings().StandardBounds is not null&&repo.LoadSettings().CompactBounds is not null,"Both layouts not persisted");}));
        await Check("header-settings-menu-remains-client-hit-testable",()=>Sync(()=>{var brand=Texts(note).Single(t=>AutomationProperties.GetAutomationId(t)=="HeaderBrand");Require(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(brand),"Caption swallowed the settings context menu");Require(brand.ContextMenu.Items.OfType<MenuItem>().Any(m=>m.Header?.ToString()?.Contains("窗口设置")==true),"Settings context menu missing");}));
        await Check("midnight-refresh-updates-both-mode-dates-and-plan",async()=>{var clock=(WindowsSmokeClock)note.Service.Clock;var original=clock.Now;try{note.ChangeMode(DisplayMode.Standard);note.ShowManagement("plan");await Settle();clock.Now=original.AddDays(1);note.CheckDayChange();await Settle();Require(Texts(note).Any(t=>t.Text==Ui.DateText(note.Service.Today)),"Standard date did not advance");var manager=Application.Current.Windows.OfType<ManagementWindow>().Single();Require(Texts(manager).Any(t=>t.Text.Contains(note.Service.Today.ToString("yyyy年MM月dd日"))),"Plan date did not advance");manager.Close();clock.Now=original;note.CheckDayChange();note.ChangeMode(DisplayMode.Compact);await Settle();clock.Now=original.AddDays(1);note.CheckDayChange();await Settle();Require(Texts(note).Any(t=>t.Text==Ui.DateText(note.Service.Today)+"  "+Ui.Weekday(note.Service.Today)),"Compact date did not advance");Require(note.Service.ForToday().Any(t=>t.Id==task!.Id),"Midnight rollover lost the running task");}finally{clock.Now=original;note.CheckDayChange();note.ChangeMode(DisplayMode.Standard);}});
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{platform=Environment.OSVersion.ToString(),framework=RuntimeInformation.FrameworkDescription,elapsedSeconds=stopwatch.Elapsed.TotalSeconds,passed=rows.Count(r=>r.Passed),failed=rows.Count(r=>!r.Passed),tests=rows},new JsonSerializerOptions{WriteIndented=true}));
        return rows.All(r=>r.Passed)?0:1;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in Descendants(VisualTreeHelper.GetChild(root,i)))yield return item;
    }
    private static IEnumerable<TextBlock> Texts(DependencyObject root)=>Descendants(root).OfType<TextBlock>();
    private static Button? Find(DependencyObject root,string id)=>Descendants(root).OfType<Button>().FirstOrDefault(b=>AutomationProperties.GetAutomationId(b)==id);
    private static void Click(DependencyObject root,string id){var button=Find(root,id)??throw new InvalidOperationException("Button not found: "+id);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    private static void Capture(System.Windows.Window window,string path){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);window.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(path);encoder.Save(stream);}
}
