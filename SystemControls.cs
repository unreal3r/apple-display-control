using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AppleControl {
internal static class Keyboard {
    internal static string[] DeviceNames={"AppleKeyboard","AppleKeyMagic2Keyboard","AppleKeyboardInternalUSB","AppleSPIKeyboard"};
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h,uint code,byte[] input,int inputLength,byte[] output,int outputLength,out int returned,IntPtr overlapped);
    internal static string[] Services={"KeyMagic","KeyMagic2"};
    internal static Dictionary<string,byte> ReadSettings() {
        var found=new Dictionary<string,byte>();
        foreach(string service in Services) using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+service)) {
            if(key==null) continue; byte[] value=key.GetValue("OSXFnBehavior") as byte[];
            if(value!=null && value.Length==1) found[service]=value[0];
        }
        return found;
    }
    internal static string Probe() {
        var lines=new List<string>();
        foreach(var pair in ReadSettings()) lines.Add(pair.Key+" stored mode="+pair.Value);
        foreach(string name in DeviceNames) {
            using(var handle=Hid.CreateFileW(@"\\.\"+name,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                if(handle.IsInvalid) { lines.Add(name+": open error "+Marshal.GetLastWin32Error()); continue; }
                byte[] output=new byte[4]; int returned;
                bool ok=DeviceIoControl(handle,0xB4032018,null,0,output,4,out returned,IntPtr.Zero);
                lines.Add(name+": query="+ok+", bytes="+returned+", value="+BitConverter.ToString(output)+", error="+(ok?0:Marshal.GetLastWin32Error()));
            }
        }
        return string.Join(Environment.NewLine,lines);
    }
    internal static Dictionary<string,uint> ReadLive() {
        var result=new Dictionary<string,uint>();
        foreach(string name in DeviceNames) using(var handle=Hid.CreateFileW(@"\\.\"+name,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
            if(handle.IsInvalid) continue;byte[] output=new byte[4];int returned;
            if(DeviceIoControl(handle,0xB4032018,null,0,output,4,out returned,IntPtr.Zero)&&returned>=4) result[name]=BitConverter.ToUInt32(output,0);
        }
        return result;
    }
    internal static void SetLive(string name,uint mode) {
        using(var handle=Hid.CreateFileW(@"\\.\"+name,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
            if(handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            int returned;byte[] value=BitConverter.GetBytes(mode);
            Hid.Check(DeviceIoControl(handle,0xB403201C,value,4,null,0,out returned,IntPtr.Zero));
            var modes=ReadLive();if(!modes.ContainsKey(name)||modes[name]!=mode) throw new Exception("Apple keyboard driver did not retain the requested mode.");
        }
    }
    // Both the live driver and OSXFnBehavior use 0 for F1-F12, 1 for media keys.
    internal static uint ModeForStandard(bool standard){return standard?0u:1u;}
    internal static bool StandardForMode(uint mode){return mode==0;}
    internal static void Set(bool standard) {
        var found=ReadSettings(); var live=ReadLive();
        if(found.Count==0 && live.Count==0) throw new Exception("Apple keyboard driver not found. Keep the Apple keyboard driver installed.");
        var changed=new List<string>();
        try {
            foreach(var pair in found) using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+pair.Key,true)) {
                key.SetValue("OSXFnBehavior",new byte[]{(byte)ModeForStandard(standard)},RegistryValueKind.Binary); changed.Add(pair.Key);
            }
            foreach(var pair in live) SetLive(pair.Key,ModeForStandard(standard));
        } catch {
            foreach(var pair in live) try {SetLive(pair.Key,pair.Value);}catch(Exception ex){App.Log("Keyboard rollback: "+ex.Message);}
            foreach(string name in changed) using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+name,true)) key.SetValue("OSXFnBehavior",new byte[]{found[name]},RegistryValueKind.Binary);
            throw;
        }
    }
}

internal static class Hdr {
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags,ref uint paths,IntPtr pathBuffer,ref uint modes,IntPtr modeBuffer,IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(IntPtr packet);
    // Returns true if any active output uses HDR/advanced color. Conservative
    // global guard: never choose an SDR-only reference preset while HDR is on.
    internal static bool? AnyEnabled() {
        for(int retry=0;retry<3;retry++) {
            uint paths,modes; if(GetDisplayConfigBufferSizes(2,out paths,out modes)!=0) return null;
            IntPtr p=Marshal.AllocHGlobal(checked((int)paths*72)); IntPtr m=Marshal.AllocHGlobal(checked((int)modes*64));
            try {
                int result=QueryDisplayConfig(2,ref paths,p,ref modes,m,IntPtr.Zero);
                if(result==122) continue; if(result!=0) return null;
                bool unknown=false;
                for(int i=0;i<paths;i++) {
                    IntPtr packet=Marshal.AllocHGlobal(32);
                    try {
                        for(int j=0;j<32;j++) Marshal.WriteByte(packet,j,0);
                        Marshal.WriteInt32(packet,0,9); Marshal.WriteInt32(packet,4,32);
                        IntPtr target=IntPtr.Add(p,i*72+20);
                        Marshal.WriteInt64(packet,8,Marshal.ReadInt64(target)); Marshal.WriteInt32(packet,16,Marshal.ReadInt32(target,8));
                        if(DisplayConfigGetDeviceInfo(packet)!=0) { unknown=true; continue; }
                        if((Marshal.ReadInt32(packet,20)&2)!=0) return true;
                    } finally { Marshal.FreeHGlobal(packet); }
                }
                return unknown?(bool?)null:false;
            } finally { Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(m); }
        }
        return null;
    }
}
}
