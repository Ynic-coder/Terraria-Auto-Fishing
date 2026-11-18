using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Globalization;
using System.Diagnostics;
using System.Windows.Forms;
using HarmonyLib;

namespace FishingBridge
{
    public static class Bootstrap
    {
        static Type main, playerType, projectileType, focus;
        static Harmony harmony;
        static volatile bool enabled;
        static long heartbeat;
        static readonly object gate = new object();
        static Request pending;
        static bool attached;
        const string Owner = "TerrariaFishing.Background.1";
        static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static readonly System.Collections.Generic.Dictionary<string, FieldInfo> cache = new System.Collections.Generic.Dictionary<string, FieldInfo>();
        static FieldInfo F(Type t, string name) { lock(cache) { string key=t.FullName+"::"+name; if(!cache.TryGetValue(key,out var f)) { for(var c=t;c!=null && f==null;c=c.BaseType) f=c.GetField(name,Flags); if(f==null) throw new MissingFieldException(t.FullName,name); cache[key]=f; } return f; } }
        static object Get(Type t, string n, object o=null) => F(t,n).GetValue(o);
        static void Set(Type t,string n,object value,object o=null) => F(t,n).SetValue(o,value);
        static bool B(Type t,string n,object o=null) => (bool)Get(t,n,o);
        static int I(Type t,string n,object o=null) => (int)Get(t,n,o);
        static bool Active => enabled && Stopwatch.GetTimestamp() - Interlocked.Read(ref heartbeat) < Stopwatch.Frequency*3;
        public static int Start(string pipe)
        {
            lock(gate) { if(attached) return 1; attached=true; }
            // Resolve Harmony from the bridge directory in the game's default AppDomain.
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            new Thread(()=>Serve(pipe)) { IsBackground=true, Name="Terraria fishing bridge" }.Start();
            return 0;
        }
        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name=new AssemblyName(args.Name).Name;
            if(name!="0Harmony" && name!="EasyHook") return null;
            string path=Path.Combine(Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location),name+".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
        static void OnGameThread(Action action)
        {
            var instance=Get(main,"instance");
            var window=instance.GetType().GetProperty("Window").GetValue(instance,null);
            var hwnd=(IntPtr)window.GetType().GetProperty("Handle").GetValue(window,null);
            var control=Control.FromHandle(hwnd) ?? throw new Exception("Окно игры не найдено.");
            if(control.InvokeRequired) control.Invoke(action); else action();
        }
        static void Install()
        {
            harmony=new Harmony(Owner);
            harmony.Patch(AccessTools.Method(playerType,"ItemCheck",Type.EmptyTypes), prefix:new HarmonyMethod(typeof(Bootstrap),nameof(BeforeItem)), postfix:new HarmonyMethod(typeof(Bootstrap),nameof(AfterItem)), finalizer:new HarmonyMethod(typeof(Bootstrap),nameof(FinalizeItem)));
            harmony.Patch(AccessTools.Method(focus,"UpdateFocus"), postfix:new HarmonyMethod(typeof(Bootstrap),nameof(AfterFocus)));
        }
        static void Serve(string name)
        {
            try {
                var asm=Array.Find(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetName().Name=="Terraria") ?? throw new Exception("Terraria не загружена в основном AppDomain.");
                main=asm.GetType("Terraria.Main",true); playerType=asm.GetType("Terraria.Player",true); projectileType=asm.GetType("Terraria.Projectile",true); focus=asm.GetType("Terraria.FocusHelper",true);
                // Validate every field used from a hook before changing any game method.
                foreach(string f in new[]{"gameMenu","gamePaused","playerInventory","drawingPlayerChat","editSign","editChest","mapFullscreen","ingameOptionsWindow","myPlayer","player","projectile","mouseX","mouseY","screenWidth","screenHeight"}) F(main,f);
                foreach(string f in new[]{"active","dead","mouseInterface","controlUseItem","releaseUseItem","delayUseItem","itemAnimation","itemTime","whoAmI"}) F(playerType,f);
                using(var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.None)) {
                    pipe.WaitForConnection();
                    using(var reader=new StreamReader(pipe)) using(var writer=new StreamWriter(pipe){AutoFlush=true}) {
                        OnGameThread(Install); writer.WriteLine("READY");
                        string line;
                        while((line=reader.ReadLine())!=null) {
                            Interlocked.Exchange(ref heartbeat,Stopwatch.GetTimestamp());
                            if(line=="PING") { writer.WriteLine("OK"); continue; }
                            if(line=="ARM") { enabled=true; writer.WriteLine("OK"); continue; }
                            if(line=="STOP") { Disable(); writer.WriteLine("OK"); continue; }
                            if(line=="DETACH") { Disable(); OnGameThread(()=>harmony.UnpatchAll(Owner)); harmony=null; lock(gate) attached=false; writer.WriteLine("OK"); break; }
                            var words=line.Split(' ');
                            if(words.Length==4 && words[0]=="USE" && (words[1]=="Cast" || words[1]=="Hook") && int.TryParse(words[2],NumberStyles.Integer,CultureInfo.InvariantCulture,out int x) && int.TryParse(words[3],NumberStyles.Integer,CultureInfo.InvariantCulture,out int y)) {
                                var request=new Request { Kind=words[1], X=x,Y=y, Created=Stopwatch.GetTimestamp() };
                                lock(gate) pending=request;
                                bool completed = request.Done.WaitOne(1800);
                                if(!completed) { lock(gate) { if(pending==request) pending=null; } request.Result="SKIP"; }
                                writer.WriteLine(request.Result ?? "SKIP");
                                if(completed) request.Done.Dispose();
                            } else writer.WriteLine("ERROR invalid command");
                        }
                    }
                }
            } catch(Exception ex) {
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(),"TerrariaFishing-bridge-"+Process.GetCurrentProcess().Id+".log"),ex.ToString()); } catch { }
            } finally {
                Disable();
                try { if(harmony!=null) OnGameThread(()=>harmony.UnpatchAll(Owner)); } catch { }
                lock(gate) attached=false;
            }
        }
        static void Disable() { enabled=false; lock(gate) { if(pending!=null) { pending.Result="SKIP"; pending.Done.Set(); pending=null; } } }
        public static void AfterFocus(ref bool __0)
        {
            try { if(Active && !B(focus,"IsSelectedApplication") && !B(main,"gameMenu")) __0=false; } catch { enabled=false; }
        }
        static bool Allowed(object p)
        {
            if(!Active || B(main,"gameMenu") || B(main,"gamePaused") || B(main,"playerInventory") || B(main,"drawingPlayerChat") || B(main,"editSign") || B(main,"editChest") || B(main,"mapFullscreen") || B(main,"ingameOptionsWindow")) return false;
            if(!B(playerType,"active",p) || B(playerType,"dead",p) || B(playerType,"mouseInterface",p) || I(playerType,"whoAmI",p)!=I(main,"myPlayer") || I(playerType,"itemAnimation",p)>0 || I(playerType,"itemTime",p)>0) return false;
            var inv=(Array)Get(playerType,"inventory",p); int selected=(int)playerType.GetProperty("selectedItem",Flags).GetValue(p,null);
            if(selected<0 || selected>=inv.Length) return false;
            var item=inv.GetValue(selected);
            return I(item.GetType(),"fishingPole",item)>0;
        }
        public static void BeforeItem(object __instance, out Change __state)
        {
            __state=null; Request request=null;
            try {
                lock(gate) {
                    if(pending==null || I(playerType,"whoAmI",__instance)!=I(main,"myPlayer")) return;
                    request=pending; pending=null;
                }
                if(!Allowed(__instance) || Stopwatch.GetTimestamp()-request.Created>Stopwatch.Frequency) { request.Result="SKIP"; request.Done.Set(); return; }
                int count=0; bool bite=false;
                foreach(var p in (Array)Get(main,"projectile")) {
                    if(p==null || !B(projectileType,"active",p) || !B(projectileType,"bobber",p) || I(projectileType,"owner",p)!=I(main,"myPlayer")) continue;
                    count++; var ai=(float[])Get(projectileType,"ai",p); var local=(float[])Get(projectileType,"localAI",p);
                    if(ai[0]==0 && ai[1]<0 && local[1]!=0) bite=true;
                }
                if(request.Kind=="Hook" ? !bite : count!=0) { request.Result="SKIP"; request.Done.Set(); return; }
                if(request.X<0 || request.Y<0 || request.X>=I(main,"screenWidth") || request.Y>=I(main,"screenHeight")) { request.Result="SKIP"; request.Done.Set(); return; }
                __state=new Change { Player=__instance, Request=request, X=I(main,"mouseX"),Y=I(main,"mouseY"), Control=B(playerType,"controlUseItem",__instance), Release=B(playerType,"releaseUseItem",__instance), Delay=B(playerType,"delayUseItem",__instance) };
                Set(main,"mouseX",request.X); Set(main,"mouseY",request.Y);
                Set(playerType,"controlUseItem",true,__instance); Set(playerType,"releaseUseItem",true,__instance); Set(playerType,"delayUseItem",false,__instance);
            } catch { enabled=false; if(request!=null) { request.Result="SKIP"; request.Done.Set(); } Restore(__state); __state=null; }
        }
        public static void AfterItem(Change __state) { if(__state==null) return; try { Restore(__state); __state.Request.Result="DONE"; } catch { enabled=false; __state.Request.Result="SKIP"; } finally { __state.Request.Done.Set(); } }
        public static void FinalizeItem(Exception __exception, Change __state) { if(__exception!=null && __state!=null) { try { Restore(__state); } catch {} __state.Request.Result="SKIP"; __state.Request.Done.Set(); enabled=false; } }
        static void Restore(Change c)
        {
            if(c==null) return;
            Set(main,"mouseX",c.X); Set(main,"mouseY",c.Y); Set(playerType,"controlUseItem",c.Control,c.Player); Set(playerType,"releaseUseItem",c.Release,c.Player); Set(playerType,"delayUseItem",c.Delay,c.Player);
        }
        public sealed class Change { public object Player; public Request Request; public int X,Y; public bool Control,Release,Delay; }
        public sealed class Request { public string Kind,Result; public int X,Y; public long Created; public readonly ManualResetEvent Done=new ManualResetEvent(false); }
    }
}

