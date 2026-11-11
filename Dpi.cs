using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AppleControl {
internal static class Dpi {
    // Keep the normal .NET configuration beside a renamed/downloaded EXE too.
    // Run before any WinForms types initialize; the updater stays single-file.
    internal static void Initialize(){
        string path=Process.GetCurrentProcess().MainModule.FileName+".config";
        if(File.Exists(path))return;
        try{using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("AppleControl.Dpi.config"))using(var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read))source.CopyTo(target);}
        catch(IOException){if(!File.Exists(path))throw;}
    }
    [DllImport("user32.dll")]static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")]static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
    internal static bool PerMonitorV2 {get{return AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(),new IntPtr(-4));}}
}
}
