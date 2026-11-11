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
        Dpi.Initialize();return RunTests(args);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int RunTests(string[] args){
        root=Path.Combine(App.Folder,"test-work",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using(var dpiProbe=new Control())dpiProbe.CreateControl();
            Assert(Dpi.PerMonitorV2,"Windows is still stretching a low-resolution, DPI-unaware window");
            Clicks();KeyboardModes();StableRefresh();SliderInput();Sliders();DisplayOptions();WheelInput();LinkedTargets();Metadata();Arguments();Install();
            if(args.Contains("--online")){var release=System.Threading.Tasks.Task.Run(()=>Updates.Check()).GetAwaiter().GetResult();Assert(release.Version!=null,"Live release lookup failed");string file=System.Threading.Tasks.Task.Run(()=>Updates.Download(release,n=>{})).GetAwaiter().GetResult();Assert(File.Exists(file),"Live update download failed");File.Delete(file);Directory.Delete(Path.GetDirectoryName(file));Console.WriteLine("Live GitHub check, download and verification passed: "+release.Version);}
            Console.WriteLine("Passed "+count+" checks.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void Clicks(){
        var gate=new TrayClickGate(500);Assert(gate.Single(100),"First click was delayed by the double-click interval");
        gate.Double(200);Assert(!gate.Single(201),"Trailing double-click event reopened the popup");Assert(!gate.Single(700),"Double-click suppression ended too early");Assert(gate.Single(701),"A subsequent single click was lost");
    }
    static IEnumerable<Control> All(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in All(child))yield return nested;}}
    static void StableRefresh(){using(var box=new DarkComboBox()){object[] values={"Studio Display","Studio Display XDR"};Assert(Theme.SetChoices(box,values),"Initial display list was not populated");box.SelectedIndex=1;int changes=0;box.SelectedIndexChanged+=(s,e)=>changes++;for(int i=0;i<10;i++)Assert(!Theme.SetChoices(box,values),"Unchanged polling rebuilt the display list");Assert(box.SelectedIndex==1&&changes==0,"Background refresh changed the selection or flashed the list");}}
    static void KeyboardModes(){Assert(Keyboard.ModeForStandard(true)==0,"Checked must select F1-F12 mode, not brightness/media keys");Assert(Keyboard.ModeForStandard(false)==1,"Unchecked must select brightness/media keys");Assert(Keyboard.StandardForMode(0),"Reading standard-key mode must check the box");Assert(!Keyboard.StandardForMode(1),"Reading media-key mode must uncheck the box");}
    static void SliderInput(){using(var slider=new BrightnessSlider()){
        slider.AccessibilityObject.Value="35%";Assert(slider.Value==35,"Accessible slider value was not applied");slider.AccessibilityObject.Value="999%";Assert(slider.Value==100,"Slider did not clamp an out-of-range value");
        var key=typeof(BrightnessSlider).GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        key.Invoke(slider,new object[]{new KeyEventArgs(Keys.Home)});Assert(slider.Value==0,"Home did not select minimum brightness");key.Invoke(slider,new object[]{new KeyEventArgs(Keys.Left)});Assert(slider.Value==0,"Arrow key moved below minimum brightness");key.Invoke(slider,new object[]{new KeyEventArgs(Keys.PageUp)});Assert(slider.Value==10,"Page Up did not increase brightness by one step");
        slider.Enabled=false;slider.AccessibilityObject.Value="80%";Assert(slider.Value==10,"Disabled slider accepted an accessibility change");
    }}
    static void Pump(int ms){var timer=Stopwatch.StartNew();while(timer.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(10);}}
    static void DisplayOptions(){
        var a=new Hid.Display{Id="a",Name="Studio Display"};var b=new Hid.Display{Id="b",Name="Studio Display XDR"};var c=new Hid.Display{Id="c",Name="Offline display"};var prefs=new DisplayPreferences();prefs.Apply(new[]{a,b,c});prefs.SetName("a","  Left & <desk>  ");prefs.Reorder(new[]{"b","a"});
        var saved=System.Xml.Linq.XElement.Parse(prefs.ToXml().ToString());var restored=new DisplayPreferences();restored.Read(saved);
        var order=restored.Apply(new[]{a,c,b});Assert(order.Select(d=>d.Id).SequenceEqual(new[]{"b","a","c"}),"Saved display order did not survive a restart");Assert(a.Label=="Left & <desk>"&&a.Name=="Studio Display","Rename changed hardware identity or lost special characters");
        restored.Apply(new[]{b});var reconnected=new Hid.Display{Id="a",Name="Studio Display"};Assert(restored.Apply(new[]{reconnected,b})[0].Id=="b"&&reconnected.Label=="Left & <desk>","Reconnection lost display preferences");
        restored.Reorder(new[]{"c","b"});Assert(restored.Apply(new[]{a,b,c}).Select(d=>d.Id).SequenceEqual(new[]{"c","a","b"}),"Reordering connected displays lost the offline display's position");restored.SetName("a","");restored.Apply(new[]{a});Assert(a.Label==a.Name,"Reset name did not restore the hardware name");
        using(var dialog=new DisplayArrangement(new[]{a,b})){dialog.StartPosition=FormStartPosition.Manual;dialog.ShowInTaskbar=false;dialog.Opacity=0;dialog.Location=new System.Drawing.Point(-30000,-30000);dialog.Show();dialog.PerformLayout();var editor=All(dialog).OfType<TextBox>().Single();editor.Text="Left display";Assert(dialog.Entries.First().Alias=="Left display"&&a.Alias==null,"Rename editor did not use an isolated draft");typeof(DisplayArrangement).GetMethod("MoveSelected",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(dialog,new object[]{1});Assert(dialog.Entries.Select(e=>e.Id).SequenceEqual(new[]{"b","a"}),"Move down did not reorder the dialog's displays");using(var bitmap=new System.Drawing.Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(App.Folder,"arrange-preview.png"));}dialog.Hide();}
    }
    static void WheelInput(){using(var slider=new BrightnessSlider()){slider.Value=50;slider.AdjustWheel(120,false);Assert(slider.Value==55,"Wheel did not increase brightness by 5%");slider.AdjustWheel(-120,true);Assert(slider.Value==54,"Fine wheel adjustment was not 1%");slider.AdjustWheel(60,false);Assert(slider.Value==54,"Partial high-resolution wheel input was rounded up");slider.AdjustWheel(60,false);Assert(slider.Value==59,"Partial wheel deltas did not accumulate");slider.Value=99;slider.AdjustWheel(120,false);Assert(slider.Value==100,"Wheel exceeded maximum brightness");slider.Value=1;slider.AdjustWheel(-120,false);Assert(slider.Value==0,"Wheel went below minimum brightness");slider.Enabled=false;slider.AdjustWheel(120,false);Assert(slider.Value==0,"Wheel changed a locked display");}}
    static void LinkedTargets(){var a=new Hid.Display{Id="a"};var b=new Hid.Display{Id="b"};var locked=new Hid.Display{Id="locked",Active=0};locked.Presets.Add(new Hid.Preset{Index=0,Name="Photography (P3-D65)"});var all=new[]{a,b,locked};Assert(MainWindow.BrightnessTargets(all,"b",false).SequenceEqual(new[]{b}),"Independent brightness affected another display");Assert(MainWindow.BrightnessTargets(all,"b",true).SequenceEqual(new[]{a,b}),"Linked brightness missed a display or changed a fixed reference preset");Assert(MainWindow.BrightnessTargets(all,"missing",false).Length==0&&MainWindow.BrightnessTargets(all,"missing",true).Length==0,"Disconnected source targeted another display");}
    static void Sliders(){
        var changes=new List<string>();var a=new Hid.Display{Id="studio",Name="Studio Display",Min=400,Max=60000,Raw=30000};var b=new Hid.Display{Id="xdr",Name="Studio Display XDR",Min=400,Max=60000,Raw=60000};
        using(var popup=new QuickBrightness((id,value)=>changes.Add(id+":"+value),()=>{})){
            popup.Sync(new[]{a,b});var sliders=All(popup).OfType<BrightnessSlider>().ToArray();Assert(sliders.Length==2,"Two displays did not get two sliders");
            int width=popup.Width;for(int i=0;i<10;i++)popup.Sync(new[]{a,b});Assert(popup.Width==width&&All(popup).OfType<BrightnessSlider>().SequenceEqual(sliders),"Polling resized the popup or replaced its slider controls");
            var studio=sliders.Single(s=>s.AccessibleName=="Studio Display brightness percent");studio.Value=30;studio.Value=40;Pump(220);
            Assert(changes.SequenceEqual(new[]{"studio:40"}),"Rapid changes were not coalesced or targeted the wrong display");
            popup.Result("studio",40,40,null);popup.Sync(new[]{b});Assert(All(popup).OfType<BrightnessSlider>().Count()==1,"Disconnected display remained in the popup");
            b.Active=0;b.Presets.Add(new Hid.Preset{Index=0,Name="Photography (P3-D65)"});popup.Sync(new[]{b});Assert(!All(popup).OfType<BrightnessSlider>().Single().Enabled,"Calibrated preset did not lock its slider");
            popup.Sync(new Hid.Display[0]);Assert(!All(popup).OfType<BrightnessSlider>().Any(),"Empty state retained a slider");
            b.Presets.Clear();popup.Sync(new[]{a,b});popup.Render(Path.Combine(App.Folder,"quick-preview-two-displays.png"));
            var rows=All(popup).OfType<QuickBrightness.BrightnessRow>().ToArray();var first=All(rows[0]).OfType<BrightnessSlider>().Single();var second=All(rows[1]).OfType<BrightnessSlider>().Single();int secondValue=second.Value;
            popup.WheelAt(rows[0].PointToScreen(new System.Drawing.Point(rows[0].Width/2,rows[0].Height/2)),120,true);Assert(first.Value==51&&second.Value==secondValue,"Wheel targeted the focused display instead of the hovered card");
            popup.Sync(new[]{b,a});Assert(All(popup).OfType<BrightnessSlider>().First().AccessibleName=="Studio Display XDR brightness percent","Saved ordering did not move existing popup rows");
            foreach(var row in rows){var slider=All(row).OfType<BrightnessSlider>().Single();var zero=All(row).OfType<Button>().Single(button=>button.Text=="0%");var full=All(row).OfType<Button>().Single(button=>button.Text=="100%");Assert(zero.Right<=slider.Left&&full.Left>=slider.Right,"0% and 100% buttons are not on opposite sides of the slider");var click=typeof(Button).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);click.Invoke(zero,new object[]{EventArgs.Empty});Assert(slider.Value==0,"0% button did not select minimum brightness");click.Invoke(full,new object[]{EventArgs.Empty});Assert(slider.Value==100,"100% button did not select maximum brightness");int before=changes.Count;click.Invoke(full,new object[]{EventArgs.Empty});Assert(changes.Count==before+1&&changes.Last().EndsWith(":100"),"100% at the current limit did not send a request for linked displays");}
        }
        using(var row=new QuickBrightness.BrightnessRow(a.Id,(id,value)=>{})){row.Sync(a);foreach(int dpi in new[]{96,144,192,216,96,192}){row.LayoutAtDpi(dpi);Assert(row.Width==(int)Math.Round(360*dpi/96.0),"DPI change accumulated a width error");var slider=All(row).OfType<BrightnessSlider>().Single();Assert(slider.Left>=0&&slider.Right<=row.Width,"Slider was clipped after a DPI change");}}
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
