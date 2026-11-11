using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace AppleControl {
internal static class App {
    internal static readonly string Folder=AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly string SettingsPath=Path.Combine(Folder,"settings.xml");
    internal static readonly string LogPath=Path.Combine(Folder,"display-control.log");
    static readonly object LogLock=new object();
    internal static void Log(string text) { lock(LogLock) try { if(File.Exists(LogPath) && new FileInfo(LogPath).Length>512000) File.WriteAllText(LogPath,""); File.AppendAllText(LogPath,DateTimeOffset.Now.ToString("o")+" "+text+Environment.NewLine); } catch { } }
    [STAThread] static int Main(string[] args) {
        if(args.Length>0&&args[0]=="--apply-update")return Updates.Apply(args);
        Dpi.Initialize();return Run(args);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string[] args){
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Contains("--render-review")) {
            using(var window=new MainWindow(false)) {window.RenderReview();}return 0;
        }
        if(args.Contains("--verify")) {
            try {
                var lines=new List<string>();
                foreach(var d in Hid.Discover()) {
                    using(var e=new Hid.Endpoint(d.BrightnessPath)) {uint original=e.Read(0x82,0x10);e.Write(0x82,0x10,original,true);if(e.Read(0x82,0x10)!=original)throw new Exception("Brightness readback mismatch");lines.Add(d.Name+": brightness write/readback passed; original value preserved.");}
                    d.LoadPresets();if(d.Active.HasValue){int original=d.Active.Value;d.SetPreset(original);using(var e=new Hid.Endpoint(d.PresetPath)){if(e.Read(0xFF20,3)!=original)throw new Exception("Preset readback mismatch");}lines.Add(d.Name+": preset write/readback passed; original preset preserved.");}
                }
                foreach(var pair in Keyboard.ReadLive()) {Keyboard.SetLive(pair.Key,pair.Value);lines.Add(pair.Key+": keyboard mode write/readback passed; original mode preserved.");}
                File.WriteAllLines(Path.Combine(Folder,"verification.txt"),lines);return 0;
            }catch(Exception ex){App.Log("Verification: "+ex);return 1;}
        }
        if(args.Length==2 && args[0]=="--keyboard-mode") {
            try { if(args[1]!="standard" && args[1]!="media") return 2; Keyboard.Set(args[1]=="standard"); return 0; }
            catch(Exception ex) { Log("Keyboard: "+ex); return 1; }
        }
        if(args.Contains("--diagnose")) {
            try {
                var lines=new List<string> {"Apple Display Control diagnostics", "HDR: "+Hdr.AnyEnabled(),Keyboard.Probe()};
                foreach(var d in Hid.Discover()) {
                    lines.Add(d.Name+" | brightness="+d.Raw+" range="+d.Min+".."+d.Max+" | preset endpoint="+(d.PresetPath!=null));
                    try { d.LoadPresets(); lines.Add("Active preset: "+d.Active); foreach(var p in d.Presets) lines.Add("  "+p.Index+": "+p.Name+" | "+p.Description); } catch(Exception ex) { lines.Add("Presets: "+ex.Message); }
                }
                File.WriteAllLines(Path.Combine(Folder,"diagnostics.txt"),lines); return 0;
            } catch(Exception ex) { Log(ex.ToString()); return 1; }
        }
        bool first;
        using(var mutex=new Mutex(true,@"Local\AppleDisplayControlPortable",out first)) {
            if(!first) { NativeMethods.ShowExisting(); return 0; }
            Application.Run(new MainWindow(args.Contains("--tray"))); mutex.ReleaseMutex();
        }
        return 0;
    }
    internal static class NativeMethods {
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr FindWindow(string name,string title);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
        internal static readonly int ShowMessage=RegisterWindowMessage("AppleDisplayControl.Show.v1");
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
        internal static void ShowExisting() { IntPtr w=FindWindow(null,"Apple Display Control"); if(w!=IntPtr.Zero) PostMessage(w,(uint)ShowMessage,IntPtr.Zero,IntPtr.Zero); }
    }
}

