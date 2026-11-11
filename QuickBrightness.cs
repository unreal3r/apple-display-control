using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        this.change=change;Text="Quick brightness";Font=new Font("Segoe UI",10);ForeColor=Theme.Text;
        BackColor=Theme.Popup;FormBorderStyle=FormBorderStyle.None;ControlBox=false;DoubleBuffered=true;
        ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(392,240);KeyPreview=true;
        var title=new Label {Text="Brightness",Font=new Font("Segoe UI",14,FontStyle.Bold),Dock=DockStyle.Top,Height=58,Padding=new Padding(20,19,0,0)};
        var full=new LinkLabel {Text="Open display controls  →",Dock=DockStyle.Bottom,Height=50,Padding=new Padding(21,12,0,0),TabStop=true,LinkColor=Theme.Muted,ActiveLinkColor=Theme.Accent,VisitedLinkColor=Theme.Muted,LinkBehavior=LinkBehavior.HoverUnderline};
        full.LinkClicked+=(s,e)=>{Hide();open();};
        feedback.Dock=DockStyle.Bottom;feedback.Height=38;feedback.Visible=false;feedback.Padding=new Padding(20,4,20,0);feedback.ForeColor=Theme.Muted;feedback.AutoEllipsis=true;
        rows.Dock=DockStyle.Fill;rows.FlowDirection=FlowDirection.TopDown;rows.WrapContents=false;rows.AutoScroll=true;rows.Padding=new Padding(16,4,16,0);
        Controls.Add(rows);Controls.Add(feedback);Controls.Add(full);Controls.Add(title);
        Deactivate+=(s,e)=>Hide();KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){Hide();e.Handled=true;}};
    }
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ClassStyle|=0x20000;return p;}}
    internal void Sync(IEnumerable<Hid.Display> displays) {
        var list=displays.ToList();var ids=new HashSet<string>(list.Select(d=>d.Id));
        foreach(string id in controls.Keys.ToArray()) if(!ids.Contains(id)){var row=controls[id];rows.Controls.Remove(row);row.Dispose();controls.Remove(id);}
        if(list.Count==0) {
            if(rows.Controls.Count==0)rows.Controls.Add(new Label {Name="empty",Text="No displays connected\nConnect a Studio Display or Studio Display XDR.",ForeColor=Theme.Muted,AutoSize=false,Width=360,Height=86,Padding=new Padding(4,12,0,0)});
        }else {
            foreach(Control c in rows.Controls.Cast<Control>().Where(c=>c.Name=="empty").ToArray()){rows.Controls.Remove(c);c.Dispose();}
            foreach(var d in list) {
                BrightnessRow row;if(!controls.TryGetValue(d.Id,out row)){row=new BrightnessRow(d.Id,change);controls.Add(d.Id,row);rows.Controls.Add(row);}
                row.Sync(d);
            }
        }
        ResizeContent();
    }
    void ResizeContent(){int maxHeight=Math.Max(170,Screen.FromPoint(Cursor.Position).WorkingArea.Height-80);int content=controls.Count==0?86:controls.Values.Sum(row=>row.Height+row.Margin.Vertical);ClientSize=new Size(392,Math.Min(maxHeight,112+content+(feedback.Visible?38:0)));}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(ClientSize.Width<32||ClientSize.Height<32)return;using(var outline=Theme.Rounded(new RectangleF(0,0,ClientSize.Width,ClientSize.Height),16)){var old=Region;Region=new Region(outline);if(old!=null)old.Dispose();}}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var outline=Theme.Rounded(new RectangleF(.5f,.5f,ClientSize.Width-1,ClientSize.Height-1),16))using(var pen=new Pen(Theme.Line))e.Graphics.DrawPath(pen,outline);}
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
        readonly BrightnessSlider slider=new BrightnessSlider();readonly Timer debounce=new Timer();bool syncing,inFlight;
        internal bool Interacting {get{return slider.Capture||debounce.Enabled||inFlight;}}
        internal BrightnessRow(string id,Action<string,int> change) {
            this.id=id;Width=360;Height=94;Margin=new Padding(0,0,0,10);BackColor=Theme.Popup;DoubleBuffered=true;
            name.SetBounds(50,18,226,25);name.AutoEllipsis=true;name.ForeColor=Theme.Text;name.BackColor=Theme.Card;Controls.Add(name);
            value.SetBounds(285,15,57,28);value.TextAlign=ContentAlignment.MiddleCenter;value.Font=new Font("Segoe UI",10,FontStyle.Bold);value.ForeColor=Theme.Text;value.BackColor=Theme.Field;Controls.Add(value);
            slider.Minimum=0;slider.Maximum=100;slider.LargeChange=10;slider.SetBounds(12,49,336,34);slider.BackColor=Theme.Card;Controls.Add(slider);
            note.SetBounds(18,87,324,25);note.ForeColor=Theme.Muted;note.BackColor=Theme.Card;Controls.Add(note);
            debounce.Interval=150;
            slider.ValueChanged+=(s,e)=>{value.Text=slider.Value+"%";if(!syncing){debounce.Stop();debounce.Start();}};
            debounce.Tick+=(s,e)=>{debounce.Stop();inFlight=true;change(id,slider.Value);};
        }
        internal void Sync(Hid.Display d) {
            name.Text=d.Name;slider.AccessibleName=d.Name+" brightness percent";
            var active=d.Presets.Find(p=>p.Index==d.Active);bool adjustable=active==null||active.Adjustable;
            if(!adjustable){debounce.Stop();inFlight=false;}
            slider.Enabled=adjustable;note.Text=adjustable?"":"Fixed by the reference preset";Height=adjustable?94:122;
            if(!Interacting){syncing=true;slider.Value=Math.Max(0,Math.Min(100,d.Percent));value.Text=slider.Value+"%";syncing=false;}
        }
        internal void Result(int requested,int actual) {
            inFlight=false;
            if(!debounce.Enabled&&!slider.Capture&&slider.Value==requested){syncing=true;slider.Value=Math.Max(0,Math.Min(100,actual));value.Text=slider.Value+"%";syncing=false;}
        }
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Theme.Rounded(new RectangleF(0,0,Width,Height),11))using(var brush=new SolidBrush(Theme.Card))e.Graphics.FillPath(brush,path);using(var pen=new Pen(Theme.Muted,1.5f)){e.Graphics.DrawRectangle(pen,18,21,21,14);e.Graphics.DrawLine(pen,28,35,28,39);e.Graphics.DrawLine(pen,23,39,33,39);}}
        protected override void Dispose(bool disposing){if(disposing)debounce.Dispose();base.Dispose(disposing);}
    }
}
}
