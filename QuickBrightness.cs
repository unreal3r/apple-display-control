using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AppleControl {
// Delay the single-click action until Windows' double-click interval has passed.
// A second MouseClick after MouseDoubleClick must not reopen the small popup.
internal sealed class TrayClickGate {
    readonly int interval; long due=-1,suppressUntil=-1;
    internal TrayClickGate(int interval) {this.interval=interval;}
    internal bool Single(long now) {if(now<=suppressUntil)return false;due=now+interval;return true;}
    internal void Double(long now) {due=-1;suppressUntil=now+interval;}
    internal void Cancel() {due=-1;}
    internal bool TakeSingle(long now) {if(due<0||now<due)return false;due=-1;return true;}
}

internal sealed class QuickBrightness : Form {
    readonly FlowLayoutPanel rows=new FlowLayoutPanel();
    readonly Label feedback=new Label();
    readonly Dictionary<string,BrightnessRow> controls=new Dictionary<string,BrightnessRow>();
    readonly Action<string,int> change;
    internal bool Interacting {get{return controls.Values.Any(row=>row.Interacting);}}
    internal QuickBrightness(Action<string,int> change,Action open) {
        this.change=change;Text="Quick brightness";Font=new Font("Segoe UI",10);
        BackColor=Color.White;FormBorderStyle=FormBorderStyle.None;ControlBox=false;
        ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(360,220);KeyPreview=true;
        var title=new Label {Text="Brightness",Font=new Font("Segoe UI",12,FontStyle.Bold),Dock=DockStyle.Top,Height=43,Padding=new Padding(16,12,0,0)};
        var full=new LinkLabel {Text="Open full app",Dock=DockStyle.Bottom,Height=35,Padding=new Padding(16,4,0,0),TabStop=true};
        full.LinkClicked+=(s,e)=>{Hide();open();};
        feedback.Dock=DockStyle.Bottom;feedback.Height=35;feedback.Visible=false;feedback.Padding=new Padding(16,2,12,0);feedback.ForeColor=Color.FromArgb(85,91,104);feedback.AutoEllipsis=true;
        rows.Dock=DockStyle.Fill;rows.FlowDirection=FlowDirection.TopDown;rows.WrapContents=false;rows.AutoScroll=true;rows.Padding=new Padding(10,0,10,0);
        Controls.Add(rows);Controls.Add(feedback);Controls.Add(full);Controls.Add(title);
        Deactivate+=(s,e)=>Hide();KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){Hide();e.Handled=true;}};
    }
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ClassStyle|=0x20000;return p;}}
    internal void Sync(IEnumerable<Hid.Display> displays) {
        var list=displays.ToList();var ids=new HashSet<string>(list.Select(d=>d.Id));
        foreach(string id in controls.Keys.ToArray()) if(!ids.Contains(id)){var row=controls[id];rows.Controls.Remove(row);row.Dispose();controls.Remove(id);}
        if(list.Count==0) {
            if(rows.Controls.Count==0)rows.Controls.Add(new Label {Name="empty",Text="No Apple displays connected.\nPlug in a Studio Display or Studio Display XDR.",AutoSize=false,Width=320,Height=80,Padding=new Padding(6,10,0,0)});
        }else {
            foreach(Control c in rows.Controls.Cast<Control>().Where(c=>c.Name=="empty").ToArray()){rows.Controls.Remove(c);c.Dispose();}
            foreach(var d in list) {
                BrightnessRow row;if(!controls.TryGetValue(d.Id,out row)){row=new BrightnessRow(d.Id,change);controls.Add(d.Id,row);rows.Controls.Add(row);}
                row.Sync(d);
            }
        }
        ResizeContent();
    }
    void ResizeContent(){int maxHeight=Math.Max(170,Screen.FromPoint(Cursor.Position).WorkingArea.Height-80);int content=controls.Count==0?80:controls.Values.Sum(row=>row.Height+row.Margin.Vertical);ClientSize=new Size(360,Math.Min(maxHeight,78+content+(feedback.Visible?35:0)));}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var pen=new Pen(Color.FromArgb(210,214,220)))e.Graphics.DrawRectangle(pen,0,0,ClientSize.Width-1,ClientSize.Height-1);}
    internal void ShowNear(Point anchor) {
        Rectangle area=Screen.FromPoint(anchor).WorkingArea;
        int x=Math.Max(area.Left,Math.Min(anchor.X-Width,area.Right-Width));
        int y=anchor.Y-Height-8;if(y<area.Top)y=Math.Min(area.Bottom-Height,anchor.Y+8);
        Location=new Point(x,Math.Max(area.Top,y));feedback.Text="";feedback.Visible=false;ResizeContent();Show();Activate();
    }
    internal void Result(string id,int requested,int actual,string error) {
        BrightnessRow row;if(controls.TryGetValue(id,out row))row.Result(requested,actual);
        feedback.Text=error??"";feedback.Visible=error!=null;ResizeContent();
    }
    internal void Render(string path) {
        bool wasTop=TopMost;TopMost=false;Opacity=0;Location=new Point(-30000,-30000);Show();PerformLayout();
        using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path);}Hide();TopMost=wasTop;Opacity=1;
    }
    internal sealed class BrightnessRow : Panel {
        readonly string id;readonly Label name=new Label(),value=new Label(),note=new Label();
        readonly TrackBar slider=new TrackBar();readonly Timer debounce=new Timer();bool syncing,inFlight;
        internal bool Interacting {get{return slider.Capture||debounce.Enabled||inFlight;}}
        internal BrightnessRow(string id,Action<string,int> change) {
            this.id=id;Width=320;Height=100;Margin=new Padding(0,2,0,2);
            name.SetBounds(6,4,250,25);name.AutoEllipsis=true;Controls.Add(name);
            value.SetBounds(256,4,58,25);value.TextAlign=ContentAlignment.TopRight;Controls.Add(value);
            slider.Minimum=0;slider.Maximum=100;slider.TickStyle=TickStyle.None;slider.LargeChange=10;slider.SetBounds(0,30,320,35);Controls.Add(slider);
            note.SetBounds(6,69,308,25);note.ForeColor=Color.FromArgb(85,91,104);Controls.Add(note);
            debounce.Interval=150;
            slider.ValueChanged+=(s,e)=>{value.Text=slider.Value+"%";if(!syncing){debounce.Stop();debounce.Start();}};
            debounce.Tick+=(s,e)=>{debounce.Stop();inFlight=true;change(id,slider.Value);};
        }
        internal void Sync(Hid.Display d) {
            name.Text=d.Name;slider.AccessibleName=d.Name+" brightness percent";
            var active=d.Presets.Find(p=>p.Index==d.Active);bool adjustable=active==null||active.Adjustable;
            if(!adjustable){debounce.Stop();inFlight=false;}
            slider.Enabled=adjustable;note.Text=adjustable?"":"Brightness is fixed by the reference preset.";Height=adjustable?70:100;
            if(!Interacting){syncing=true;slider.Value=Math.Max(0,Math.Min(100,d.Percent));value.Text=slider.Value+"%";syncing=false;}
        }
        internal void Result(int requested,int actual) {
            inFlight=false;
            if(!debounce.Enabled&&!slider.Capture&&slider.Value==requested){syncing=true;slider.Value=Math.Max(0,Math.Min(100,actual));value.Text=slider.Value+"%";syncing=false;}
        }
        protected override void Dispose(bool disposing){if(disposing)debounce.Dispose();base.Dispose(disposing);}
    }
}
}
