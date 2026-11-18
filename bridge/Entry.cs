using System;
using System.Runtime.InteropServices;
using EasyHook;
namespace FishingBridge
{
    public sealed class Entry : IEntryPoint
    {
        public Entry(RemoteHooking.IContext context, string pipe) { }
        public void Run(RemoteHooking.IContext context, string pipe)
        {
            try {
                var clsid = new Guid("90F1A06E-7712-4762-86B5-7A5EBA6BDB02");
                var iid = new Guid("90F1A06C-7712-4762-86B5-7A5EBA6BDB02");
                object instance;
                int result = CorBindToRuntimeEx("v4.0.30319", "wks", 0, ref clsid, ref iid, out instance);
                Marshal.ThrowExceptionForHR(result);
                var host=(ICLRRuntimeHost)instance;
                uint returned;
                Marshal.ThrowExceptionForHR(host.ExecuteInDefaultAppDomain(typeof(Entry).Assembly.Location, "FishingBridge.Bootstrap", "Start", pipe, out returned));
                Marshal.ReleaseComObject(instance);
            } catch { /* Connection timeout in the desktop app reports bootstrap failures. */ }
        }
        [DllImport("mscoree.dll", CharSet=CharSet.Unicode)] static extern int CorBindToRuntimeEx(string version, string flavor, uint flags, ref Guid clsid, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object host);
        [ComImport, Guid("90F1A06C-7712-4762-86B5-7A5EBA6BDB02"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ICLRRuntimeHost
        {
            [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int SetHostControl(IntPtr control); [PreserveSig] int GetCLRControl(out IntPtr control);
            [PreserveSig] int UnloadAppDomain(uint id, bool wait); [PreserveSig] int ExecuteInAppDomain(uint id, IntPtr callback, IntPtr cookie);
            [PreserveSig] int GetCurrentAppDomainId(out uint id);
            [PreserveSig] int ExecuteApplication(string app, uint manifests, IntPtr manifest, uint data, IntPtr activation, out int value);
            [PreserveSig] int ExecuteInDefaultAppDomain([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPWStr)] string type, [MarshalAs(UnmanagedType.LPWStr)] string method, [MarshalAs(UnmanagedType.LPWStr)] string argument, out uint result);
        }
    }
}
