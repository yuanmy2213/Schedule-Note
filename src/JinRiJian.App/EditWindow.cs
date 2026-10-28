namespace JinRiJian.App;

public sealed class EditWindow : System.Windows.Window
{
    public EditWindow(NoteWindow note,WorkItem item,bool correctTimes=false)
    {
        Ui.BaseWindow(this,"事项详情",460,correctTimes?650:490);ResizeMode=ResizeMode.CanResize;MinWidth=340;MinHeight=350;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var input=new TextBox{Text=item.Title,MaxLength=200};var ps=new TextBox{Text=item.PlannedStart?.ToString("HH:mm")??""};var pe=new TextBox{Text=item.PlannedEnd?.ToString("HH:mm")??""};var memo=new TextBox{Text=item.Note,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=90,VerticalContentAlignment=VerticalAlignment.Top};
        var form=new StackPanel{Margin=new Thickness(20)};form.Children.Add(Ui.Label("事项详情",20,true));var date=Ui.Label("归属日期："+item.Date.ToString("yyyy-MM-dd"),12,color:Ui.Muted);date.Margin=new Thickness(0,6,0,18);form.Children.Add(date);form.Children.Add(Ui.Field("事项名称",input));
        var times=Ui.Columns();times.ColumnDefinitions[1].Width=new GridLength(1,GridUnitType.Star);var pf=Ui.Field("预计开始 · 选填",ps);pf.Margin=new Thickness(0,0,8,15);Ui.At(times,pf);Ui.At(times,Ui.Field("预计结束 · 选填",pe),col:1);form.Children.Add(times);form.Children.Add(Ui.Field("备注 · 选填",memo));
        form.Children.Add(Ui.Label("实际记录："+item.ActualText,12,color:Ui.Muted));
        var adjust=new CheckBox{Content="补记 / 修正实际时间",IsChecked=correctTimes,Margin=new Thickness(0,15,0,10)};form.Children.Add(adjust);
        var started=new TextBox{Text=item.StartedAt?.ToString("yyyy-MM-dd HH:mm")??""};var ended=new TextBox{Text=item.CompletedAt?.ToString("yyyy-MM-dd HH:mm")??""};
        var actual=Ui.Stack(Ui.Field("实际开始（yyyy-MM-dd HH:mm）",started),Ui.Field("实际完成（留空表示进行中）",ended),Ui.Label("按电脑当前时区解释；不会自动编造开始时间。",12,color:Ui.Muted));actual.Visibility=correctTimes?Visibility.Visible:Visibility.Collapsed;form.Children.Add(actual);
        adjust.Checked+=(_,_)=>actual.Visibility=Visibility.Visible;adjust.Unchecked+=(_,_)=>actual.Visibility=Visibility.Collapsed;
        var error=Ui.Label("",12,color:Brushes.IndianRed);error.Margin=new Thickness(0,10,0,5);form.Children.Add(error);
        form.Children.Add(Ui.Button("保存",()=>
        {
            try
            {
                var edited=new WorkInput(input.Text,WorkInput.ParseTime(ps.Text),WorkInput.ParseTime(pe.Text),memo.Text).Validate();
                DateTimeOffset? begin=null,end=null;
                if(adjust.IsChecked==true)
                {
                    begin=ParseLocal(started.Text,note.Service.Clock.Zone);
                    if(!string.IsNullOrWhiteSpace(ended.Text))end=ParseLocal(ended.Text,note.Service.Clock.Zone);
                    if(end<begin)throw new ArgumentException("完成时间不能早于开始时间。");
                }
                var success=note.Execute(()=>
                {
                    if(begin is { } b)note.Service.EditAndCorrectTimes(item.Id,edited,b,end,item.Version);
                    else note.Service.Edit(item.Id,edited,item.Version);
                });
                if(success)Close();
            }
            catch(ArgumentException ex){error.Text=ex.Message;}
        },true,automationId:"EditSave"));
        Content=Ui.Scroll(form);
    }
    internal static DateTimeOffset ParseLocal(string text,TimeZoneInfo zone)
    {
        if(!DateTime.TryParseExact(text.Trim(),"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var value))throw new ArgumentException("实际时间请按 yyyy-MM-dd HH:mm 填写。");
        value=DateTime.SpecifyKind(value,DateTimeKind.Unspecified);
        if(zone.IsInvalidTime(value))throw new ArgumentException("该时间处于夏令时跳过区间，请修改。");
        if(zone.IsAmbiguousTime(value))throw new ArgumentException("该时间处于夏令时重复区间，请在其他时点记录，或调整电脑时区后修正。");
        return new DateTimeOffset(value,zone.GetUtcOffset(value));
    }
}