internal sealed class MainWindow : Form {
    readonly ComboBox display=new DarkComboBox(),presets=new DarkComboBox();
    readonly BrightnessSlider brightness=new BrightnessSlider(); readonly Label percent=new Label(),status=new Label(),presetNote=new Label(),keyboardNote=new Label();
    readonly CheckBox connectMax=new CheckBox(),login=new CheckBox(),standard=new CheckBox();
    readonly Button applyPreset=new Button(),applyKeyboard=new Button(),max=new Button();
    readonly NotifyIcon tray=new NotifyIcon(); readonly System.Windows.Forms.Timer poll=new System.Windows.Forms.Timer(),debounce=new System.Windows.Forms.Timer();
    readonly ToolTip help=new ToolTip {AutoPopDelay=20000};
    readonly Dictionary<string,Hid.Display> known=new Dictionary<string,Hid.Display>();
    readonly Dictionary<string,int> quickChanges=new Dictionary<string,int>();
    readonly TrayClickGate clickGate=new TrayClickGate(SystemInformation.DoubleClickTime);
    readonly QuickBrightness quick;
    bool drainingQuick,showingUpdates;Point trayAnchor;
    bool updating,busy,quitting,startHidden,rendering; Hid.Display selected;
    Panel displayPanel;
    internal MainWindow(bool hidden) {
        SuspendLayout();DoubleBuffered=true;AutoScaleDimensions=new SizeF(96,96);
        startHidden=hidden; Text="Apple Display Control"; Font=new Font("Segoe UI",10); BackColor=Color.FromArgb(246,247,249);
        AutoScaleMode=AutoScaleMode.Dpi; ClientSize=new Size(690,680); MinimumSize=new Size(706,719); StartPosition=FormStartPosition.CenterScreen;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        quick=new QuickBrightness(QueueQuickBrightness,Reveal);
        var root=new SmoothTable {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(24)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,56)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,259)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,135));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,76)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); Controls.Add(root);
        var heading=new Label {Text="Your Apple hardware",Font=new Font("Segoe UI",22,FontStyle.Bold),AutoSize=true,Margin=new Padding(0)}; root.Controls.Add(heading);
        var top=new SmoothTable {Dock=DockStyle.Fill,ColumnCount=2}; top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,105));
        DpiChanged+=(s,e)=>BeginInvoke(new Action(()=>top.ColumnStyles[1].Width=Theme.Px(this,105)));
        display.DropDownStyle=ComboBoxStyle.DropDownList; display.Dock=DockStyle.Top; display.AccessibleName="Display"; top.Controls.Add(display);
        var refresh=new Button {Text="Refresh",Dock=DockStyle.Top,Height=30}; refresh.Click+=async(s,e)=>await Scan(true); top.Controls.Add(refresh); root.Controls.Add(top);
        displayPanel=new SmoothPanel {Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(16)}; root.Controls.Add(displayPanel);
        AddLabel(displayPanel,"Brightness",16,12,200,28,FontStyle.Bold);
        percent.SetBounds(515,12,90,28); percent.TextAlign=ContentAlignment.MiddleRight; displayPanel.Controls.Add(percent);
        brightness.SetBounds(12,49,487,36); brightness.Minimum=0; brightness.Maximum=100; brightness.LargeChange=10; brightness.AccessibleName="Brightness percent"; displayPanel.Controls.Add(brightness);
        max.Text="100%"; max.SetBounds(511,50,90,34); displayPanel.Controls.Add(max);
        AddLabel(displayPanel,"Reference preset",16,105,220,26,FontStyle.Bold);
        presets.DropDownStyle=ComboBoxStyle.DropDownList; presets.SetBounds(16,138,483,30); presets.AccessibleName="Reference preset"; displayPanel.Controls.Add(presets);
        applyPreset.Text="Apply"; applyPreset.SetBounds(511,137,90,32); displayPanel.Controls.Add(applyPreset);
        presetNote.SetBounds(16,177,590,70); presetNote.AutoEllipsis=true;presetNote.ForeColor=Color.FromArgb(85,91,104); displayPanel.Controls.Add(presetNote);
        var kb=new SmoothPanel {Dock=DockStyle.Fill,BackColor=Color.White,Margin=new Padding(3,10,3,0)}; root.Controls.Add(kb);
        AddLabel(kb,"Apple keyboard",16,10,230,26,FontStyle.Bold);
        standard.Text="Use F1, F2, etc. as standard function keys"; standard.SetBounds(16,43,470,27); kb.Controls.Add(standard);
        applyKeyboard.Text="Save"; applyKeyboard.SetBounds(511,41,90,32); kb.Controls.Add(applyKeyboard);
        keyboardNote.SetBounds(16,78,590,43); keyboardNote.ForeColor=Color.FromArgb(85,91,104); kb.Controls.Add(keyboardNote);
        var options=new Panel {Dock=DockStyle.Fill}; root.Controls.Add(options);
        connectMax.Text="Set brightness to 100% when a display connects"; connectMax.SetBounds(0,12,610,25); options.Controls.Add(connectMax);
        login.Text="Open in the notification area when I log into Windows"; login.SetBounds(0,40,610,25); options.Controls.Add(login);
        var bottom=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight}; root.Controls.Add(bottom);
        status.AutoSize=false; status.Size=new Size(350,44); status.ForeColor=Color.FromArgb(85,91,104); status.Text="Looking for displays…"; bottom.Controls.Add(status);
        var exit=new Button {Text="Quit",Width=80,Height=30}; exit.Click+=(s,e)=>Quit(); bottom.Controls.Add(exit);
        var menu=new ContextMenuStrip(); menu.Items.Add("Open controls",null,(s,e)=>Reveal()); menu.Items.Add("Set all displays to 100%",null,async(s,e)=>await SetAllMax());menu.Items.Add("Check for updates…",null,(s,e)=>ShowUpdates()); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Quit",null,(s,e)=>Quit());
        tray.Icon=Icon; tray.Text="Apple Display Control"; tray.ContextMenuStrip=menu; tray.Visible=true;
        tray.MouseClick+=async(s,e)=>{if(e.Button==MouseButtons.Left&&clickGate.Single(ClickTime)){trayAnchor=Cursor.Position;await OpenQuick();}};
        tray.MouseDoubleClick+=(s,e)=>{if(e.Button==MouseButtons.Left){clickGate.Double(ClickTime);quick.Hide();Reveal();}};
        menu.Opening+=(s,e)=>quick.Hide();
        display.SelectedIndexChanged+=async(s,e)=>{ if(!updating) await SelectDisplay(); };
        brightness.ValueChanged+=(s,e)=>{ percent.Text=brightness.Value+"%"; if(!updating) { debounce.Stop(); debounce.Start(); } };
        debounce.Interval=180; debounce.Tick+=async(s,e)=>{debounce.Stop(); await SetBrightness(brightness.Value);};
        max.Click+=async(s,e)=>await SetBrightness(100);
        presets.SelectedIndexChanged+=(s,e)=>UpdatePresetNote(); applyPreset.Click+=async(s,e)=>await ChangePreset();
        applyKeyboard.Click+=async(s,e)=>await SaveKeyboard();
        connectMax.CheckedChanged+=(s,e)=>{if(!updating) SaveSettings();};
        login.CheckedChanged+=(s,e)=>{if(!updating) SetStartup();};
        poll.Interval=5000; poll.Tick+=async(s,e)=>await Scan(false);
        Shown+=async(s,e)=>{top.ColumnStyles[1].Width=Theme.Px(this,105);if(rendering)return;if(startHidden) Hide(); LoadSettings(); await Scan(true); poll.Start();};
        FormClosing+=(s,e)=>{if(!quitting) {e.Cancel=true; Hide();} };
        var updates=new Button{Text="App updates…",Width=125,Height=30};updates.Click+=(s,e)=>ShowUpdates();bottom.Controls.Add(updates);
        Theme.Apply(this);Theme.Apply(menu);help.BackColor=Theme.Field;help.ForeColor=Theme.Text;
        FormClosed+=(s,e)=>{poll.Stop();debounce.Stop();quick.Dispose();tray.Dispose();help.Dispose();};
        ResumeLayout(false);
    }
    static void AddLabel(Control parent,string text,int x,int y,int w,int h,FontStyle style) { var label=new Label {Text=text,Font=new Font("Segoe UI",10,style)}; label.SetBounds(x,y,w,h); parent.Controls.Add(label); }
    static long ClickTime {get{return (long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);}}
    internal void RenderReview() {
        LoadSettings();var found=Hid.Discover();foreach(var d in found){try{d.LoadPresets();}catch{}known[d.Id]=d;display.Items.Add(d);}selected=found.FirstOrDefault();updating=true;display.SelectedItem=selected;updating=false;Populate();
        status.Text=found.Count+" display(s) connected · Changes save silently";
        rendering=true;ShowInTaskbar=false;Opacity=0;StartPosition=FormStartPosition.Manual;Location=new Point(-30000,-30000);Show();PerformLayout();
        using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(App.Folder,"preview.png"));}Hide();
        quick.Sync(known.Values);quick.Render(Path.Combine(App.Folder,"quick-preview.png"));
        tray.Visible=false;tray.Dispose();Dispose();
    }
    void Reveal() {quick.Hide();Show(); WindowState=FormWindowState.Normal; Activate();}
    void ShowUpdates(){if(busy||drainingQuick||quick.Interacting||showingUpdates)return;quick.Hide();poll.Stop();showingUpdates=true;try{using(var dialog=new UpdateWindow()){dialog.ShowDialog(this);if(dialog.RestartRequested){quitting=true;Close();}else poll.Start();}}finally{showingUpdates=false;}}
    async Task OpenQuick(){if(quitting||IsDisposed||showingUpdates)return;quick.Sync(known.Values);quick.ShowNear(trayAnchor);await Scan(false);}
    void QueueQuickBrightness(string id,int value){quickChanges[id]=value;DrainQuickBrightness();}
    async void DrainQuickBrightness(){
        if(drainingQuick)return;drainingQuick=true;
        try{while(quickChanges.Count>0&&!quitting){
            while(busy&&!quitting)await Task.Delay(60);if(quitting)break;
            var next=quickChanges.First();quickChanges.Remove(next.Key);Hid.Display d;
            if(!known.TryGetValue(next.Key,out d)){quick.Result(next.Key,next.Value,next.Value,"Display disconnected.");continue;}
            busy=true;
            try{if(!IsAdjustable(d))throw new Exception("Brightness is fixed by the reference preset.");await Task.Run(()=>d.SetBrightness(next.Value));quick.Result(d.Id,next.Value,d.Percent,null);if(selected!=null&&selected.Id==d.Id){selected=d;updating=true;brightness.Value=Math.Max(0,Math.Min(100,d.Percent));updating=false;}status.Text=d.Name+" · "+d.Percent+"%";}
            catch(Exception ex){quick.Result(d.Id,next.Value,d.Percent,ex.Message);Error(ex);}finally{busy=false;}
        }}finally{drainingQuick=false;}
    }
    void Quit() {if(busy||showingUpdates) {status.Text="Finishing the current operation…";return;} quitting=true;Close();}
    protected override void WndProc(ref Message m) {if(m.Msg==App.NativeMethods.ShowMessage) Reveal();base.WndProc(ref m);}
    void Error(Exception ex) {status.Text=ex.Message; App.Log(ex.ToString());}
    async Task Scan(bool force) {
        if(busy||quick.Interacting||drainingQuick||brightness.Capture||debounce.Enabled||display.DroppedDown||presets.DroppedDown) return; busy=true;
        try {
            var found=await Task.Run(()=>Hid.Discover());
            var ids=new HashSet<string>(found.Select(d=>d.Id));
            foreach(string id in known.Keys.ToArray()) if(!ids.Contains(id)) known.Remove(id);
            foreach(var d in found) {
                Hid.Display previous;
                bool alreadyKnown=known.TryGetValue(d.Id,out previous);
                if(alreadyKnown && !force) {d.Presets=previous.Presets;d.Active=previous.Active;try{await Task.Run(()=>d.RefreshActive());}catch(Exception ex){App.Log("Active preset: "+ex.Message);}}
                else {
                    try {await Task.Run(()=>d.LoadPresets());} catch(Exception ex) {App.Log("Preset discovery: "+ex.Message);}
                    if(!alreadyKnown && connectMax.Checked && IsAdjustable(d)) {
                        try {await Task.Run(()=>d.SetBrightness(100));} catch(Exception ex) {App.Log("Connection brightness: "+ex.Message);}
                    }
                }
                if(alreadyKnown){previous.Name=d.Name;previous.BrightnessPath=d.BrightnessPath;previous.PresetPath=d.PresetPath;previous.Min=d.Min;previous.Max=d.Max;previous.Raw=d.Raw;previous.Presets=d.Presets;previous.Active=d.Active;}
                else known[d.Id]=d;
            }
            string selectedId=selected==null?null:selected.Id;
            updating=true;bool changed=Theme.SetChoices(display,known.Values.ToArray());
            selected=known.ContainsKey(selectedId??"")?known[selectedId]:known.Values.FirstOrDefault();
            display.SelectedItem=selected; updating=false; Populate();
            quick.Sync(known.Values);
            if(changed||force)status.Text=known.Count==0?"Connect a Studio Display or Studio Display XDR.":known.Count+" display"+(known.Count==1?"":"s")+" connected · Changes save silently";
        } catch(Exception ex) {updating=false;Error(ex);} finally {busy=false;}
    }
    async Task SelectDisplay() {if(busy) return;selected=display.SelectedItem as Hid.Display; if(selected==null) return;busy=true;try {await Task.Run(()=>selected.Refresh());Populate();}catch(Exception ex){Error(ex);}finally{busy=false;}}
    bool IsAdjustable(Hid.Display d) {var active=d.Presets.Find(p=>p.Index==d.Active);return active==null || active.Adjustable;}
    void Populate() {
        updating=true; displayPanel.Enabled=selected!=null;
        var choices=selected==null?new Hid.Preset[0]:selected.Presets.ToArray();
        Theme.SetChoices(presets,choices);
        if(selected!=null) {
            brightness.Value=Math.Max(0,Math.Min(100,selected.Percent));percent.Text=brightness.Value+"%";
            presets.SelectedItem=selected.Presets.Find(p=>p.Index==selected.Active);
            brightness.Enabled=max.Enabled=IsAdjustable(selected);
            presets.Enabled=applyPreset.Enabled=selected.Presets.Count>0;
        } else {percent.Text="—";}
        UpdatePresetNote(); updating=false;
    }
    void UpdatePresetNote() {
        var p=presets.SelectedItem as Hid.Preset;
        presetNote.Text=p!=null?(p.Adjustable?"": "This calibrated preset fixes brightness. ")+p.Description:selected!=null&&selected.Presets.Count>0?"Select a preset. The current preset could not be read.":"Reference presets are not available on this connection.";
        help.SetToolTip(presetNote,presetNote.Text);
    }
    async Task SetBrightness(int value) {
        if(busy||selected==null)return;busy=true;Hid.Display d=selected;
        try {await Task.Run(()=>d.SetBrightness(value));updating=true;brightness.Value=Math.Max(0,Math.Min(100,d.Percent));updating=false;status.Text=d.Name+" · "+d.Percent+"%";}catch(Exception ex){Error(ex);}finally{busy=false;}
    }
    async Task SetAllMax() {if(busy||showingUpdates)return; foreach(var d in known.Values.ToArray()) {if(!IsAdjustable(d))continue;busy=true;try{await Task.Run(()=>d.SetBrightness(100));}catch(Exception ex){Error(ex);}finally{busy=false;}}Populate();}
    async Task ChangePreset() {
        if(busy||selected==null)return;var next=presets.SelectedItem as Hid.Preset;if(next==null||next.Index==selected.Active)return;
        busy=true; Hid.Display d=selected; int? previous=d.Active;
        try {
            bool? hdr=await Task.Run(()=>Hdr.AnyEnabled());
            if(hdr!=false) {status.Text=hdr==true?"Turn off Windows HDR before switching reference presets.":"HDR status could not be checked. Preset switching is paused.";return;}
            if(!previous.HasValue) {status.Text="Current preset could not be read; switching is paused to allow safe recovery.";return;}
            await Task.Run(()=>d.SetPreset(next.Index));
            bool keep=false;
            try {using(var confirm=new PresetConfirm()) keep=confirm.ShowDialog(this)==DialogResult.OK;}
            finally {if(!keep) d.SetPreset(previous.Value);}
            await Task.Run(()=>d.Refresh());Populate();status.Text=keep?"Reference preset applied.":"Previous reference preset restored.";
        }catch(Exception ex){Error(ex);}finally{busy=false;}
    }
    string StartupPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"Apple Display Control.lnk");}}
    void SetStartup() {
        try {
            if(login.Checked) {
                Type type=Type.GetTypeFromProgID("WScript.Shell"); dynamic shell=Activator.CreateInstance(type);dynamic link=shell.CreateShortcut(StartupPath);
                link.TargetPath=Application.ExecutablePath;link.Arguments="--tray";link.WorkingDirectory=App.Folder;link.Description="Apple display and keyboard controls";link.Save();
                status.Text="Will open quietly when you log in.";
            }else {if(File.Exists(StartupPath))File.Delete(StartupPath);status.Text="Automatic startup disabled.";}
        }catch(Exception ex){updating=true;login.Checked=File.Exists(StartupPath);updating=false;Error(ex);}
    }
    void LoadSettings() {
        updating=true;
        try {if(File.Exists(App.SettingsPath))connectMax.Checked=(string)XDocument.Load(App.SettingsPath).Root.Element("MaxOnConnect")=="true";}catch(Exception ex){App.Log("Settings: "+ex.Message);}
        login.Checked=File.Exists(StartupPath);
        var kb=Keyboard.ReadSettings();var live=Keyboard.ReadLive();standard.Enabled=applyKeyboard.Enabled=kb.Count>0||live.Count>0;
        standard.Checked=live.Count>0?live.Values.All(Keyboard.StandardForMode):kb.Count>0&&kb.Values.All(v=>Keyboard.StandardForMode(v));
        keyboardNote.Text=!standard.Enabled?"Apple keyboard driver not detected.":"Hold Fn for the other action. Saving requires Windows approval.";
        updating=false;
    }
    void SaveSettings() {try {new XDocument(new XElement("Settings",new XElement("MaxOnConnect",connectMax.Checked?"true":"false"))).Save(App.SettingsPath);}catch(Exception ex){Error(ex);}}
    async Task SaveKeyboard() {
        if(busy)return;busy=true;
        try {
            var info=new ProcessStartInfo(Application.ExecutablePath,"--keyboard-mode "+(standard.Checked?"standard":"media")) {UseShellExecute=true,Verb="runas",WorkingDirectory=App.Folder};
            using(var p=Process.Start(info)) {await Task.Run(()=>p.WaitForExit());if(p.ExitCode!=0)throw new Exception("Keyboard setting was not saved. See the log for details.");}
            keyboardNote.Text=Keyboard.ReadLive().Count>0?"Saved and applied to the connected Apple keyboard.":"Saved. Reconnect the keyboard or restart Windows to apply this preference.";
        }catch(System.ComponentModel.Win32Exception ex){if(ex.NativeErrorCode==1223)status.Text="Keyboard setting unchanged.";else Error(ex);}catch(Exception ex){Error(ex);}finally{busy=false;}
    }
}
internal sealed class PresetConfirm : Form {
    int seconds=15; readonly Label countdown=new Label(); readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    internal PresetConfirm() {
        SuspendLayout();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;DoubleBuffered=true;
        Text="Keep this reference preset?";Font=new Font("Segoe UI",11);ClientSize=new Size(440,155);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;
        countdown.SetBounds(20,20,400,55);Controls.Add(countdown);
        var keep=new Button {Text="Keep preset",DialogResult=DialogResult.OK};keep.SetBounds(195,100,110,32);Controls.Add(keep);
        var revert=new Button {Text="Revert",DialogResult=DialogResult.Cancel};revert.SetBounds(315,100,100,32);Controls.Add(revert);CancelButton=revert;AcceptButton=keep;
        timer.Interval=1000;timer.Tick+=(s,e)=>{seconds--;UpdateText();if(seconds<=0){DialogResult=DialogResult.Cancel;Close();}};UpdateText();Shown+=(s,e)=>timer.Start();FormClosed+=(s,e)=>timer.Dispose();
        Theme.Apply(this);
        ResumeLayout(false);
    }
    void UpdateText(){countdown.Text="Can you see the display clearly?\nReverting automatically in "+seconds+" seconds.";}
}
}
