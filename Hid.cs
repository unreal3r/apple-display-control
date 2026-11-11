using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AppleControl {
internal static class Hid {
    internal const int Success=0x00110000;
    [StructLayout(LayoutKind.Sequential)] internal struct DeviceData {
        public int Size; public Guid Guid; public int DevInst; public IntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Guid; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] internal struct Caps {
        public ushort Usage, Page, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort Links, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [StructLayout(LayoutKind.Explicit, Size=72)] internal struct Value {
        [FieldOffset(0)] public ushort Page;
        [FieldOffset(2)] public byte Report;
        [FieldOffset(6)] public ushort Link;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(18)] public ushort Bits;
        [FieldOffset(20)] public ushort Count;
        [FieldOffset(40)] public int Min;
        [FieldOffset(44)] public int Max;
        [FieldOffset(56)] public ushort Usage;
        [FieldOffset(58)] public ushort UsageMax;
        public bool Matches(ushort page, ushort usage) { return Page==page && (IsRange==0 ? Usage==usage : Usage<=usage && usage<=UsageMax); }
    }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid guid,IntPtr enumerator,IntPtr window,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr data,ref Guid guid,uint index,ref DeviceData item);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set,ref DeviceData item,IntPtr detail,int size,out int required,ref DeviceData data);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDevicePropertyW(IntPtr set,ref DeviceData data,ref PropertyKey key,out uint type,byte[] buffer,uint length,out uint required,uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint mode,uint flags,IntPtr template);
    [DllImport("hid.dll",SetLastError=true)] static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,out Caps caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type,[Out] Value[] caps,ref ushort count,IntPtr data);
    [DllImport("hid.dll",SetLastError=true)] static extern bool HidD_GetFeature(SafeFileHandle handle,[In,Out] byte[] report,int length);
    [DllImport("hid.dll",SetLastError=true)] static extern bool HidD_SetFeature(SafeFileHandle handle,byte[] report,int length);
    [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int type,ushort page,ushort link,ushort usage,out uint value,IntPtr data,byte[] report,int length);
    [DllImport("hid.dll")] static extern int HidP_SetUsageValue(int type,ushort page,ushort link,ushort usage,uint value,IntPtr data,[In,Out] byte[] report,int length);
    [DllImport("hid.dll")] static extern int HidP_GetUsageValueArray(int type,ushort page,ushort link,ushort usage,[Out] byte[] value,ushort valueLength,IntPtr data,byte[] report,int length);
    internal static void Check(int status) { if(status!=Success) throw new Exception("Display communication failed (0x"+status.ToString("X8")+")."); }
    internal static void Check(bool ok) { if(!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    internal sealed class Endpoint : IDisposable {
        SafeFileHandle handle; IntPtr data;
        internal Caps Caps; internal Value[] Values;
        internal Endpoint(string path) {
            handle=CreateFileW(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
            if(handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            try {
                Check(HidD_GetPreparsedData(handle,out data)); Check(HidP_GetCaps(data,out Caps));
                ushort count=Caps.FeatureValues; Values=new Value[count];
                if(count>0) Check(HidP_GetValueCaps(2,Values,ref count,data));
            } catch { Dispose(); throw; }
        }
        internal Value? Find(ushort page,ushort usage) { foreach(Value v in Values) if(v.Matches(page,usage)) return v; return null; }
        byte[] ReadReport(Value cap) { byte[] b=new byte[Caps.FeatureLength]; b[0]=cap.Report; Check(HidD_GetFeature(handle,b,b.Length)); return b; }
        internal uint Read(ushort page,ushort usage) { Value cap=Required(page,usage); byte[] b=ReadReport(cap); uint value; Check(HidP_GetUsageValue(2,page,cap.Link,usage,out value,data,b,b.Length)); return value; }
        internal Value Required(ushort page,ushort usage) { Value? cap=Find(page,usage); if(!cap.HasValue) throw new Exception("This display does not expose that control."); return cap.Value; }
        internal void Write(ushort page,ushort usage,uint value,bool preserve) {
            Value cap=Required(page,usage);
            if(cap.Min<0 || value<cap.Min || value>cap.Max) throw new ArgumentOutOfRangeException("value");
            byte[] b=preserve ? ReadReport(cap) : new byte[Caps.FeatureLength]; b[0]=cap.Report;
            Check(HidP_SetUsageValue(2,page,cap.Link,usage,value,data,b,b.Length)); Check(HidD_SetFeature(handle,b,b.Length));
        }
        internal string ReadString(ushort usage) {
            Value cap=Required(0xFF20,usage); byte[] b=ReadReport(cap);
            int length=(cap.Bits*cap.Count+7)/8;
            if(length<=0 || length>8192) return "";
            byte[] value=new byte[length]; Check(HidP_GetUsageValueArray(2,0xFF20,cap.Link,usage,value,(ushort)length,data,b,b.Length));
            return Encoding.Unicode.GetString(value).Split('\0')[0].Trim();
        }
        public void Dispose() { if(data!=IntPtr.Zero) { HidD_FreePreparsedData(data); data=IntPtr.Zero; } if(handle!=null) { handle.Dispose(); handle=null; } }
    }
    internal sealed class Display {
        internal string Id, Name, BrightnessPath, PresetPath;
        internal int Min,Max; internal uint Raw;
        internal List<Preset> Presets=new List<Preset>();
        internal int? Active;
        internal int Percent { get { return (int)Math.Round(100.0*(Raw-Min)/(Max-Min)); } }
        public override string ToString() { return Name; }
        internal void Refresh() { using(var e=new Endpoint(BrightnessPath)) Raw=e.Read(0x82,0x10); }
        internal void RefreshActive() {if(PresetPath!=null)using(var e=new Endpoint(PresetPath))Active=(int)e.Read(0xFF20,3);}
        internal void SetBrightness(int percent) {
            percent=Math.Max(0,Math.Min(100,percent));
            uint wanted=(uint)Math.Round(Min+(Max-Min)*percent/100.0);
            using(var e=new Endpoint(BrightnessPath)) { e.Write(0x82,0x10,wanted,true); System.Threading.Thread.Sleep(80); Raw=e.Read(0x82,0x10); }
            if(Math.Abs((long)Raw-wanted)>Math.Max(1,(Max-Min)/200)) throw new Exception("The display did not keep the brightness setting. Check its reference preset and Windows HDR settings.");
        }
        internal void LoadPresets() {
            Presets.Clear(); if(PresetPath==null) return;
            using(var e=new Endpoint(PresetPath)) {
                try { Active=(int)e.Read(0xFF20,3); } catch { Active=null; }
                var cursor=e.Required(0xFF20,4);
                int bound=Math.Min(128,Math.Max(0,cursor.Max));
                for(int i=0;i<=bound;i++) {
                    e.Write(0xFF20,4,(uint)i,false);
                    if(e.Read(0xFF20,6)==0) break;
                    string name=e.ReadString(8); if(name.Length==0) break;
                    string description=""; try { description=e.ReadString(9); } catch { }
                    Presets.Add(new Preset {Index=i,Name=name,Description=description});
                }
            }
        }
        internal void SetPreset(int index) {
            if(!Presets.Exists(p=>p.Index==index)) throw new Exception("Unknown reference preset.");
            using(var e=new Endpoint(PresetPath)) e.Write(0xFF20,3,(uint)index,false);
            Active=index;
        }
    }
    internal sealed class Preset {
        internal int Index; internal string Name,Description;
        internal bool Adjustable { get { return Name.StartsWith("Apple ",StringComparison.OrdinalIgnoreCase) || Name.StartsWith("Pro Display XDR",StringComparison.OrdinalIgnoreCase); } }
        public override string ToString() { return Name; }
    }
    internal static List<Display> Discover() {
        Guid guid; HidD_GetHidGuid(out guid);
        IntPtr set=SetupDiGetClassDevsW(ref guid,IntPtr.Zero,IntPtr.Zero,18);
        if(set==new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var displays=new List<Display>(); var presetPaths=new Dictionary<string,string>();
        try {
            for(uint i=0;;i++) {
                var item=new DeviceData {Size=Marshal.SizeOf(typeof(DeviceData))};
                if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref item)) { int error=Marshal.GetLastWin32Error(); if(error==259) break; throw new Win32Exception(error); }
                var dev=new DeviceData {Size=Marshal.SizeOf(typeof(DeviceData))};
                int size; SetupDiGetDeviceInterfaceDetailW(set,ref item,IntPtr.Zero,0,out size,ref dev);
                if(size<8) continue; IntPtr detail=Marshal.AllocHGlobal(size);
                try {
                    Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                    Check(SetupDiGetDeviceInterfaceDetailW(set,ref item,detail,size,out size,ref dev));
                    string path=Marshal.PtrToStringUni(IntPtr.Add(detail,4)); string lower=path.ToLowerInvariant();
                    string name=lower.Contains("vid_05ac&pid_1116")?"Studio Display XDR":lower.Contains("vid_05ac&pid_1114")?"Studio Display":lower.Contains("vid_05ac&pid_1118")?"Studio Display (Gen 2)":null;
                    if(name==null) continue;
                    var key=new PropertyKey {Guid=new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"),Id=2};
                    byte[] id=new byte[16]; uint type,required;
                    string container=SetupDiGetDevicePropertyW(set,ref dev,ref key,out type,id,16,out required,0) && new Guid(id)!=Guid.Empty ? new Guid(id).ToString() : null;
                    try {
                        using(var e=new Endpoint(path)) {
                            if(e.Find(0xFF20,3).HasValue && container!=null) presetPaths[container]=path;
                            Value? brightness=e.Find(0x82,0x10);
                            if(brightness.HasValue && brightness.Value.Min>=0 && brightness.Value.Max>brightness.Value.Min) {
                                displays.Add(new Display {Id=container??path,Name=name,BrightnessPath=path,Min=brightness.Value.Min,Max=brightness.Value.Max,Raw=e.Read(0x82,0x10)});
                            }
                        }
                    } catch(Exception ex) { App.Log("Probe: "+ex.Message); }
                } finally { Marshal.FreeHGlobal(detail); }
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
        foreach(var d in displays) { string path; if(presetPaths.TryGetValue(d.Id,out path)) d.PresetPath=path; }
        var counts=new Dictionary<string,int>();foreach(var d in displays){int n;counts.TryGetValue(d.Name,out n);counts[d.Name]=n+1;}
        var sequence=new Dictionary<string,int>();foreach(var d in displays){string name=d.Name;if(counts[name]>1){int n;sequence.TryGetValue(name,out n);sequence[name]=++n;d.Name+=" ("+n+")";}}
        return displays;
    }
}
}
