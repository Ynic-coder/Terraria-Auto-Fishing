using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;

namespace Fishing;



sealed class MemoryReader : IDisposable
{
    readonly IntPtr handle;
    readonly Dictionary<string, ulong> statics = new();
    readonly Dictionary<string, Dictionary<string, int>> fields = new();
    readonly Dictionary<string, uint> tables = new();
    public int Pid { get; }
    public string Version { get; }
    public MemoryReader(int pid)
    {
        Pid = pid;
        using var process = Process.GetProcessById(pid);
        Version = process.MainModule?.FileVersionInfo.FileVersion ?? "?";
        handle = Native.OpenProcess(0x1010, false, pid);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Нет доступа к процессу Terraria.");
        try
        {
            // A snapshot keeps CLR metadata discovery consistent without holding the game suspended.
            using var target = DataTarget.CreateSnapshotAndAttach(pid);
            using var runtime = target.ClrVersions.Single().CreateRuntime();
            var module = runtime.EnumerateModules().First(m => m.Name != null && Path.GetFileName(m.Name).Equals("Terraria.exe", StringComparison.OrdinalIgnoreCase));
            var main = module.GetTypeByName("Terraria.Main") ?? throw new InvalidOperationException("Тип Terraria.Main не найден.");
            var domain = runtime.AppDomains.First(d => d.Modules.Any(m => m.Address == module.Address));
            foreach (var name in new[] { "myPlayer", "player", "projectile", "gameMenu", "gamePaused", "playerInventory", "drawingPlayerChat", "editSign", "editChest", "mapFullscreen" })
                statics[name] = (main.GetStaticFieldByName(name) ?? throw new InvalidOperationException("Нет поля Main." + name)).GetAddress(domain);
            foreach (var name in new[] { "Player", "Projectile", "Item" })
            {
                var type = module.GetTypeByName("Terraria." + name) ?? throw new InvalidOperationException("Нет типа " + name);
                tables[name] = checked((uint)type.MethodTable);
                fields[name] = new();
                for (var t = type; t != null; t = t.BaseType)
                    foreach (var f in t.Fields) fields[name].TryAdd(f.Name!, f.Offset + 4);
            }
            // selectedItem became a property in newer Terraria builds.
            if (!fields["Player"].ContainsKey("selectedItem"))
                fields["Player"]["selectedItem"] = fields["Player"]["selectedItemState"] + (module.GetTypeByName("Terraria.Player")!.GetFieldByName("selectedItemState")!.Type!.GetFieldByName("selected")!.Offset);
            foreach (var (type, names) in new Dictionary<string, string[]> {
                ["Player"] = ["active", "dead", "name", "inventory", "selectedItem", "itemAnimation", "itemTime", "mouseInterface"],
                ["Projectile"] = ["active", "bobber", "owner", "ai", "localAI"],
                ["Item"] = ["fishingPole", "bait", "stack"] })
                foreach (var name in names) _ = fields[type][name];
        }
        catch { Native.CloseHandle(handle); throw; }
    }
    public byte[] Read(ulong address, int size)
    {
        if (address < 0x10000 || address > uint.MaxValue || size < 0 || size > 65536) throw new IOException("Некорректный адрес игры.");
        var data = new byte[size];
        if (!Native.ReadProcessMemory(handle, new IntPtr(unchecked((int)address)), data, size, out var read) || read.ToInt64() != size)
            throw new IOException("Не удалось прочитать память. Игра закрыта или состояние изменилось.");
        return data;
    }
    uint U32(ulong a) => BitConverter.ToUInt32(Read(a, 4));
    int I32(ulong a) => unchecked((int)U32(a));
    bool Bool(ulong a) => Read(a, 1)[0] != 0;
    ulong At(ulong obj, string type, string name) => obj + (ulong)fields[type][name];
    uint Ref(ulong obj, string type, string name) => U32(At(obj, type, name));
    int Int(ulong obj, string type, string name) => I32(At(obj, type, name));
    bool Flag(ulong obj, string type, string name) => Bool(At(obj, type, name));
    bool Valid(uint obj, string type) => obj >= 0x10000 && U32(obj) == tables[type];
    uint[] Array(uint obj, int limit)
    {
        int count = I32(obj + 4UL);
        if (count < 0 || count > limit) throw new IOException("Массив игры изменился. Повторите подключение.");
        var bytes = Read(obj + 8UL, count * 4);
        var refs = new uint[count]; Buffer.BlockCopy(bytes, 0, refs, 0, bytes.Length); return refs;
    }
    string Name(uint address)
    {
        if (address == 0) return "Игрок";
        var length = Math.Clamp(I32(address + 4UL), 0, 64);
        return System.Text.Encoding.Unicode.GetString(Read(address + 8UL, length * 2));
    }
    public FishState Sample()
    {
        if (Bool(statics["gameMenu"])) return new(true, false, false, false, false, false, 0, 0, false, 0, 0, 0, "—");
        int id = I32(statics["myPlayer"]);
        var players = Array(U32(statics["player"]), 512);
        if (id < 0 || id >= players.Length || !Valid(players[id], "Player")) throw new IOException("Игрок ещё не загружен.");
        uint player = players[id];
        if (!Flag(player, "Player", "active")) throw new IOException("Игрок не активен.");
        var inv = Array(Ref(player, "Player", "inventory"), 256);
        int selected = Int(player, "Player", "selectedItem");
        bool rod = selected >= 0 && selected < inv.Length && Valid(inv[selected], "Item") && Int(inv[selected], "Item", "fishingPole") > 0;
        int bait = 0;
        for (int i = 0; i < Math.Min(inv.Length, 58); i++)
            if ((i < 50 || i >= 54) && Valid(inv[i], "Item") && Int(inv[i], "Item", "bait") > 0)
                bait += Math.Max(0, Int(inv[i], "Item", "stack"));
        int bobbers = 0, catchId = 0; bool bite = false; float ai0 = 0, ai1 = 0;
        foreach (uint p in Array(U32(statics["projectile"]), 2048))
        {
            if (p == 0 || !Flag(p, "Projectile", "active") || !Valid(p, "Projectile") || !Flag(p, "Projectile", "bobber") || Int(p, "Projectile", "owner") != id) continue;
            bobbers++;
            var ai = Read(Ref(p, "Projectile", "ai") + 8UL, 8);
            float a0 = BitConverter.ToSingle(ai, 0), a1 = BitConverter.ToSingle(ai, 4);
            float result = BitConverter.ToSingle(Read(Ref(p, "Projectile", "localAI") + 12UL, 4));
            if (!float.IsFinite(a0) || !float.IsFinite(a1) || !float.IsFinite(result)) throw new IOException("Нестабильное состояние поплавка.");
            ai0 = a0; ai1 = a1;
            // Same checks as Player.ItemCheck_PullFishingBobbers in installed 1.4.5.8.
            if (a0 == 0 && a1 < 0 && result != 0) { bite = true; catchId = (int)result; }
        }
        // Recheck roots to reject a sample that crossed a world/player transition.
        if (Bool(statics["gameMenu"]) || I32(statics["myPlayer"]) != id) throw new IOException("Смена мира.");
        return new(false, Bool(statics["gamePaused"]), Flag(player, "Player", "dead"), Bool(statics["playerInventory"]) || Bool(statics["drawingPlayerChat"]) || Bool(statics["editSign"]) || Bool(statics["editChest"]) || Bool(statics["mapFullscreen"]) || Flag(player, "Player", "mouseInterface"),
            Int(player, "Player", "itemAnimation") > 0 || Int(player, "Player", "itemTime") > 0, rod, bait, bobbers, bite, catchId, ai0, ai1, Name(Ref(player, "Player", "name")));
    }
    public void Dispose() => Native.CloseHandle(handle);
}

static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] data, int size, out IntPtr read);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public MOUSEINPUT mouse; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint data, flags, time; public UIntPtr extra; }
    internal static bool Foreground(int pid) { GetWindowThreadProcessId(GetForegroundWindow(), out uint active); return active == pid; }
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    internal static bool PointOverGame(Point point, int pid) { GetWindowThreadProcessId(WindowFromPoint(point), out uint owner); return owner == pid; }
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    internal static Point GamePoint(Point point, int pid) { using var p = Process.GetProcessById(pid); if (!ScreenToClient(p.MainWindowHandle, ref point)) throw new IOException("Окно игры недоступно."); return point; }
    internal static Point ScreenPoint(Point point, int pid) { using var p = Process.GetProcessById(pid); if (!ClientToScreen(p.MainWindowHandle, ref point)) throw new IOException("Окно игры недоступно."); return point; }
    internal static void Mouse(bool down)
    {
        var input = new INPUT { type = 0, mouse = new MOUSEINPUT { flags = down ? 2u : 4u } };
        if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) != 1) throw new IOException("Windows не приняла клик. Запустите игру и меню с одинаковыми правами.");
    }
}




