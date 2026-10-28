using System.Windows.Markup;
using System.Windows.Shell;
using Microsoft.Win32;

namespace JinRiJian.App;

internal static class Ui
{
    public static bool Dark { get; private set; }
    public static Brush Paper => B(Dark?"#2C2A23":"#FFF9E9");
    public static Brush PaperDeep => B(Dark?"#3A372B":"#F3E9CC");
    public static Brush Window => B(Dark?"#222725":"#FCFCFA");
    public static Brush Subtle => B(Dark?"#2B302D":"#F3F5F1");
    public static Brush Text => B(Dark?"#E2EAE5":"#263C33");
    public static Brush Muted => B(Dark?"#B0BFB5":"#607367");
    public static Brush Line => B(Dark?"#465249":"#DCE3DB");
    public static Brush Green => B(Dark?"#A0CDB3":"#315C49");
    public static Brush OnGreen => B(Dark?"#173327":"#FFFEFA");
    public static Brush Soft => B(Dark?"#35483C":"#E8F0E8");
    public static Brush White => B(Dark?"#29322D":"#FFFFFF");
    public static Brush B(string hex)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    public static void ApplyTheme(string value)
    {
        Dark=value=="dark"||value=="system"&&(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int light&&light==0);
        var resources=Application.Current.Resources;
        resources["AppText"]=Text;resources["AppLine"]=Line;resources["AppInput"]=White;
        resources[typeof(Button)]=XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Setter Property="FontFamily" Value="Segoe UI, Microsoft YaHei UI"/><Setter Property="FontSize" Value="14"/>
              <Setter Property="Foreground" Value="{DynamicResource AppText}"/><Setter Property="BorderBrush" Value="{DynamicResource AppLine}"/>
              <Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="10,6"/><Setter Property="MinHeight" Value="34"/>
              <Setter Property="Background" Value="Transparent"/><Setter Property="Cursor" Value="Hand"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.82"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="surface" Property="BorderThickness" Value="2"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
            </Style>
            """);
        resources[typeof(TextBox)]=XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TextBox"><Setter Property="Padding" Value="9,8"/><Setter Property="FontSize" Value="14"/><Setter Property="Foreground" Value="{DynamicResource AppText}"/><Setter Property="Background" Value="{DynamicResource AppInput}"/><Setter Property="BorderBrush" Value="{DynamicResource AppLine}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="VerticalContentAlignment" Value="Center"/></Style>
            """);
    }
    public static void BaseWindow(System.Windows.Window window,string title,double width,double height)
    {
        var area=SystemParameters.WorkArea;window.Title=title;window.Width=Math.Min(width,area.Width);window.Height=Math.Min(height,area.Height);window.Background=Window;window.Foreground=Text;
        window.FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");window.FontSize=14;window.UseLayoutRounding=true;window.SnapsToDevicePixels=true;
        window.WindowStartupLocation=WindowStartupLocation.CenterScreen;
    }
    public static void Chrome(System.Windows.Window window,int caption=48)
    {
        window.WindowStyle=WindowStyle.None;window.ResizeMode=ResizeMode.CanResizeWithGrip;
        WindowChrome.SetWindowChrome(window,new WindowChrome {CaptionHeight=caption,ResizeBorderThickness=new Thickness(7),CornerRadius=new CornerRadius(10),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
    }
    public static TextBlock Label(string text,double size=14,bool bold=false,Brush? color=null)=>new(){Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Foreground=color??Text,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
    public static Button Button(string text,Action action,bool primary=false,bool quiet=false,string? automationId=null)
    {
        var button=new Button {Content=text,Background=primary?Green:Brushes.Transparent,Foreground=primary?OnGreen:Text,BorderBrush=primary?Green:Line,BorderThickness=new Thickness(quiet?0:1)};
        if(automationId is not null)System.Windows.Automation.AutomationProperties.SetAutomationId(button,automationId);
        button.Click+=(_,_)=>action();WindowChrome.SetIsHitTestVisibleInChrome(button,true);return button;
    }
    public static Border Card(UIElement child,Brush? fill=null,Thickness? padding=null)=>new(){Child=child,Background=fill??Soft,CornerRadius=new CornerRadius(9),Padding=padding??new Thickness(13,12,13,12)};
    public static Border Divider()=>new(){Height=1,Background=Line,Margin=new Thickness(0,2,0,2)};
    public static ScrollViewer Scroll(UIElement content)=>new(){Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    public static StackPanel Stack(params UIElement[] children){var panel=new StackPanel();foreach(var child in children)panel.Children.Add(child);return panel;}
    public static void At(Grid grid,UIElement child,int row=0,int col=0){Grid.SetRow(child,row);Grid.SetColumn(child,col);grid.Children.Add(child);}
    public static Grid Columns(double fixedRight=0)
    {
        var g=new Grid();g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=fixedRight>0?new GridLength(fixedRight):GridLength.Auto});return g;
    }
    public static FrameworkElement Field(string label,Control input)
    {
        Detach(input);
        var t=Label(label,12,color:Muted);t.Margin=new Thickness(0,0,0,6);var p=Stack(t,input);p.Margin=new Thickness(0,0,0,15);return p;
    }
    public static void Detach(FrameworkElement element)
    {
        if(element.Parent is Panel panel)panel.Children.Remove(element);
        else if(element.Parent is Decorator decorator)decorator.Child=null;
        else if(element.Parent is ContentControl content)content.Content=null;
    }
    public static MenuItem Menu(string text,Action action){var item=new MenuItem{Header=text};item.Click+=(_,_)=>action();return item;}
    public static string DateText(DateOnly day)=>$"{day.Month}月{day.Day}日";
    public static string Weekday(DateOnly day)=>new[]{"周日","周一","周二","周三","周四","周五","周六"}[(int)day.DayOfWeek];
}
