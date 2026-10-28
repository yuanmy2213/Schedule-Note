using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace JinRiJian.App;

public sealed class NoteWindow : System.Windows.Window,IDisposable
{
    public WorkService Service { get; }
    public string DataDirectory { get; }
    public bool Exiting { get; private set; }
    public DisplayMode Mode { get; private set; }
    internal bool Testing { get; }
    internal bool TrayVisible => tray.Visible;
    internal TextBox QuickInput { get; private set; }=null!;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer geometryTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly DispatcherTimer dayTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    private readonly DispatcherTimer undoTimer=new(){Interval=TimeSpan.FromSeconds(10)};
    private StackPanel list=null!;
    private Button done=null!,pin=null!;
    private TextBlock count=null!,carry=null!;
    private TextBlock? dateLabel,weekLabel;
    private Button undoButton=null!;
    private bool expandedDone,restoring,folded;
    private double expandedHeight;
    private string draft="";
    private string? undoId;
    private long? undoVersion;
    private DateOnly shownDate;
    private ManagementWindow? management;
    public event EventHandler? DataChanged;

    public NoteWindow(WorkService service,string directory,bool testing=false)
    {
        Service=service;DataDirectory=directory;Testing=testing;
        Ui.BaseWindow(this,"今日笺",390,565);Ui.Chrome(this);ShowInTaskbar=false;
        var prefs=Service.Repository.LoadSettings();Mode=prefs.Mode;Topmost=prefs.Topmost;
        tray=new Forms.NotifyIcon{Text="今日笺 · 每日办公事项",Visible=true};
        var iconStream=Assembly.GetExecutingAssembly().GetManifestResourceStream("JinRiJian.App.Assets.app.ico");
        tray.Icon=iconStream is null?System.Drawing.SystemIcons.Application:new System.Drawing.Icon(iconStream);
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(Reveal);
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏便利贴",null,(_,_)=>Dispatcher.Invoke(()=>{if(IsVisible)Hide();else Reveal();}));
        menu.Items.Add("标准模式",null,(_,_)=>Dispatcher.Invoke(()=>{ChangeMode(DisplayMode.Standard);Reveal();}));
        menu.Items.Add("精简模式",null,(_,_)=>Dispatcher.Invoke(()=>{ChangeMode(DisplayMode.Compact);Reveal();}));
        menu.Items.Add("历史与月度导出",null,(_,_)=>Dispatcher.Invoke(()=>ShowManagement("history")));
        menu.Items.Add("窗口设置",null,(_,_)=>Dispatcher.Invoke(ShowSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出",null,(_,_)=>Dispatcher.Invoke(Exit));tray.ContextMenuStrip=menu;
        Build();RestoreGeometry();
        geometryTimer.Tick+=(_,_)=>{geometryTimer.Stop();SaveGeometry();};
        LocationChanged+=(_,_)=>QueueGeometry();SizeChanged+=(_,_)=>QueueGeometry();
        dayTimer.Tick+=(_,_)=>CheckDayChange();dayTimer.Start();
        undoTimer.Tick+=(_,_)=>{undoTimer.Stop();undoId=null;undoVersion=null;undoButton.Visibility=Visibility.Collapsed;};
        Closing+=OnClosing;Loaded+=(_,_)=>TryBackup();
    }

    private void Build()
    {
        if(QuickInput is not null)draft=QuickInput.Text;
        Background=Ui.Paper;MinWidth=260;MinHeight=Mode==DisplayMode.Standard?360:220;
        var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var header=Ui.Columns();header.Height=48;header.Margin=new Thickness(13,0,9,0);
        var brand=Mode==DisplayMode.Standard?Ui.Label("▤  今日笺",14,true):Ui.Label(Ui.DateText(Service.Today)+"  "+Ui.Weekday(Service.Today),14,true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(brand,"HeaderBrand");
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(brand,true);brand.ContextMenu=CreateMenu();brand.MouseLeftButtonDown+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed){e.Handled=true;DragMove();}};
        dateLabel=Mode==DisplayMode.Compact?brand:null;weekLabel=null;
        Ui.At(header,brand);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        pin=Ui.Button(Topmost?"已置顶":"置顶",()=>{Topmost=!Topmost;pin.Content=Topmost?"已置顶":"置顶";SaveGeometry();},quiet:true,automationId:"Pin");pin.FontSize=12;pin.Padding=new Thickness(6,3,6,3);pin.MinHeight=30;actions.Children.Add(pin);
        if(Mode==DisplayMode.Standard)
        {
            var compact=Ui.Button("精简",()=>ChangeMode(DisplayMode.Compact),quiet:true,automationId:"SwitchCompact");compact.FontSize=12;compact.Padding=new Thickness(6,3,6,3);compact.MinHeight=30;actions.Children.Add(compact);
            var fold=Ui.Button(folded?"＋":"—",ToggleFold,quiet:true,automationId:"Fold");fold.MinWidth=26;fold.Padding=new Thickness(5,3,5,3);fold.MinHeight=30;actions.Children.Add(fold);
        }
        else
        {
            var more=Ui.Button("⋯",()=>{},quiet:true,automationId:"More");more.FontSize=20;more.Padding=new Thickness(5,0,5,0);more.MinHeight=30;more.Click+=(_,_)=>{more.ContextMenu=CreateMenu();more.ContextMenu.PlacementTarget=more;more.ContextMenu.IsOpen=true;};actions.Children.Add(more);
        }
        Ui.At(header,actions,col:1);header.ContextMenu=CreateMenu();
        var headerBorder=new Border{Background=Mode==DisplayMode.Standard?Ui.PaperDeep:Ui.Paper,CornerRadius=new CornerRadius(11,11,0,0),Child=header};Ui.At(root,headerBorder);
        if(folded){Content=root;return;}
        if(Mode==DisplayMode.Standard)
        {
            var dates=Ui.Columns();dates.Margin=new Thickness(18,16,18,13);
            var dateText=new StackPanel{Orientation=Orientation.Horizontal};dateLabel=Ui.Label(Ui.DateText(Service.Today),22,true);dateText.Children.Add(dateLabel);var week=Ui.Label(Ui.Weekday(Service.Today),13,color:Ui.Muted);weekLabel=week;week.Margin=new Thickness(9,0,0,0);dateText.Children.Add(week);Ui.At(dates,dateText);
            count=Ui.Label("",12,color:Ui.Muted);Ui.At(dates,count,col:1);Ui.At(root,dates,1);
        }
        else count=Ui.Label("");
        list=new StackPanel();var scroll=Ui.Scroll(list);scroll.Margin=new Thickness(Mode==DisplayMode.Standard?18:12,0,Mode==DisplayMode.Standard?18:12,0);Ui.At(root,scroll,2);
        var footer=new StackPanel{Margin=new Thickness(Mode==DisplayMode.Standard?18:12,2,Mode==DisplayMode.Standard?18:12,Mode==DisplayMode.Standard?9:12)};
        var completedRow=Ui.Columns();done=Ui.Button("已完成",()=>{expandedDone=!expandedDone;Refresh();},quiet:true,automationId:"ToggleCompleted");done.FontSize=12;done.Foreground=Ui.Muted;done.Padding=new Thickness(0,8,0,8);done.HorizontalAlignment=HorizontalAlignment.Left;Ui.At(completedRow,done);
        undoButton=Ui.Button("撤销完成",Undo,quiet:true,automationId:"UndoCompletion");undoButton.FontSize=12;undoButton.Foreground=Ui.Green;undoButton.Visibility=undoId is null?Visibility.Collapsed:Visibility.Visible;Ui.At(completedRow,undoButton,col:1);footer.Children.Add(completedRow);
        var add=Ui.Columns();QuickInput=new TextBox{Text=draft,MaxLength=200,Background=Brushes.Transparent,BorderBrush=Ui.PaperDeep,FontSize=12,Padding=new Thickness(9,8,5,8),ToolTip="新增重要事项，按 Enter 添加"};System.Windows.Automation.AutomationProperties.SetAutomationId(QuickInput,"QuickAddInput");
        var entry=new Grid();var hint=Ui.Label("＋ 新增重要事项，按 Enter 添加",12,color:Ui.Muted);hint.Margin=new Thickness(10,0,4,0);hint.IsHitTestVisible=false;hint.Visibility=string.IsNullOrEmpty(draft)?Visibility.Visible:Visibility.Collapsed;entry.Children.Add(QuickInput);entry.Children.Add(hint);QuickInput.TextChanged+=(_,_)=>{draft=QuickInput.Text;hint.Visibility=string.IsNullOrEmpty(draft)?Visibility.Visible:Visibility.Collapsed;};
        QuickInput.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;AddQuick();}};Ui.At(add,entry);var plus=Ui.Button("＋",AddQuick,quiet:true,automationId:"QuickAdd");plus.Margin=new Thickness(5,0,0,0);plus.Padding=new Thickness(5);Ui.At(add,plus,col:1);footer.Children.Add(add);
        carry=Ui.Label("",11,color:Ui.Muted);carry.Visibility=Visibility.Collapsed;carry.Margin=new Thickness(0,5,0,0);footer.Children.Add(carry);
        if(Mode==DisplayMode.Standard)
        {
            var links=Ui.Columns();links.Margin=new Thickness(-6,7,-6,0);var plan=Ui.Button("规划今日",()=>ShowManagement("plan"),quiet:true,automationId:"OpenPlan");plan.FontSize=12;plan.HorizontalAlignment=HorizontalAlignment.Left;Ui.At(links,plan);var history=Ui.Button("历史记录",()=>ShowManagement("history"),quiet:true,automationId:"OpenHistory");history.FontSize=12;Ui.At(links,history,col:1);footer.Children.Add(links);
        }
        Ui.At(root,footer,3);
        Content=new Border{Child=root,BorderBrush=Ui.PaperDeep,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12)};
        Refresh();
    }

    public void Refresh()
    {
        shownDate=Service.Today;if(folded)return;
        if(dateLabel is not null)dateLabel.Text=Ui.DateText(Service.Today)+(Mode==DisplayMode.Compact?"  "+Ui.Weekday(Service.Today):"");if(weekLabel is not null)weekLabel.Text=Ui.Weekday(Service.Today);
        var items=Service.ForToday();var today=items.Where(t=>t.Date==Service.Today).ToArray();count.Text=$"已完成 {today.Count(t=>t.Status==WorkStatus.Completed)} / {today.Length}";
        var old=items.Count(t=>t.Date<Service.Today);carry.Text=old>0?$"含 {old} 项延续事项，保留原归属日期":"";carry.Visibility=old>0?Visibility.Visible:Visibility.Collapsed;
        list.Children.Clear();
        var running=items.Where(t=>t.Status==WorkStatus.Running).ToArray();var pending=items.Where(t=>t.Status==WorkStatus.Pending).ToArray();var completed=items.Where(t=>t.Status==WorkStatus.Completed).ToArray();
        if(Mode==DisplayMode.Standard)
        {
            if(running.Length>0){Heading("进行中");foreach(var t in running)list.Children.Add(TaskRow(t));}
            Heading("待开始",$"{pending.Length} 项");foreach(var t in pending)list.Children.Add(TaskRow(t));
        }
        else foreach(var t in running.Concat(pending))list.Children.Add(TaskRow(t));
        if(expandedDone)foreach(var t in completed)list.Children.Add(TaskRow(t));
        if(items.Count==0){var empty=Ui.Label("今天最重要的几件事，先写下来。",13,color:Ui.Muted);empty.Margin=new Thickness(0,20,0,14);list.Children.Add(empty);}
        done.Content=$"已完成 · {completed.Length}  {(expandedDone?"⌃":"⌄")}";
        void Heading(string title,string detail=""){var grid=Ui.Columns();grid.Margin=new Thickness(0,12,0,8);Ui.At(grid,Ui.Label(title,12,color:Ui.Muted));Ui.At(grid,Ui.Label(detail,12,color:Ui.Muted),col:1);list.Children.Add(grid);}
    }

    internal void CheckDayChange(){if(Service.Today!=shownDate){Refresh();DataChanged?.Invoke(this,EventArgs.Empty);TryBackup();}}

    private FrameworkElement TaskRow(WorkItem task)
    {
        var running=task.Status==WorkStatus.Running;var completed=task.Status==WorkStatus.Completed;
        var title=Ui.Label(task.Title,14,running&&!completed,completed?Ui.Muted:Ui.Text);if(completed)title.TextDecorations=TextDecorations.Strikethrough;
        var time=task.Status==WorkStatus.Pending?task.PlannedText:task.StartedAt is { } start?FormatActual(task,start):"";
        if(task.Date<Service.Today)time=$"延续 {task.Date:MM-dd}"+(time.Length>0?" · "+time:"");
        var left=Ui.Stack(title);if(time.Length>0){var meta=Ui.Label(time,12,color:Ui.Muted);meta.Margin=new Thickness(0,3,0,0);left.Children.Add(meta);}
        var action=completed?null:Ui.Button(running?"完成":"开始",()=>{if(running)Complete(task.Id);else Execute(()=>Service.Start(task.Id));},running,quiet:Mode==DisplayMode.Compact&&!running,automationId:(running?"Complete-":"Start-")+task.Id);
        if(action is not null){action.FontSize=Mode==DisplayMode.Compact?12:14;action.MinHeight=Mode==DisplayMode.Compact?30:36;action.Padding=new Thickness(Mode==DisplayMode.Compact?8:11,5,Mode==DisplayMode.Compact?8:11,5);}
        FrameworkElement content;
        if(running&&Mode==DisplayMode.Standard)
        {
            left.Children.Clear();var panel=new StackPanel();panel.Children.Add(title);var bottom=Ui.Columns();bottom.Margin=new Thickness(0,10,0,0);Ui.At(bottom,Ui.Label(time,12,color:Ui.Muted));Ui.At(bottom,action!,col:1);panel.Children.Add(bottom);content=Ui.Card(panel,Ui.Soft,new Thickness(14,13,14,13));content.Margin=new Thickness(0,0,0,8);
        }
        else
        {
            var row=Ui.Columns();Ui.At(row,left);if(action is not null){action.Margin=new Thickness(10,0,0,0);Ui.At(row,action,col:1);}else Ui.At(row,Ui.Label("✓",12,color:Ui.Muted),col:1);
            content=new Border{Child=row,Background=running?Ui.Soft:Brushes.Transparent,CornerRadius=new CornerRadius(running?7:0),Padding=new Thickness(running?8:0,running?10:12,running?8:0,running?10:12),BorderBrush=Ui.PaperDeep,BorderThickness=new Thickness(0,0,0,running?0:1),Margin=new Thickness(0,running?3:0,0,running?4:0)};
        }
        content.ContextMenu=ItemMenu(task);content.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2){e.Handled=true;Edit(task);}};content.ToolTip="双击编辑；右键查看更多操作";
        return content;
    }

    private string FormatActual(WorkItem t,DateTimeOffset start)=>t.CompletedAt is { } end?$"{start:MM-dd HH:mm} — {end:MM-dd HH:mm}":DateOnly.FromDateTime(start.DateTime)==Service.Today?$"{start:HH:mm} 开始":$"{start:MM-dd HH:mm} 开始";
    internal ContextMenu ItemMenu(WorkItem item)
    {
        var m=new ContextMenu();m.Items.Add(Ui.Menu("编辑事项",()=>Edit(item)));m.Items.Add(Ui.Menu("补记 / 修正实际时间",()=>Edit(item,true)));
        if(item.Status==WorkStatus.Completed)m.Items.Add(Ui.Menu("撤销完成",()=>Execute(()=>Service.Reopen(item.Id))));
        if(item.Status==WorkStatus.Running)m.Items.Add(Ui.Menu("重置为待开始",()=>Execute(()=>Service.ResetToPending(item.Id))));
        m.Items.Add(new Separator());m.Items.Add(Ui.Menu("上移",()=>Execute(()=>Service.Move(item.Id,-1))));m.Items.Add(Ui.Menu("下移",()=>Execute(()=>Service.Move(item.Id,1))));
        m.Items.Add(Ui.Menu("删除（可从历史恢复）",()=>Execute(()=>Service.Delete(item.Id))));return m;
    }
    private ContextMenu CreateMenu()
    {
        var m=new ContextMenu();m.Items.Add(Ui.Menu(Mode==DisplayMode.Standard?"切换到精简模式":"切换到标准模式",()=>ChangeMode(Mode==DisplayMode.Standard?DisplayMode.Compact:DisplayMode.Standard)));
        m.Items.Add(Ui.Menu("今日规划",()=>ShowManagement("plan")));m.Items.Add(Ui.Menu("历史与月度导出",()=>ShowManagement("history")));m.Items.Add(Ui.Menu("窗口设置 / 备份恢复",ShowSettings));m.Items.Add(new Separator());m.Items.Add(Ui.Menu("收起到托盘",Hide));m.Items.Add(Ui.Menu("退出",Exit));return m;
    }
    private void AddQuick(){var title=QuickInput.Text;if(string.IsNullOrWhiteSpace(title))return;if(Execute(()=>Service.Add(new WorkInput(title)))){draft="";QuickInput.Clear();QuickInput.Focus();}}
    private void Complete(string id)=>Execute(()=>{var item=Service.Complete(id);undoId=id;undoVersion=item.Version;undoButton.Visibility=Visibility.Visible;undoTimer.Stop();undoTimer.Start();});
    private void Undo(){if(undoId is null)return;if(Execute(()=>Service.Reopen(undoId,undoVersion))){undoId=null;undoVersion=null;undoButton.Visibility=Visibility.Collapsed;undoTimer.Stop();}}
    public bool Execute(Action action)
    {
        try{action();Refresh();DataChanged?.Invoke(this,EventArgs.Empty);TryBackup();return true;}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {Program.Log(DataDirectory,ex);if(Testing)throw;MessageBox.Show(this,ex.Message,"操作未完成",MessageBoxButton.OK,MessageBoxImage.Warning);return false;}
    }
    private void TryBackup(){try{Service.TryAutomaticBackup(Path.Combine(DataDirectory,"backups"));}catch(Exception ex){Program.Log(DataDirectory,ex);}}
    public void Edit(WorkItem item,bool correction=false){var dialog=new EditWindow(this,item,correction){Owner=IsVisible?this:null};dialog.ShowDialog();}
    public void ShowManagement(string page)
    {
        if(management is null){management=new ManagementWindow(this);management.Closed+=(_,_)=>management=null;}
        management.ShowPage(page);management.Show();management.Activate();
    }
    private void ShowSettings(){var dialog=new SettingsWindow(this){Owner=IsVisible?this:null};dialog.ShowDialog();}
    public void ReloadSettings(){var settings=Service.Repository.LoadSettings();Ui.ApplyTheme(settings.Theme);Topmost=settings.Topmost;Build();management?.Close();management=null;}

    public void ChangeMode(DisplayMode value)
    {
        if(value==Mode)return;if(folded)ToggleFold();SaveGeometry();draft=QuickInput.Text;
        var settings=Service.Repository.LoadSettings();Service.Repository.SaveSettings(settings with{Mode=value});Mode=value;
        Build();RestoreGeometry();
    }
    private void ToggleFold()
    {
        if(!folded){SaveGeometry();expandedHeight=Height;folded=true;Build();MinHeight=48;Height=48;ResizeMode=ResizeMode.NoResize;}
        else{folded=false;ResizeMode=ResizeMode.CanResizeWithGrip;Build();Height=Math.Max(MinHeight,expandedHeight);}
    }
    private void QueueGeometry(){if(restoring||folded||!IsLoaded||WindowState!=WindowState.Normal)return;geometryTimer.Stop();geometryTimer.Start();}
    internal void SaveGeometry()
    {
        if(restoring||folded||WindowState!=WindowState.Normal||!double.IsFinite(Left)||!double.IsFinite(Top))return;
        var settings=Service.Repository.LoadSettings();var b=ScreenPlacement.Capture(this);
        settings=Mode==DisplayMode.Standard?settings with{StandardBounds=b}:settings with{CompactBounds=b};Service.Repository.SaveSettings(settings with{Mode=Mode,Topmost=Topmost});
    }
    private void RestoreGeometry()
    {
        restoring=true;var s=Service.Repository.LoadSettings();var b=Mode==DisplayMode.Standard?s.StandardBounds:s.CompactBounds;var target=ScreenPlacement.Target(b);var work=target.Work;
        Width=Math.Min(Math.Max(MinWidth,b?.Width??(Mode==DisplayMode.Standard?390:340)),work.Width/target.Scale);
        Height=Math.Min(Math.Max(MinHeight,b?.Height??(Mode==DisplayMode.Standard?565:360)),work.Height/target.Scale);
        WindowStartupLocation=WindowStartupLocation.Manual;
        var currentScale=VisualTreeHelper.GetDpi(this).DpiScaleX;Left=target.X/currentScale;Top=target.Y/currentScale;
        Dispatcher.BeginInvoke(new Action(()=>ScreenPlacement.Apply(this,target.X,target.Y,target.Scale,work)),DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(new Action(()=>restoring=false),DispatcherPriority.ApplicationIdle);
    }
    public void Reveal(){if(!IsVisible)Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
    private void OnClosing(object? sender,CancelEventArgs e){if(Exiting)return;e.Cancel=true;SaveGeometry();Hide();}
    public void Exit(){if(Exiting)return;SaveGeometry();Exiting=true;management?.Close();tray.Visible=false;Close();Application.Current.Shutdown();}
    internal void ExitForTests(){Exiting=true;tray.Visible=false;management?.Close();Close();}
    public void Dispose(){dayTimer.Stop();geometryTimer.Stop();undoTimer.Stop();tray.Visible=false;tray.Dispose();}
}
