using System.Windows.Input;
using Microsoft.Win32;

namespace JinRiJian.App;

public sealed class ManagementWindow : System.Windows.Window
{
    private readonly NoteWindow note;
    private readonly ContentControl page=new();
    private readonly Button planTab,historyTab;
    private string current="plan";
    private readonly TextBox title=new(){MaxLength=200},ps=new(),pe=new(),memo=new(){AcceptsReturn=true,Height=90,VerticalContentAlignment=VerticalAlignment.Top,TextWrapping=TextWrapping.Wrap};
    private StackPanel planList=new(),historyList=new();
    private readonly TextBox month=new(){Width=130,ToolTip="月份，格式为 yyyy-MM"};
    private readonly CheckBox deleted=new(){Content="显示已删除事项",VerticalAlignment=VerticalAlignment.Center};
    private string? editId;
    private long? editVersion;
    private TextBlock formTitle=Ui.Label("新增重要事项",16,true),error=Ui.Label("",12,color:Brushes.IndianRed);
    private Button save=null!;
    public ManagementWindow(NoteWindow owner)
    {
        note=owner;Ui.BaseWindow(this,"今日笺 · 规划与历史",920,660);Ui.Chrome(this);MinWidth=660;MinHeight=500;
        System.Windows.Automation.AutomationProperties.SetAutomationId(title,"PlanTitle");System.Windows.Automation.AutomationProperties.SetAutomationId(ps,"PlanStart");System.Windows.Automation.AutomationProperties.SetAutomationId(pe,"PlanEnd");
        var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(58)});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(55)});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(42)});
        var header=Ui.Columns();header.Margin=new Thickness(18,0,14,0);Ui.At(header,Ui.Label("▤  今日笺",14,true));var tools=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};tools.Children.Add(Ui.Button("回到便利贴 ↗",()=>{note.Reveal();Close();},quiet:true));tools.Children.Add(Ui.Button("×",Close,quiet:true));Ui.At(header,tools,col:1);Ui.At(root,new Border{Child=header,BorderBrush=Ui.Line,BorderThickness=new Thickness(0,0,0,1)});
        var tabs=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(24,8,24,0)};planTab=Ui.Button("今日规划",()=>ShowPage("plan"),quiet:true);historyTab=Ui.Button("历史记录",()=>ShowPage("history"),quiet:true);planTab.Margin=new Thickness(0,0,12,0);tabs.Children.Add(planTab);tabs.Children.Add(historyTab);Ui.At(root,new Border{Child=tabs,BorderBrush=Ui.Line,BorderThickness=new Thickness(0,0,0,1)},1);
        page.Margin=new Thickness(24,22,24,16);Ui.At(root,page,2);
        var foot=Ui.Columns();foot.Margin=new Thickness(24,0,24,0);Ui.At(foot,Ui.Label("本机保存 · 无需登录",12,color:Ui.Muted));Ui.At(foot,Ui.Label("事项历时包含中途间隔，不代表净工时",12,color:Ui.Muted),col:1);Ui.At(root,new Border{Child=foot,BorderBrush=Ui.Line,BorderThickness=new Thickness(0,1,0,0)},3);Content=root;
        month.Text=note.Service.Today.ToString("yyyy-MM");deleted.Checked+=(_,_)=>Refresh();deleted.Unchecked+=(_,_)=>Refresh();month.KeyDown+=(_,e)=>{if(e.Key==Key.Enter)Refresh();};
        note.DataChanged+=OnChanged;Closed+=(_,_)=>note.DataChanged-=OnChanged;ShowPage("plan");
    }
    private void OnChanged(object? sender,EventArgs e)=>Refresh();
    public void ShowPage(string name){current=name;planTab.Background=name=="plan"?Ui.Soft:Brushes.Transparent;historyTab.Background=name=="history"?Ui.Soft:Brushes.Transparent;if(name=="plan")BuildPlan();else BuildHistory();Refresh();}
    private void BuildPlan()
    {
        var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(74)});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        var heading=Ui.Stack(Ui.Label("今天，把重要的事做好。",23,true),Ui.Label($"{note.Service.Today:yyyy年MM月dd日} · {Ui.Weekday(note.Service.Today)}",12,color:Ui.Muted));Ui.At(root,heading);
        var columns=Ui.Columns(286);columns.ColumnDefinitions.Insert(1,new ColumnDefinition{Width=new GridLength(28)});planList=new StackPanel();Ui.At(columns,Ui.Scroll(planList));
        var form=new StackPanel();formTitle=Ui.Label(editId is null?"新增重要事项":"编辑事项",16,true);formTitle.Margin=new Thickness(0,0,0,17);form.Children.Add(formTitle);form.Children.Add(Ui.Field("事项名称 · 必填",title));
        var times=new Grid();times.ColumnDefinitions.Add(new ColumnDefinition());times.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});times.ColumnDefinitions.Add(new ColumnDefinition());Ui.At(times,Ui.Field("预计开始 · 选填",ps));Ui.At(times,Ui.Field("预计结束 · 选填",pe),col:2);form.Children.Add(times);form.Children.Add(Ui.Field("备注 · 选填",memo));
        error=Ui.Label("",12,color:Brushes.IndianRed);error.Margin=new Thickness(0,0,0,8);form.Children.Add(error);save=Ui.Button(editId is null?"添加事项":"保存修改",Save,true,automationId:"PlanSave");form.Children.Add(save);var hint=Ui.Label("只填事项名称，也能直接开始。",12,color:Ui.Muted);hint.Margin=new Thickness(0,12,0,0);form.Children.Add(hint);form.Children.Add(Ui.Button("清空 / 取消编辑",Clear,quiet:true));
        var surface=Ui.Card(form,Ui.Subtle,new Thickness(18));surface.VerticalAlignment=VerticalAlignment.Top;Ui.At(columns,Ui.Scroll(surface),col:2);Ui.At(root,columns,1);page.Content=root;
        title.KeyDown-=TitleKeyDown;title.KeyDown+=TitleKeyDown;
    }
    private void TitleKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;Save();}}
    private void Clear(){editId=null;editVersion=null;title.Clear();ps.Clear();pe.Clear();memo.Clear();formTitle.Text="新增重要事项";save.Content="添加事项";error.Text="";title.Focus();}
    private void Save()
    {
        try
        {
            var input=new WorkInput(title.Text,WorkInput.ParseTime(ps.Text),WorkInput.ParseTime(pe.Text),memo.Text).Validate();
            var success=note.Execute(()=>{if(editId is null)note.Service.Add(input);else note.Service.Edit(editId,input,editVersion);});if(success)Clear();
        }
        catch(ArgumentException ex){error.Text=ex.Message;}
    }
    private void BeginEdit(WorkItem item){editId=item.Id;editVersion=item.Version;title.Text=item.Title;ps.Text=item.PlannedStart?.ToString("HH:mm")??"";pe.Text=item.PlannedEnd?.ToString("HH:mm")??"";memo.Text=item.Note;formTitle.Text="编辑事项";save.Content="保存修改";title.Focus();}
    private void BuildHistory()
    {
        Ui.Detach(month);Ui.Detach(deleted);
        var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(75)});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        Ui.At(root,Ui.Stack(Ui.Label("做过的事，都有记录。",23,true),Ui.Label("按归属日期回看计划与完成情况",12,color:Ui.Muted)));
        var toolbar=Ui.Columns();var left=new StackPanel{Orientation=Orientation.Horizontal};left.Children.Add(month);var query=Ui.Button("查看",Refresh,quiet:true);query.Margin=new Thickness(8,0,8,0);left.Children.Add(query);left.Children.Add(deleted);Ui.At(toolbar,left);Ui.At(toolbar,Ui.Button("导出本月",()=>_=Export(),true,automationId:"ExportMonth"),col:1);toolbar.Margin=new Thickness(0,0,0,14);Ui.At(root,toolbar,1);historyList=new StackPanel();Ui.At(root,Ui.Scroll(historyList),2);page.Content=root;
    }
    private bool TryMonth(out DateOnly start)
    {
        if(DateOnly.TryParseExact(month.Text.Trim()+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out start))return true;
        historyList.Children.Clear();historyList.Children.Add(Ui.Label("月份请按 yyyy-MM 填写，例如 2026-10。",13,color:Brushes.IndianRed));return false;
    }
    public void Refresh()
    {
        if(current=="plan")
        {
            planList.Children.Clear();var items=note.Service.ForToday();
            foreach(var t in items)
            {
                var row=Ui.Columns();var details=Ui.Stack(Ui.Label(t.Title,14,true),Ui.Label(t.PlannedText.Length>0?t.PlannedText:"未设置计划时间",12,color:Ui.Muted));if(t.StartedAt is not null)details.Children.Add(Ui.Label("实际 "+t.ActualText,12,color:Ui.Muted));Ui.At(row,details);
                var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};var state=Ui.Label(t.StatusText,11,color:Ui.Muted);state.Margin=new Thickness(8,5,5,0);actions.Children.Add(state);actions.Children.Add(Ui.Button("编辑",()=>BeginEdit(t),quiet:true));Ui.At(row,actions,col:1);var border=new Border{Child=row,Padding=new Thickness(0,15,0,15),BorderBrush=Ui.Line,BorderThickness=new Thickness(0,0,0,1),ContextMenu=note.ItemMenu(t)};planList.Children.Add(border);
            }
            if(items.Count==0)planList.Children.Add(Ui.Label("今天最重要的几件事，先写下来。",13,color:Ui.Muted));
        }
        else
        {
            if(!TryMonth(out var start))return;historyList.Children.Clear();var items=note.Service.ForMonth(start.Year,start.Month,deleted.IsChecked==true);
            var summary=Ui.Label($"{items.Count} 项事项 · 已完成 {items.Count(t=>t.Status==WorkStatus.Completed)} 项",12,color:Ui.Muted);summary.Margin=new Thickness(0,0,0,12);historyList.Children.Add(summary);
            foreach(var day in items.GroupBy(t=>t.Date).OrderByDescending(g=>g.Key))
            {
                var date=Ui.Label(Ui.DateText(day.Key)+$"   {day.Count()} 项",14,true);date.Margin=new Thickness(0,13,0,8);historyList.Children.Add(date);
                foreach(var t in day)
                {
                    var row=Ui.Columns(280);var left=Ui.Stack(Ui.Label(t.Title,14,true),Ui.Label(t.PlannedText,12,color:Ui.Muted));Ui.At(row,left);
                    var right=Ui.Columns();Ui.At(right,Ui.Label(t.DeletedAt is not null?"已删除":t.StatusText,12,color:Ui.Muted));var actual=Ui.Label(t.ActualText,12,color:Ui.Muted);actual.HorizontalAlignment=HorizontalAlignment.Right;Ui.At(right,actual,col:1);Ui.At(row,right,col:1);
                    var entry=new Border{Child=row,Padding=new Thickness(0,12,0,12),BorderBrush=Ui.Line,BorderThickness=new Thickness(0,0,0,1)};
                    if(t.DeletedAt is null){entry.ContextMenu=note.ItemMenu(t);entry.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2)note.Edit(t);};}
                    else{entry.Opacity=.65;var menu=new ContextMenu();menu.Items.Add(Ui.Menu("恢复事项",()=>note.Execute(()=>note.Service.Restore(t.Id))));entry.ContextMenu=menu;}
                    historyList.Children.Add(entry);
                }
            }
            if(items.Count==0)historyList.Children.Add(Ui.Label("这个月还没有事项记录。",14,color:Ui.Muted));
        }
    }
    private async Task Export()
    {
        if(!TryMonth(out var start))return;var prefs=note.Service.Repository.LoadSettings();var dialog=new SaveFileDialog{FileName=$"办公事项_{start:yyyy-MM}.xlsx",Filter="Excel 工作簿 (*.xlsx)|*.xlsx",DefaultExt=".xlsx",AddExtension=true,InitialDirectory=Directory.Exists(prefs.LastExportDirectory)?prefs.LastExportDirectory:Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};
        if(dialog.ShowDialog(this)!=true)return;
        IsEnabled=false;
        try{await Task.Run(()=>note.Service.ExportMonth(start.Year,start.Month,dialog.FileName));var latest=note.Service.Repository.LoadSettings();note.Service.Repository.SaveSettings(latest with{LastExportDirectory=Path.GetDirectoryName(dialog.FileName)??""});MessageBox.Show(this,"月度记录已导出。\n"+dialog.FileName,"导出完成",MessageBoxButton.OK,MessageBoxImage.Information);}
        catch(Exception ex){Program.Log(note.DataDirectory,ex);MessageBox.Show(this,"导出未完成，请检查文件是否正在被使用。\n"+ex.Message,"导出未完成",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{IsEnabled=true;}
    }
}
