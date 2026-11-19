namespace Fishing;
static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if(args.Contains("--probe-background") || args.Contains("--probe-settings")) {
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
                    var filter=client.SendAsync("FILTER 2337,2338").GetAwaiter().GetResult();
                    client.SendAsync("AIM 1 400 0").GetAwaiter().GetResult();
                    var right=client.SendAsync("TARGET").GetAwaiter().GetResult();
                    client.SendAsync("AIM 2 400 0").GetAwaiter().GetResult();
                    var left=client.SendAsync("TARGET").GetAwaiter().GetResult();
                    client.SendAsync("AIM 0 400 0").GetAwaiter().GetResult();
                    client.SendAsync("FILTER ALL").GetAwaiter().GetResult();
                    bool checkBackground=args.Contains("--probe-background");
                    if(checkBackground) { client.SendAsync("ARM").GetAwaiter().GetResult(); Thread.Sleep(300); }
                    var elapsed=System.Diagnostics.Stopwatch.StartNew();
                    var result=checkBackground ? client.SendAsync("USE Cast -1 -1").GetAwaiter().GetResult() : "NOT_REQUESTED"; elapsed.Stop();
                    client.SendAsync("STOP").GetAwaiter().GetResult();
                    File.WriteAllText("background-diagnostic.txt", $"PING={ping}; FILTER={filter}; right={right}; left={left}; guarded command={result}; response={elapsed.ElapsedMilliseconds}ms; game foreground={gameForeground}; focus preserved={foreground==Native.GetForegroundWindow()}");
                    if(filter!="OK" || !right.StartsWith("TARGET ") || !left.StartsWith("TARGET ") || (checkBackground && (result!="SKIP" || elapsed.ElapsedMilliseconds>=1500 || gameForeground))) Environment.ExitCode=1;
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
        if(args.Any(a=>a.StartsWith("--render-"))) {
            using Form form=args.Contains("--render-filter") ? new ItemFilterForm([2334,2335]) : args.Contains("--render-aim") ? new AimForm(1,400,0) : new MainForm(); form.Show(); Application.DoEvents(); form.Update();
            using var bitmap=new Bitmap(form.Width,form.Height); form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args.Contains("--render-filter") ? "filter-preview.png" : args.Contains("--render-aim") ? "aim-preview.png" : "menu-preview.png"); return;
        }
        Application.Run(new MainForm());
    }
}





