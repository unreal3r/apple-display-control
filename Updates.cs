using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AppleControl {
internal sealed class Release {
    internal Version Version;internal string Url,Hash,Name;internal long Size;
}
internal static class Updates {
    internal const string CurrentVersion="0.3.0";
    const string Latest="https://api.github.com/repos/amedinafp/apple-display-control/releases/latest";
    internal static Release Parse(string json) {
        var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
        object draft,pre;if(data.TryGetValue("draft",out draft)&&(bool)draft||data.TryGetValue("prerelease",out pre)&&(bool)pre)throw new Exception("No stable release found.");
        string tag=(string)data["tag_name"];Version version;if(!Version.TryParse(tag.TrimStart('v','V'),out version))throw new Exception("The release has an unsupported version number.");
        foreach(object item in (System.Collections.IEnumerable)data["assets"]) {
            var asset=(Dictionary<string,object>)item;string name=(string)asset["name"];
            if(!string.Equals(name,"Apple.Display.Control.exe",StringComparison.OrdinalIgnoreCase)&&!string.Equals(name,"Apple Display Control.exe",StringComparison.OrdinalIgnoreCase))continue;
            string url=(string)asset["browser_download_url"];Uri uri;
            if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.IsDefaultPort||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!="")throw new Exception("The update download address is not valid.");
            string[] parts=uri.AbsolutePath.Split('/');
            if(parts.Length!=7||parts[1]==""||parts[2]!="apple-display-control"||parts[3]!="releases"||parts[4]!="download"||Uri.UnescapeDataString(parts[5])!=tag||Uri.UnescapeDataString(parts[6])!=name)throw new Exception("The update download is not an asset of this app release.");
            object digest;string hash=asset.TryGetValue("digest",out digest)?digest as string:null;
            if(hash==null||!System.Text.RegularExpressions.Regex.IsMatch(hash,@"^sha256:[a-fA-F0-9]{64}$"))throw new Exception("GitHub did not supply a verification hash for this update.");
            long size=Convert.ToInt64(asset["size"]);if(size<=0||size>25*1024*1024)throw new Exception("The update size is not valid.");
            return new Release {Version=version,Url=url,Name=name,Hash=hash.Substring(7),Size=size};
        }
        throw new Exception("This release has no compatible Windows app download.");
    }
    static WebClient Client() {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        var client=new WebClient();client.Headers[HttpRequestHeader.UserAgent]="Apple-Display-Control/"+CurrentVersion;client.Headers[HttpRequestHeader.Accept]="application/vnd.github+json";return client;
    }
    internal static async Task<T> Timed<T>(Task<T> operation,WebClient client,int milliseconds){if(await Task.WhenAny(operation,Task.Delay(milliseconds))!=operation){client.CancelAsync();throw new TimeoutException("GitHub did not respond in time. Try again later.");}return await operation;}
    internal static async Task<Release> Check() {using(var client=Client())return Parse(await Timed(client.DownloadStringTaskAsync(Latest),client,15000));}
    internal static void Verify(string path,Release release) {
        if(new FileInfo(path).Length!=release.Size)throw new Exception("The update download is incomplete.");
        string hash;using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
        if(!string.Equals(hash,release.Hash,StringComparison.OrdinalIgnoreCase))throw new Exception("The update could not be verified. Your current app has not been changed.");
    }
    internal static async Task<string> Download(Release release,Action<int> progress) {
        string folder=Path.Combine(App.Folder,".updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"Apple.Display.Control.exe");
        try {
            using(var client=Client()){client.Headers.Remove(HttpRequestHeader.Accept);client.DownloadProgressChanged+=(s,e)=>{if(e.BytesReceived>release.Size)client.CancelAsync();else progress(e.ProgressPercentage);};var download=client.DownloadFileTaskAsync(release.Url,path);if(await Task.WhenAny(download,Task.Delay(120000))!=download){client.CancelAsync();throw new TimeoutException("The update download timed out. Try again later.");}await download;}
            Verify(path,release);return path;
        }catch{try{File.Delete(path);Directory.Delete(folder);}catch{}throw;}
    }
    // CommandLineToArgvW-compatible quoting; arguments never go through a shell.
    internal static string Quote(string value){
        var result=new System.Text.StringBuilder("\"");int slashes=0;
        foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){result.Append('\\',slashes*2+1);result.Append(c);}else{result.Append('\\',slashes);result.Append(c);}slashes=0;}
        result.Append('\\',slashes*2);result.Append('"');return result.ToString();
    }
    internal static void StartInstaller(string staged,Release release) {
        ValidatePaths(staged,Application.ExecutablePath);
        using(var current=Process.GetCurrentProcess()) {
            string args="--apply-update "+current.Id+" "+current.StartTime.ToUniversalTime().Ticks+" "+Quote(Application.ExecutablePath)+" "+release.Hash;
            Process.Start(new ProcessStartInfo(staged,args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=App.Folder});
        }
    }
    internal static int Apply(string[] args) {
        string target=null,backup=null,next=null;bool validated=false,replaced=false;
        try {
            if(args.Length!=5)return 2;int pid=int.Parse(args[1]);long start=long.Parse(args[2]);target=Path.GetFullPath(args[3]);backup=target+".previous";
            string self=Path.GetFullPath(Application.ExecutablePath);ValidatePaths(self,target);
            if(!System.Text.RegularExpressions.Regex.IsMatch(args[4],@"^[a-fA-F0-9]{64}$"))throw new Exception("Invalid update hash.");
            Verify(self,new Release{Size=new FileInfo(self).Length,Hash=args[4]});validated=true;
            Process parent=null;try{parent=Process.GetProcessById(pid);}catch(ArgumentException){}
            if(parent!=null)using(parent){
                if(!parent.HasExited){
                    if(parent.StartTime.ToUniversalTime().Ticks!=start||!string.Equals(parent.MainModule.FileName,target,StringComparison.OrdinalIgnoreCase))throw new Exception("The running app does not match this update.");
                    if(!parent.WaitForExit(30000))throw new Exception("The app did not exit in time. Update cancelled.");
                }
            }
            next=Path.Combine(Path.GetDirectoryName(target),"AppleDisplayControl-"+Guid.NewGuid().ToString("N")+".tmp");
            using(var input=File.OpenRead(self))using(var output=new FileStream(next,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
            for(int attempt=0;;attempt++)try{File.Replace(next,target,backup,true);replaced=true;break;}catch(IOException){if(attempt>=39)throw;Thread.Sleep(100);}
            Process.Start(new ProcessStartInfo(target,"--tray"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(target)});
            return 0;
        }catch(Exception ex){
            App.Log("Update installation failed: "+ex);
            if(replaced)try{File.Replace(backup,target,null,true);}catch(Exception restore){App.Log("Update recovery: "+restore.Message);}
            if(validated&&File.Exists(target))try{Process.Start(new ProcessStartInfo(target,"--tray"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});}catch{}
            return 1;
        }finally{if(next!=null&&File.Exists(next))try{File.Delete(next);}catch{}}
    }
    internal static void ValidatePaths(string staged,string target){
        string name=Path.GetFileName(target);if(name!="Apple Display Control.exe"&&name!="Apple.Display.Control.exe")throw new Exception("Keep the app named Apple Display Control.exe or Apple.Display.Control.exe to use in-app updates.");
        string folder=Path.GetDirectoryName(target),root=Path.Combine(folder,".updates"),stage=Path.GetDirectoryName(staged);Guid id;
        if(!string.Equals(Path.GetDirectoryName(stage),root,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(stage),"N",out id)||Path.GetFileName(staged)!="Apple.Display.Control.exe")throw new Exception("Invalid update staging location.");
        foreach(string path in new[]{folder,root,stage,staged,target,target+".previous"})if((File.Exists(path)||Directory.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new Exception("Updates cannot replace linked files or folders.");
        if(!File.Exists(target))throw new Exception("The installed app was not found.");
    }
}

internal sealed class UpdateWindow : Form {
    readonly Label message=new Label();readonly Button install=new Button();readonly ProgressBar progress=new ProgressBar();readonly Button close=new Button();Release release;bool downloading;
    internal bool RestartRequested;
    internal UpdateWindow() {
        SuspendLayout();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;DoubleBuffered=true;
        Text="App updates";Font=new Font("Segoe UI",10);ClientSize=new Size(440,210);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        var heading=new Label{Text="Apple Display Control",Font=new Font("Segoe UI",14,FontStyle.Bold)};heading.SetBounds(20,18,400,30);Controls.Add(heading);
        message.SetBounds(20,58,400,57);message.Text="Checking GitHub for updates…";Controls.Add(message);
        progress.SetBounds(20,123,400,12);progress.Visible=false;Controls.Add(progress);
        install.Text="Download and restart";install.SetBounds(20,157,235,33);install.Enabled=false;Controls.Add(install);
        close.Text="Close";close.SetBounds(325,157,95,33);close.Click+=(s,e)=>Close();Controls.Add(close);
        Shown+=async(s,e)=>{try{release=await Updates.Check();if(IsDisposed)return;bool newer=release.Version>new Version(Updates.CurrentVersion);message.Text=newer?"Version "+release.Version+" is available.\nInstalled: "+Updates.CurrentVersion:"You’re up to date.\nInstalled: "+Updates.CurrentVersion;install.Enabled=newer;}catch(Exception ex){if(!IsDisposed)message.Text="Could not check for updates.\n"+ex.Message;}};
        install.Click+=async(s,e)=>{
            downloading=true;install.Enabled=close.Enabled=false;progress.Visible=true;
            try{message.Text="Downloading and verifying version "+release.Version+"…";string path=await Updates.Download(release,n=>{if(!IsDisposed)progress.Value=Math.Max(0,Math.Min(100,n));});Updates.StartInstaller(path,release);RestartRequested=true;downloading=false;Close();}
            catch(Exception ex){message.Text=ex.Message;install.Enabled=true;close.Enabled=true;downloading=false;App.Log("Update download: "+ex.Message);}
        };
        FormClosing+=(s,e)=>{if(downloading)e.Cancel=true;};
        Theme.Apply(this);
        ResumeLayout(false);
    }
}
}
