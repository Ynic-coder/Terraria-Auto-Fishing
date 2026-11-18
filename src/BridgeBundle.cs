using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace Fishing;
static class BridgeBundle
{
    public static string Prepare(int pid)
    {
        using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("Fishing.BackgroundBundle") ?? throw new IOException("Не найден встроенный фоновый модуль.");
        using var data=new MemoryStream(); resource.CopyTo(data); byte[] bytes=data.ToArray();
        string hash=Convert.ToHexString(SHA256.HashData(bytes));
        string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerrariaFishing","bridge",hash);
        // EasyHook keeps its native loader in the target. Reuse our existing loader directory on reconnect.
        string cacheRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerrariaFishing","bridge")+Path.DirectorySeparatorChar;
        string legacy=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"bridge"));
        using(var game=System.Diagnostics.Process.GetProcessById(pid))
            foreach(System.Diagnostics.ProcessModule module in game.Modules)
                if(module.ModuleName.Equals("EasyHook32.dll",StringComparison.OrdinalIgnoreCase)) {
                    string loaded=Path.GetFullPath(Path.GetDirectoryName(module.FileName)!);
                    if(loaded.Equals(legacy,StringComparison.OrdinalIgnoreCase) || loaded.StartsWith(cacheRoot,StringComparison.OrdinalIgnoreCase)) path=loaded;
                }
        Directory.CreateDirectory(path);
        using var archive=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read);
        foreach(var entry in archive.Entries)
        {
            if(entry.FullName!=Path.GetFileName(entry.FullName)) throw new IOException("Некорректный встроенный пакет.");
            using var source=entry.Open(); using var content=new MemoryStream(); source.CopyTo(content); byte[] expected=content.ToArray();
            string destination=Path.Combine(path,entry.Name);
            if(!File.Exists(destination) || !SHA256.HashData(File.ReadAllBytes(destination)).AsSpan().SequenceEqual(SHA256.HashData(expected))) File.WriteAllBytes(destination,expected);
        }
        return Path.Combine(path,"FishingInjector.exe");
    }
}

