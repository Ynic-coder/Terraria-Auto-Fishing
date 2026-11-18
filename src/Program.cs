namespace Fishing;
static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if(args.Contains("--probe-background")) {
            try {
                ApplicationConfiguration.Initialize();
                using var probeWindow=new Form { Text="Проверка фонового режима", Width=420, Height=130, StartPosition=FormStartPosition.CenterScreen };
                probeWindow.Controls.Add(new Label { Dock=DockStyle.Fill, Text="Проверяется обработчик без заброса и подсечки…", TextAlign=ContentAlignment.MiddleCenter });
                probeWindow.Show(); probeWindow.Activate(); Application.DoEvents();
                int pid=System.Diagnostics.Process.GetProcessesByName("Terraria").Single().Id;
                var client=BackgroundClient.ConnectAsync(pid).GetAwaiter().GetResult();
                try {
                    var foreground=Native.GetForegroundWindow(); bool gameForeground=Native.Foreground(pid);
                    var ping=client.SendAsync("PING").GetAwaiter().GetResult();
                    client.SendAsync("ARM").GetAwaiter().GetResult(); Thread.Sleep(300);
                    var elapsed=System.Diagnostics.Stopwatch.StartNew();
                    var result=client.SendAsync("USE Cast -1 -1").GetAwaiter().GetResult(); elapsed.Stop();
                    client.SendAsync("STOP").GetAwaiter().GetResult();
                    File.WriteAllText("background-diagnostic.txt", $"PING={ping}; guarded command={result}; response={elapsed.ElapsedMilliseconds}ms; game foreground={gameForeground}; focus preserved={foreground==Native.GetForegroundWindow()}");
                    if(result!="SKIP" || elapsed.ElapsedMilliseconds>=1500 || gameForeground) Environment.ExitCode=1;
                } finally { client.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            } catch(Exception ex) { File.WriteAllText("background-diagnostic.txt",ex.ToString()); Environment.ExitCode=1; }
            return;
        }
        if(args.Contains("--diagnose")) {
            try { using var r=new MemoryReader(System.Diagnostics.Process.GetProcessesByName("Terraria").Single().Id); File.WriteAllText("diagnostic.txt",$"Terraria {r.Version}, PID {r.Pid}\n{r.Sample()}\n"); }
            catch(Exception ex) { File.WriteAllText("diagnostic.txt",ex.ToString()); Environment.ExitCode=1; }
            return;
        }
        ApplicationConfiguration.Initialize();
        if(args.Contains("--render-ui")) {
            using var form=new MainForm(); form.Show(); Application.DoEvents(); form.Update();
            using var bitmap=new Bitmap(form.Width,form.Height); form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save("menu-preview.png"); return;
        }
        Application.Run(new MainForm());
    }
}

