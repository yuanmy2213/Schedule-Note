using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;

namespace JinRiJian.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        var smoke=args.Contains("--smoke-test");
        string? Arg(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        var directory=Arg("--data-dir")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JinRiJian");
        Directory.CreateDirectory(directory);
        var identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory))))[..20];
        using var mutex=new Mutex(true,@"Local\JinRiJian-"+identity,out var owns);
        if(!owns){Signal(identity);return 0;}
        try
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var repo=new WorkRepository(Path.Combine(directory,"daily.db"));var service=new WorkService(repo,new SystemClock());
            Ui.ApplyTheme(repo.LoadSettings().Theme);
            var note=new NoteWindow(service,directory,smoke);app.MainWindow=note;
            app.DispatcherUnhandledException+=(_,e)=>{Log(directory,e.Exception);e.Handled=true;if(smoke){app.Shutdown(1);return;}MessageBox.Show("操作未能完成，数据仍保留。\n"+e.Exception.Message,"今日笺",MessageBoxButton.OK,MessageBoxImage.Warning);};
            note.Show();
            if(smoke)app.Dispatcher.BeginInvoke(new Action(async()=>{var result=await WindowsSmoke.Run(note,Arg("--test-report")??Path.Combine(directory,"windows-smoke.json"));note.ExitForTests();app.Shutdown(result);}),DispatcherPriority.ApplicationIdle);
            _=Listen(identity,note);
            var code=app.Run();note.Dispose();return code;
        }
        catch(Exception ex){Log(directory,ex);if(!smoke)MessageBox.Show("今日笺未能启动：\n"+ex.Message,"今日笺",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
        finally{mutex.ReleaseMutex();}
    }
    private static void Signal(string name)
    {
        try{using var pipe=new NamedPipeClientStream(".","JinRiJian-"+name,PipeDirection.Out);pipe.Connect(3000);using var writer=new StreamWriter(pipe);writer.WriteLine("SHOW");}catch(IOException){}catch(TimeoutException){}
    }
    private static async Task Listen(string name,NoteWindow note)
    {
        while(!note.Exiting)
        {
            try{await using var pipe=new NamedPipeServerStream("JinRiJian-"+name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();using var reader=new StreamReader(pipe);if(await reader.ReadLineAsync()=="SHOW")await note.Dispatcher.InvokeAsync(note.Reveal);}
            catch(Exception ex) when(ex is IOException or ObjectDisposedException){if(note.Exiting)return;}
        }
    }
    internal static void Log(string directory,Exception ex)
    {
        try{var path=Path.Combine(directory,"errors.log");if(File.Exists(path)&&new FileInfo(path).Length>1024*1024)File.Move(path,path+".old",true);File.AppendAllText(path,$"{DateTimeOffset.Now:O} {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n");}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
