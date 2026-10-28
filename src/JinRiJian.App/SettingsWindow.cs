using System.Diagnostics;
using Microsoft.Win32;

namespace JinRiJian.App;

public sealed class SettingsWindow : System.Windows.Window
{
    public SettingsWindow(NoteWindow note)
    {
        Ui.BaseWindow(this,"窗口设置与备份",470,590);MinWidth=350;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var prefs=note.Service.Repository.LoadSettings();var form=new StackPanel{Margin=new Thickness(22)};form.Children.Add(Ui.Label("窗口设置",20,true));
        var top=new CheckBox{Content="始终显示在其他窗口上方",IsChecked=note.Topmost,Margin=new Thickness(0,20,0,12)};var auto=new CheckBox{Content="开机后自动显示便利贴",IsChecked=prefs.AutoStart,Margin=new Thickness(0,0,0,16)};form.Children.Add(top);form.Children.Add(auto);
        var mode=new ComboBox{ItemsSource=new[]{"标准模式（第一版）","精简模式（第二版）"},SelectedIndex=note.Mode==DisplayMode.Standard?0:1,Padding=new Thickness(7)};form.Children.Add(Ui.Field("界面模式",mode));
        var theme=new ComboBox{ItemsSource=new[]{"跟随系统","浅色","深色"},SelectedIndex=prefs.Theme=="dark"?2:prefs.Theme=="light"?1:0,Padding=new Thickness(7)};form.Children.Add(Ui.Field("主题",theme));
        var width=new TextBox{Text=note.ActualWidth.ToString("0",CultureInfo.InvariantCulture)};var height=new TextBox{Text=note.ActualHeight.ToString("0",CultureInfo.InvariantCulture)};
        var sizes=Ui.Columns();sizes.ColumnDefinitions[1].Width=new GridLength(1,GridUnitType.Star);var wf=Ui.Field("当前模式宽度",width);wf.Margin=new Thickness(0,0,8,15);Ui.At(sizes,wf);Ui.At(sizes,Ui.Field("当前模式高度",height),col:1);form.Children.Add(sizes);
        form.Children.Add(Ui.Label("两种模式分别记住位置和大小，事项与时间记录共用。",12,color:Ui.Muted));
        var error=Ui.Label("",12,color:Brushes.IndianRed);error.Margin=new Thickness(0,10,0,8);form.Children.Add(error);
        form.Children.Add(Ui.Button("保存设置",()=>
        {
            try
            {
                if(!double.TryParse(width.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var w)||!double.TryParse(height.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var h)||!double.IsFinite(w)||!double.IsFinite(h)||w<260||h<(note.Mode==DisplayMode.Standard?360:220))throw new ArgumentException("请填写有效的窗口大小；宽度至少 260，标准模式高度至少 360，精简模式至少 220。");
                AutoStart.Set(auto.IsChecked==true);
                var latest=note.Service.Repository.LoadSettings();note.Service.Repository.SaveSettings(latest with{Topmost=top.IsChecked==true,AutoStart=auto.IsChecked==true,Theme=theme.SelectedIndex==2?"dark":theme.SelectedIndex==1?"light":"system"});
                note.Topmost=top.IsChecked==true;note.Width=Math.Min(w,SystemParameters.WorkArea.Width);note.Height=Math.Min(h,SystemParameters.WorkArea.Height);note.SaveGeometry();note.ChangeMode(mode.SelectedIndex==1?DisplayMode.Compact:DisplayMode.Standard);note.ReloadSettings();Close();
            }
            catch(Exception ex) when(ex is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException){error.Text=ex.Message;}
        },true));
        form.Children.Add(Ui.Divider());var backupTitle=Ui.Label("数据与备份",16,true);backupTitle.Margin=new Thickness(0,15,0,10);form.Children.Add(backupTitle);
        var backupRow=new StackPanel{Orientation=Orientation.Horizontal};var backup=Ui.Button("立即备份",()=>Backup(note));backup.Margin=new Thickness(0,0,8,0);backupRow.Children.Add(backup);backupRow.Children.Add(Ui.Button("恢复备份",()=>Restore(note)));form.Children.Add(backupRow);
        form.Children.Add(Ui.Button("打开数据目录",()=>Process.Start(new ProcessStartInfo("explorer.exe",note.DataDirectory){UseShellExecute=true}),quiet:true));
        var description=Ui.Label("备份使用 SQLite 快照。恢复前会自动保留当前数据；已删除事项可在历史窗口中查看并恢复。",12,color:Ui.Muted);description.Margin=new Thickness(0,8,0,0);form.Children.Add(description);Content=Ui.Scroll(form);
    }
    private void Backup(NoteWindow note)
    {
        var dialog=new SaveFileDialog{FileName=$"今日笺备份_{DateTime.Now:yyyyMMdd-HHmmss}.db",DefaultExt=".db",Filter="今日笺备份 (*.db)|*.db"};if(dialog.ShowDialog(this)!=true)return;
        try{note.Service.Repository.BackupTo(dialog.FileName);MessageBox.Show(this,"备份已保存。","备份完成");}catch(Exception ex){MessageBox.Show(this,ex.Message,"备份未完成");}
    }
    private void Restore(NoteWindow note)
    {
        var dialog=new OpenFileDialog{Filter="今日笺备份 (*.db)|*.db",CheckFileExists=true};if(dialog.ShowDialog(this)!=true)return;
        if(MessageBox.Show(this,"恢复会用所选备份替换当前数据。当前数据会先保留一份安全备份。是否继续？","恢复备份",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        if(note.Execute(()=>note.Service.Repository.RestoreFrom(dialog.FileName,Path.Combine(note.DataDirectory,"backups")))){note.ReloadSettings();MessageBox.Show(this,"数据已恢复，恢复前数据保存在 backups 目录。","恢复完成");Close();}
    }
}

internal static class AutoStart
{
    public static void Set(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled){var exe=Environment.ProcessPath??throw new InvalidOperationException("无法确定程序路径。");key.SetValue("JinRiJian","\""+exe+"\"");}
        else key.DeleteValue("JinRiJian",false);
    }
}
