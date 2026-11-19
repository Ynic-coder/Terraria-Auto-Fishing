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
        static System.Collections.Generic.HashSet<int> allowedItems;
        static int aimMode, aimDistance=400, aimVertical;
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
                                                        if(line=="TARGET") {
                                string target="SKIP";
                                OnGameThread(()=>{ if(!B(main,"gameMenu") && aimMode!=0) { var p=((Array)Get(main,"player")).GetValue(I(main,"myPlayer")); var r=new Request(); SetRelativeAim(p,r); target="TARGET "+r.X+" "+r.Y; r.Done.Dispose(); } });
                                writer.WriteLine(target);continue;
                            }
                            if(line=="PING") { writer.WriteLine("OK"); continue; }
                            if(line=="ARM") { enabled=true; writer.WriteLine("OK"); continue; }
                            if(line=="STOP") { Disable(); writer.WriteLine("OK"); continue; }
                            if(line=="DETACH") { Disable(); OnGameThread(()=>harmony.UnpatchAll(Owner)); harmony=null; lock(gate) attached=false; writer.WriteLine("OK"); break; }
                                                        if(line.StartsWith("FILTER ")) {
                                string values=line.Substring(7);
                                var parsed=new System.Collections.Generic.HashSet<int>(); bool valid=true;
                                if(values!="ALL") foreach(string part in values.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries)) { if(!int.TryParse(part,out int id)||id<=0) {valid=false;break;} parsed.Add(id); }
                                if(valid) allowedItems=values=="ALL"?null:parsed;
                                writer.WriteLine(valid?"OK":"ERROR filter"); continue;
                            }
                            var words=line.Split(' ');
                            if(words.Length==4 && words[0]=="AIM" && int.TryParse(words[1],out int mode) && int.TryParse(words[2],out int distance) && int.TryParse(words[3],out int vertical) && mode>=0 && mode<=2 && distance>=40 && distance<=1200 && vertical>=-600 && vertical<=600) {
                                aimMode=mode;aimDistance=distance;aimVertical=vertical;writer.WriteLine("OK");continue;
                            }
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
                int count=0; bool bite=false, rejected=false; var accepted=allowedItems;
                foreach(var p in (Array)Get(main,"projectile")) {
                    if(p==null || !B(projectileType,"active",p) || !B(projectileType,"bobber",p) || I(projectileType,"owner",p)!=I(main,"myPlayer")) continue;
                    count++; var ai=(float[])Get(projectileType,"ai",p); var local=(float[])Get(projectileType,"localAI",p);
                    if(ai[0]==0 && ai[1]<0 && local[1]!=0) { bite=true; if(accepted!=null && (local[1]<=0 || !accepted.Contains((int)local[1]))) rejected=true; }
                }
                if(request.Kind=="Hook" ? (!bite || rejected) : count!=0) { request.Result="SKIP"; request.Done.Set(); return; }
                if(aimMode!=0) SetRelativeAim(__instance,request);
                if(request.X<0 || request.Y<0 || request.X>=I(main,"screenWidth") || request.Y>=I(main,"screenHeight")) { request.Result="SKIP"; request.Done.Set(); return; }
                __state=new Change { Player=__instance, Request=request, X=I(main,"mouseX"),Y=I(main,"mouseY"), Control=B(playerType,"controlUseItem",__instance), Release=B(playerType,"releaseUseItem",__instance), Delay=B(playerType,"delayUseItem",__instance) };
                Set(main,"mouseX",request.X); Set(main,"mouseY",request.Y);
                Set(playerType,"controlUseItem",true,__instance); Set(playerType,"releaseUseItem",true,__instance); Set(playerType,"delayUseItem",false,__instance);
            } catch { enabled=false; if(request!=null) { request.Result="SKIP"; request.Done.Set(); } Restore(__state); __state=null; }
        }
                static void SetRelativeAim(object player, Request request)
        {
            object position=Get(playerType,"position",player), screen=Get(main,"screenPosition");
            Type vector=position.GetType();
            float px=(float)F(vector,"X").GetValue(position)+I(playerType,"width",player)/2f;
            float py=(float)F(vector,"Y").GetValue(position)+I(playerType,"height",player)/2f;
            float sx=(float)F(vector,"X").GetValue(screen), sy=(float)F(vector,"Y").GetValue(screen);
            object matrix=Get(main,"GameViewMatrix"); object zoom=matrix.GetType().GetProperty("Zoom").GetValue(matrix,null);
            float zx=(float)F(vector,"X").GetValue(zoom), zy=(float)F(vector,"Y").GetValue(zoom);
            int w=I(main,"screenWidth"),h=I(main,"screenHeight");
            request.X=Math.Max(0,Math.Min(w-1,(int)(w/2f+(px-sx-w/2f)*zx)+(aimMode==1?aimDistance:-aimDistance)));
            request.Y=Math.Max(0,Math.Min(h-1,(int)(h/2f+(py-sy-h/2f)*zy)+aimVertical));
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



