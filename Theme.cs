using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppleControl {
internal static class Theme {
    internal static readonly Color Background=Color.FromArgb(18,21,28),Popup=Color.FromArgb(23,27,35),Card=Color.FromArgb(31,37,48),Field=Color.FromArgb(40,48,62),Line=Color.FromArgb(54,64,81),Text=Color.FromArgb(236,241,249),Muted=Color.FromArgb(155,168,188),Accent=Color.FromArgb(134,181,255);
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int size);
    internal static GraphicsPath Rounded(RectangleF rect,float radius){var p=new GraphicsPath();float d=radius*2;p.AddArc(rect.X,rect.Y,d,d,180,90);p.AddArc(rect.Right-d,rect.Y,d,d,270,90);p.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);p.AddArc(rect.X,rect.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    internal static void Apply(Control control){
        bool muted=control.ForeColor==Color.FromArgb(85,91,104);control.ForeColor=muted?Muted:Text;
        if(control is Form){control.BackColor=Background;control.HandleCreated+=(s,e)=>{int dark=1;try{DwmSetWindowAttribute(control.Handle,20,ref dark,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}};}
        else control.BackColor=control.BackColor==Color.White?Card:control.Parent==null?Background:control.Parent.BackColor;
        var button=control as Button;if(button!=null){button.FlatStyle=FlatStyle.Flat;button.BackColor=Field;button.FlatAppearance.BorderColor=Line;button.FlatAppearance.MouseOverBackColor=Color.FromArgb(51,63,82);button.FlatAppearance.MouseDownBackColor=Line;}
        var check=control as CheckBox;if(check!=null){check.FlatStyle=FlatStyle.Flat;check.FlatAppearance.BorderColor=Muted;check.FlatAppearance.CheckedBackColor=Field;}
        var combo=control as ComboBox;if(combo!=null){combo.BackColor=Field;combo.FlatStyle=FlatStyle.Flat;combo.DrawMode=DrawMode.OwnerDrawFixed;combo.ItemHeight=24;combo.DrawItem+=(s,e)=>{bool selected=(e.State&DrawItemState.Selected)!=0;using(var brush=new SolidBrush(selected?Line:Field))e.Graphics.FillRectangle(brush,e.Bounds);string text=e.Index>=0?combo.GetItemText(combo.Items[e.Index]):combo.Text;TextRenderer.DrawText(e.Graphics,text,combo.Font,Rectangle.Inflate(e.Bounds,-6,0),combo.Enabled?Text:Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);};}
        foreach(Control child in control.Controls)Apply(child);
    }
    internal static void Apply(ContextMenuStrip menu){menu.BackColor=Popup;menu.ForeColor=Text;menu.Renderer=new ToolStripProfessionalRenderer(new MenuColors());menu.ShowImageMargin=false;foreach(ToolStripItem item in menu.Items){item.ForeColor=Text;item.Padding=new Padding(8,5,8,5);}}
    sealed class MenuColors:ProfessionalColorTable{public override Color ToolStripDropDownBackground{get{return Popup;}}public override Color MenuItemSelected{get{return Field;}}public override Color MenuBorder{get{return Line;}}public override Color MenuItemBorder{get{return Field;}}public override Color SeparatorDark{get{return Line;}}public override Color SeparatorLight{get{return Line;}}}
}

// A small painted slider avoids the light native trackbar in the dark UI.
// Keyboard and accessibility values remain available without a UI framework.
internal sealed class BrightnessSlider:Control {
    int value,minimum,maximum=100;bool hover;
    internal event EventHandler ValueChanged;
    internal int Minimum{get{return minimum;}set{minimum=value;Value=this.value;}}
    internal int Maximum{get{return maximum;}set{maximum=value;Value=this.value;}}
    internal int LargeChange=10;
    internal int Value{get{return value;}set{int next=Math.Max(minimum,Math.Min(maximum,value));if(this.value==next)return;this.value=next;Invalidate();AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}}
    internal BrightnessSlider(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);TabStop=true;Cursor=Cursors.Hand;Height=34;AccessibleRole=AccessibleRole.Slider;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float x=12+(Width-24)*(value-minimum)/(float)Math.Max(1,maximum-minimum);float y=Height/2f;using(var track=new Pen(Theme.Line,6)){track.StartCap=track.EndCap=LineCap.Round;e.Graphics.DrawLine(track,12,y,Width-12,y);}if(x>12)using(var fill=new Pen(Enabled?Theme.Accent:Theme.Muted,6)){fill.StartCap=fill.EndCap=LineCap.Round;e.Graphics.DrawLine(fill,12,y,x,y);}float radius=hover||Capture?8:7;using(var brush=new SolidBrush(Enabled?Theme.Text:Theme.Muted))e.Graphics.FillEllipse(brush,x-radius,y-radius,radius*2,radius*2);if(Focused)using(var ring=new Pen(Theme.Accent,1))e.Graphics.DrawEllipse(ring,x-radius-3,y-radius-3,radius*2+6,radius*2+6);}
    void SetFromX(int x){Value=minimum+(int)Math.Round((maximum-minimum)*Math.Max(0,Math.Min(1,(x-12)/(double)Math.Max(1,Width-24))));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left&&Enabled){Focus();Capture=true;SetFromX(e.X);}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Capture)SetFromX(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left){Capture=false;Invalidate();}}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;Invalidate();}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Cursor=Enabled?Cursors.Hand:Cursors.Default;Invalidate();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override bool IsInputKey(Keys key){return key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||key==Keys.Home||key==Keys.End||key==Keys.PageUp||key==Keys.PageDown||base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);switch(e.KeyCode){case Keys.Left:case Keys.Down:Value--;break;case Keys.Right:case Keys.Up:Value++;break;case Keys.Home:Value=Minimum;break;case Keys.End:Value=Maximum;break;case Keys.PageUp:Value+=LargeChange;break;case Keys.PageDown:Value-=LargeChange;break;default:return;}e.Handled=true;}
    protected override AccessibleObject CreateAccessibilityInstance(){return new SliderAccess(this);}
    sealed class SliderAccess:ControlAccessibleObject{readonly BrightnessSlider owner;internal SliderAccess(BrightnessSlider owner):base(owner){this.owner=owner;}public override string Value{get{return owner.Value+"%";}set{int number;if(int.TryParse((value??"").TrimEnd('%'),out number)&&owner.Enabled)owner.Value=number;}}}
}
internal sealed class DarkComboBox:ComboBox {
    protected override void WndProc(ref Message message){
        base.WndProc(ref message);if(message.Msg!=0xF&&message.Msg!=0x317&&message.Msg!=0x318)return;
        using(var g=message.Msg==0xF?Graphics.FromHwnd(Handle):message.WParam!=IntPtr.Zero?Graphics.FromHdc(message.WParam):Graphics.FromHwnd(Handle)){
            int width=SystemInformation.VerticalScrollBarWidth+4;
            using(var brush=new SolidBrush(Theme.Field))g.FillRectangle(brush,Width-width,0,width,Height);
            using(var pen=new Pen(Theme.Line,2))g.DrawRectangle(pen,1,1,Width-2,Height-2);
            g.SmoothingMode=SmoothingMode.AntiAlias;float x=Width-width/2f,y=Height/2f;using(var pen=new Pen(Theme.Muted,1.5f))g.DrawLines(pen,new[]{new PointF(x-4,y-2),new PointF(x,y+2),new PointF(x+4,y-2)});
        }
    }
}
}
