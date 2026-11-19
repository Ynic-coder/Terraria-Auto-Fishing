using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

static class BuilderProgram
{
    [STAThread] static void Main(string[] args)
    {
        if (args.Contains("--self-test")) { SelfTest(); return; }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var form = new BuildForm();
        if(args.Contains("--render")) { form.Show(); Application.DoEvents(); using(var image=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height)); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"builder-preview.png")); } form.Dispose(); return; }
        Application.Run(form);
    }
    static void SelfTest()
    {
        try {
            string sdk=BuildRunner.FindSdk();
            var output=new StringBuilder();
            int code=BuildRunner.Execute("powershell.exe", "-NoProfile -NonInteractive -Command \"Write-Output 'stdout-ok'; [Console]::Error.WriteLine('stderr-ok'); exit 7\"", AppDomain.CurrentDomain.BaseDirectory, delegate(string line) { lock(output) output.AppendLine(line); });
            if(code!=7 || !output.ToString().Contains("stdout-ok") || !output.ToString().Contains("stderr-ok")) throw new Exception("Output/exit-code capture failed");
            File.WriteAllText("builder-check.txt","PASS: SDK detection, stdout, stderr, nonzero exit code.\r\n"+sdk);
        } catch(Exception e) { File.WriteAllText("builder-check.txt",e.ToString()); Environment.ExitCode=1; }
    }
}
static class BuildRunner
{
    public static string FindSdk()
    {
        var output=new StringBuilder();
        int code=Execute("dotnet","--list-sdks",AppDomain.CurrentDomain.BaseDirectory,delegate(string line) { lock(output) output.AppendLine(line); });
        bool found=output.ToString().Split('\n').Any(delegate(string line) { int major; return int.TryParse(line.Split('.')[0].Trim(),out major) && major>=9; });
        if(code!=0 || !found) throw new InvalidOperationException("Для сборки нужен .NET SDK 9 или новее. Установите SDK, затем снова откройте это окно.");
        return output.ToString();
    }
    public static int Execute(string file,string arguments,string directory,Action<string> receive)
    {
        using(var process=new Process()) {
            process.StartInfo=new ProcessStartInfo(file,arguments) { WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
            process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e) { if(e.Data!=null) receive(e.Data); };
            process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e) { if(e.Data!=null) receive(e.Data); };
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit(); return process.ExitCode;
        }
    }
}
sealed class BuildForm : Form
{
    readonly Button build=new Button(), folder=new Button(), run=new Button();
    readonly TextBox log=new TextBox(); readonly Label status=new Label(); readonly ProgressBar progress=new ProgressBar();
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    bool busy;
    string OutputFile { get { return Path.Combine(root,"release","TerrariaFishing.exe"); } }
    public BuildForm()
    {
        Text="Сборка Terraria Auto Fishing"; ClientSize=new Size(760,540); MinimumSize=new Size(650,460);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,84));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,25));
        var description=new Label { Dock=DockStyle.Fill,Text="Сборка приложения одним нажатием\r\nНужны .NET SDK 9 или новее и интернет для загрузки библиотек.\r\nПеред сборкой закройте окно авторыбалки. Terraria закрывать не нужно." };
        var buttons=new FlowLayoutPanel { Dock=DockStyle.Fill };
        build.Text="Собрать";folder.Text="Открыть папку EXE";run.Text="Запустить приложение";
        foreach(var b in new[]{build,folder,run}) { b.AutoSize=true;b.Height=32;buttons.Controls.Add(b); }
        run.Enabled=File.Exists(OutputFile);folder.Enabled=Directory.Exists(Path.Combine(root,"release"));
        log.Dock=DockStyle.Fill;log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Both;log.WordWrap=false;log.Font=new Font("Consolas",9);
        status.Text="Готово к сборке";status.Dock=DockStyle.Fill; progress.Dock=DockStyle.Fill;
        layout.Controls.Add(description);layout.Controls.Add(buttons);layout.Controls.Add(status);layout.Controls.Add(log);layout.Controls.Add(progress);Controls.Add(layout);
        build.Click+=async delegate { await Build(); };
        folder.Click+=delegate { Process.Start(new ProcessStartInfo(Path.Combine(root,"release")){UseShellExecute=true}); };
        run.Click+=delegate { if(File.Exists(OutputFile)) Process.Start(new ProcessStartInfo(OutputFile){UseShellExecute=true,WorkingDirectory=root}); };
        FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(busy) { e.Cancel=true; MessageBox.Show(this,"Дождитесь окончания сборки. Журнал обновляется в этом окне.","Сборка выполняется"); } };
    }
    void AddLine(string line)
    {
        if(IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(delegate { if(log.TextLength>200000) log.Text=log.Text.Substring(log.TextLength-100000);log.AppendText(line+Environment.NewLine); }));
    }
    async Task Build()
    {
        if(busy) return;
        busy=true;build.Enabled=run.Enabled=false;progress.Style=ProgressBarStyle.Marquee;status.Text="Проверяю SDK и файлы проекта…";log.Clear();
        try {
            string script=Path.Combine(root,"build.ps1");
            if(!File.Exists(script)) throw new IOException("Не найден build.ps1. Оставьте «Собрать.exe» в корне папки с исходниками.");
            if(File.Exists(OutputFile)) { try { using(var test=new FileStream(OutputFile,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {} } catch(IOException) { throw new IOException("Готовый EXE занят. Закройте окно авторыбалки и повторите сборку."); } }
            string sdk=await Task.Run(delegate { return BuildRunner.FindSdk(); });log.AppendText("Найден SDK:\r\n"+sdk+Environment.NewLine);status.Text="Собираю приложение…";
            Directory.CreateDirectory(Path.Combine(root,"artifacts"));string logFile=Path.Combine(root,"artifacts","build-gui.log");
            int code;
            using(var writer=new StreamWriter(logFile,false,Encoding.UTF8)) {
                code=await Task.Run(delegate { return BuildRunner.Execute(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\"",root,delegate(string line) { lock(writer) { writer.WriteLine(line);writer.Flush(); } AddLine(line); }); });
            }
            if(code!=0 || !File.Exists(OutputFile)) throw new Exception("Сборка не завершилась. Причина показана в журнале. Полный журнал: artifacts/build-gui.log");
            status.Text="Готово! release\\TerrariaFishing.exe";folder.Enabled=run.Enabled=true;
        } catch(System.ComponentModel.Win32Exception) { status.Text="Не найден .NET SDK";log.AppendText("Установите .NET SDK 9 или новее, затем повторите сборку.\r\n"); }
        catch(Exception e) { status.Text="Сборка не завершена";log.AppendText(e.Message+Environment.NewLine); }
        finally { busy=false;build.Enabled=true;progress.Style=ProgressBarStyle.Blocks;progress.Value=0; }
    }
}
