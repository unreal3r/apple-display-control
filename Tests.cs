using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AppleControl {
internal static class Tests {
    static int count;
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);count++;}
    static void Reject(Action action,string message){bool failed=false;try{action();}catch{failed=true;}Assert(failed,message);}
    static string Hash(string path){using(var s=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(s)).Replace("-","");}
    static string Fixture(string url,string hash){return new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"tag_name","v0.3.0"},{"draft",false},{"prerelease",false},{"assets",new object[]{new Dictionary<string,object>{{"name","Apple.Display.Control.exe"},{"browser_download_url",url},{"digest",hash},{"size",3}}}}});}
    static string Executable {get{return Process.GetCurrentProcess().MainModule.FileName;}}
    static string root;
    [STAThread] static int Main(string[] args){
        if(args.Length>0&&args[0]=="--apply-update")return Updates.Apply(args);
        if(args.Length>0&&args[0]=="--tray"){File.WriteAllText(Executable+".restarted","ok");return 0;}
        if(args.Length>0&&args[0]=="--fixture-parent"){File.WriteAllText(args[1],"ready");Thread.Sleep(2000);return 0;}
        if(args.Length>0&&args[0]=="--echo"){File.WriteAllLines(args[1],args.Skip(2));return 0;}
        root=Path.Combine(App.Folder,"test-work",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Clicks();KeyboardModes();SliderInput();Sliders();Metadata();Arguments();Install();
            if(args.Contains("--online")){var release=System.Threading.Tasks.Task.Run(()=>Updates.Check()).GetAwaiter().GetResult();Assert(release.Version!=null,"Live release lookup failed");string file=System.Threading.Tasks.Task.Run(()=>Updates.Download(release,n=>{})).GetAwaiter().GetResult();Assert(File.Exists(file),"Live update download failed");File.Delete(file);Directory.Delete(Path.GetDirectoryName(file));Console.WriteLine("Live GitHub check, download and verification passed: "+release.Version);}
            Console.WriteLine("Passed "+count+" checks.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void Clicks(){
        var gate=new TrayClickGate(500);gate.Single(100);Assert(!gate.TakeSingle(599),"Single click opened before the double-click interval");Assert(gate.TakeSingle(600),"Single click did not open");Assert(!gate.TakeSingle(700),"Single click opened twice");
        gate.Single(1000);gate.Double(1200);gate.Single(1201);Assert(!gate.TakeSingle(1800),"Double click left a pending popup");gate.Single(1900);Assert(gate.TakeSingle(2400),"Click after a double-click was lost");
        gate.Single(3000);gate.Cancel();Assert(!gate.TakeSingle(3600),"Right-click cancellation failed");
    }
    static IEnumerable<Control> All(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in All(child))yield return nested;}}
    static void KeyboardModes(){Assert(Keyboard.ModeForStandard(true)==0,"Checked must select F1-F12 mode, not brightness/media keys");Assert(Keyboard.ModeForStandard(false)==1,"Unchecked must select brightness/media keys");Assert(Keyboard.StandardForMode(0),"Reading standard-key mode must check the box");Assert(!Keyboard.StandardForMode(1),"Reading media-key mode must uncheck the box");}
    static void SliderInput(){using(var slider=new BrightnessSlider()){
        slider.AccessibilityObject.Value="35%";Assert(slider.Value==35,"Accessible slider value was not applied");slider.AccessibilityObject.Value="999%";Assert(slider.Value==100,"Slider did not clamp an out-of-range value");
        var key=typeof(BrightnessSlider).GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        key.Invoke(slider,new object[]{new KeyEventArgs(Keys.Home)});Assert(slider.Value==0,"Home did not select minimum brightness");key.Invoke(slider,new object[]{new KeyEventArgs(Keys.Left)});Assert(slider.Value==0,"Arrow key moved below minimum brightness");key.Invoke(slider,new object[]{new KeyEventArgs(Keys.PageUp)});Assert(slider.Value==10,"Page Up did not increase brightness by one step");
        slider.Enabled=false;slider.AccessibilityObject.Value="80%";Assert(slider.Value==10,"Disabled slider accepted an accessibility change");
    }}
    static void Pump(int ms){var timer=Stopwatch.StartNew();while(timer.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(10);}}
    static void Sliders(){
        var changes=new List<string>();var a=new Hid.Display{Id="studio",Name="Studio Display",Min=400,Max=60000,Raw=30000};var b=new Hid.Display{Id="xdr",Name="Studio Display XDR",Min=400,Max=60000,Raw=60000};
        using(var popup=new QuickBrightness((id,value)=>changes.Add(id+":"+value),()=>{})){
            popup.Sync(new[]{a,b});var sliders=All(popup).OfType<BrightnessSlider>().ToArray();Assert(sliders.Length==2,"Two displays did not get two sliders");
            var studio=sliders.Single(s=>s.AccessibleName=="Studio Display brightness percent");studio.Value=30;studio.Value=40;Pump(220);
            Assert(changes.SequenceEqual(new[]{"studio:40"}),"Rapid changes were not coalesced or targeted the wrong display");
            popup.Result("studio",40,40,null);popup.Sync(new[]{b});Assert(All(popup).OfType<BrightnessSlider>().Count()==1,"Disconnected display remained in the popup");
            b.Active=0;b.Presets.Add(new Hid.Preset{Index=0,Name="Photography (P3-D65)"});popup.Sync(new[]{b});Assert(!All(popup).OfType<BrightnessSlider>().Single().Enabled,"Calibrated preset did not lock its slider");
            popup.Sync(new Hid.Display[0]);Assert(!All(popup).OfType<BrightnessSlider>().Any(),"Empty state retained a slider");
            b.Presets.Clear();popup.Sync(new[]{a,b});popup.Render(Path.Combine(App.Folder,"quick-preview-two-displays.png"));
        }
    }
    static void Metadata(){
        const string url="https://github.com/amedinafp/apple-display-control/releases/download/v0.3.0/Apple.Display.Control.exe";
        string sample=Path.Combine(root,"download.exe");File.WriteAllBytes(sample,new byte[]{1,2,3});string hash=Hash(sample);
        var release=Updates.Parse(Fixture(url,"sha256:"+hash));Assert(release.Version==new Version("0.3.0"),"Release version parsing failed");Updates.Verify(sample,release);count++;
        File.WriteAllBytes(sample,new byte[]{3,2,1});Reject(()=>Updates.Verify(sample,release),"Corrupt update was accepted");
        Reject(()=>Updates.Parse(Fixture(url,null)),"Update without a hash was accepted");Reject(()=>Updates.Parse(Fixture(url.Replace("https://github.com","https://example.com"),"sha256:"+hash)),"Untrusted update host was accepted");Reject(()=>Updates.Parse(Fixture(url.Replace("https:","http:"),"sha256:"+hash)),"Unencrypted download was accepted");
        Reject(()=>Updates.Parse(Fixture(url.Replace("github.com","attacker@github.com"),"sha256:"+hash)),"Embedded URL credentials were accepted");
        Reject(()=>Updates.Parse(Fixture(url.Replace("github.com","github.com:444"),"sha256:"+hash)),"Nonstandard update port was accepted");
        Reject(()=>Updates.Parse(Fixture(url.Replace("Apple.Display.Control.exe","other.exe"),"sha256:"+hash)),"An unrelated executable download was accepted");
    }
    static void Arguments(){
        string output=Path.Combine(root,"arguments.txt");string[] expected={@"C:\A folder\app.exe",@"C:\ends in slash\","a\"b",@"one\two\"+"\"three",""};
        string args="--echo "+Updates.Quote(output)+" "+string.Join(" ",expected.Select(Updates.Quote));
        using(var process=Process.Start(new ProcessStartInfo(Executable,args){UseShellExecute=false,CreateNoWindow=true})){Assert(process.WaitForExit(5000)&&process.ExitCode==0,"Argument fixture failed");}
        Assert(File.ReadAllLines(output).SequenceEqual(expected),"Update arguments were not preserved");
    }
    static void Install(){
        string folder=Path.Combine(root,"Install with spaces");Directory.CreateDirectory(folder);string target=Path.Combine(folder,"Apple Display Control.exe");File.Copy(Executable,target);
        string staging=Path.Combine(folder,".updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);string staged=Path.Combine(staging,"Apple.Display.Control.exe");File.Copy(Executable,staged);using(var s=new FileStream(staged,FileMode.Append))s.WriteByte(42);
        Reject(()=>Updates.ValidatePaths(staged,Path.Combine(folder,"Unrelated.exe")),"Updater accepted an unrelated target executable");
        Reject(()=>Updates.ValidatePaths(Executable,target),"Updater accepted an unstaged executable");
        string unrelated=Path.Combine(folder,"Unrelated.exe");File.Copy(Executable,unrelated);
        using(var rejected=Process.Start(new ProcessStartInfo(staged,"--apply-update 9999999 1 "+Updates.Quote(unrelated)+" "+Hash(staged)){UseShellExecute=false,CreateNoWindow=true})){Assert(rejected.WaitForExit(5000)&&rejected.ExitCode!=0,"Invalid update was not rejected");}
        Thread.Sleep(100);Assert(!File.Exists(unrelated+".restarted"),"Invalid update arguments launched another executable");
        string config=Path.Combine(folder,"settings.xml");File.WriteAllText(config,"keep my settings");string ready=Path.Combine(folder,"ready.txt");
        using(var parent=Process.Start(new ProcessStartInfo(target,"--fixture-parent "+Updates.Quote(ready)){UseShellExecute=false,CreateNoWindow=true})){
            long start=parent.StartTime.ToUniversalTime().Ticks;var clock=Stopwatch.StartNew();while(!File.Exists(ready)&&clock.ElapsedMilliseconds<5000)Thread.Sleep(20);
            Assert(File.Exists(ready),"Update fixture did not start");
            string args="--apply-update "+parent.Id+" "+start+" "+Updates.Quote(target)+" "+Hash(staged);
            using(var helper=Process.Start(new ProcessStartInfo(staged,args){UseShellExecute=false,CreateNoWindow=true})){Assert(helper.WaitForExit(10000)&&helper.ExitCode==0,"In-app replacement failed");}
        }
        var wait=Stopwatch.StartNew();while(!File.Exists(target+".restarted")&&wait.ElapsedMilliseconds<5000)Thread.Sleep(20);
        Assert(File.Exists(target+".restarted"),"Updated app was not restarted");Assert(Hash(target)==Hash(staged),"Target app was not replaced");Assert(File.Exists(target+".previous"),"Backup was not kept");Assert(File.ReadAllText(config)=="keep my settings","Update changed user settings");
    }
}
}
