using System;
using System.IO;
using System.Diagnostics;
using EasyHook;
class Program
{
    static int Main(string[] args)
    {
        try {
            int pid = int.Parse(args[0]);
            using(var p = Process.GetProcessById(pid))
                if(p.ProcessName != "Terraria" || p.MainModule.FileVersionInfo.FileVersion != "1.4.5.8") throw new Exception("Поддерживается Terraria 1.4.5.8.");
            string payload = typeof(FishingBridge.Entry).Assembly.Location;
            RemoteHooking.Inject(pid, InjectionOptions.DoNotRequireStrongName | InjectionOptions.NoService | InjectionOptions.NoWOW64Bypass, payload, null, args[1]);
            return 0;
        } catch(Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
    }
}

