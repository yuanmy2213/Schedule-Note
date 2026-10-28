using System.Runtime.InteropServices;
using System.Windows.Interop;
using Forms=System.Windows.Forms;

namespace JinRiJian.App;

internal static class ScreenPlacement
{
    [StructLayout(LayoutKind.Sequential)]internal struct Rectangle{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr hwnd,out Rectangle rect);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromPoint(Point point,uint flags);
    [DllImport("shcore.dll")]private static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);

    public static WindowBounds Capture(System.Windows.Window window)
    {
        var hwnd=new WindowInteropHelper(window).Handle;var dpi=VisualTreeHelper.GetDpi(window).DpiScaleX;
        if(hwnd!=IntPtr.Zero&&GetWindowRect(hwnd,out var r))
        {
            var screen=Forms.Screen.FromRectangle(new System.Drawing.Rectangle(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top));
            return new(r.Left,r.Top,window.ActualWidth,window.ActualHeight,screen.DeviceName,dpi);
        }
        return new(window.Left*dpi,window.Top*dpi,window.Width,window.Height,Dpi:dpi);
    }

    public static (int X,int Y,double Scale,Rect Work) Target(WindowBounds? bounds)
    {
        var screen=bounds is null?Forms.Screen.PrimaryScreen:Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==bounds.Monitor);
        screen??=Forms.Screen.PrimaryScreen??Forms.Screen.AllScreens[0];
        var area=screen.WorkingArea;var monitor=MonitorFromPoint(new Point{X=area.Left+area.Width/2,Y=area.Top+area.Height/2},2);
        var scale=1d;
        if(GetDpiForMonitor(monitor,0,out var x,out _)==0&&x>0)scale=x/96d;
        var px=bounds is null?area.Right-(int)(390*scale)-24:(int)Math.Round(bounds.Left);
        var py=bounds is null?area.Top+50:(int)Math.Round(bounds.Top);
        return(px,py,scale,new Rect(area.Left,area.Top,area.Width,area.Height));
    }

    public static void Apply(System.Windows.Window window,int x,int y,double scale,Rect work)
    {
        var w=(int)Math.Round(window.Width*scale);var h=(int)Math.Round(window.Height*scale);
        x=(int)Math.Clamp(x,work.Left,Math.Max(work.Left,work.Right-w));y=(int)Math.Clamp(y,work.Top,Math.Max(work.Top,work.Bottom-h));
        var hwnd=new WindowInteropHelper(window).Handle;
        if(hwnd!=IntPtr.Zero&&!SetWindowPos(hwnd,IntPtr.Zero,x,y,w,h,0x0014))throw new InvalidOperationException("无法恢复窗口位置。");
    }
}
