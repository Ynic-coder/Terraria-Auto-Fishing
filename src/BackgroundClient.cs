using System.Diagnostics;
using System.IO.Pipes;

namespace Fishing;
sealed class BackgroundClient : IAsyncDisposable
{
    readonly NamedPipeClientStream pipe;
    readonly StreamReader reader;
    readonly StreamWriter writer;
    readonly SemaphoreSlim gate = new(1,1);
    BackgroundClient(NamedPipeClientStream pipe) { this.pipe=pipe; reader=new(pipe); writer=new(pipe){AutoFlush=true}; }
    public static async Task<BackgroundClient> ConnectAsync(int pid)
    {
        string name=$"TerrariaFishing-{pid}-{Guid.NewGuid():N}";
        string helper=BridgeBundle.Prepare(pid);
        if(!File.Exists(helper)) throw new IOException("Не удалось подготовить фоновый модуль.");
        using var process=Process.Start(new ProcessStartInfo(helper) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardError=true, ArgumentList={pid.ToString(),name} }) ?? throw new IOException("Не удалось запустить модуль подключения.");
        var error=process.StandardError.ReadToEndAsync();
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15))) await process.WaitForExitAsync(timeout.Token);
        if(process.ExitCode!=0) throw new IOException("Подключение фонового модуля: "+await error);
        var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
        BackgroundClient? client=null;
        try {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await pipe.ConnectAsync(timeout.Token); client=new(pipe);
            string? ready=await client.reader.ReadLineAsync(timeout.Token);
            if(ready!="READY") throw new IOException("Фоновый модуль не запустился. См. %TEMP%/TerrariaFishing-bridge-"+pid+".log");
            return client;
        } catch { pipe.Dispose(); throw; }
    }
    public async Task<string> SendAsync(string command)
    {
        await gate.WaitAsync();
        try {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await writer.WriteLineAsync(command.AsMemory(),timeout.Token);
            return await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Фоновый модуль отключён.");
        } finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        try { if(pipe.IsConnected) await SendAsync("DETACH"); } catch { }
        try { writer.Dispose(); } catch { } try { reader.Dispose(); } catch { } pipe.Dispose();
    }
}




