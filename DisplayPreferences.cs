using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace AppleControl {
internal sealed class DisplayPreferences {
    readonly List<string> order=new List<string>();
    readonly Dictionary<string,string> names=new Dictionary<string,string>();
    internal void SetName(string id,string name){name=new string((name??"").Where(c=>!char.IsControl(c)).ToArray()).Trim();if(name.Length>60){name=name.Substring(0,60);if(char.IsHighSurrogate(name[name.Length-1]))name=name.Substring(0,name.Length-1);}if(name.Length==0)names.Remove(id);else names[id]=name;}
    internal Hid.Display[] Apply(IEnumerable<Hid.Display> displays){var list=displays.ToArray();foreach(var d in list){if(!order.Contains(d.Id))order.Add(d.Id);string name;d.Alias=names.TryGetValue(d.Id,out name)?name:null;}return list.OrderBy(d=>order.IndexOf(d.Id)).ToArray();}
    internal void Reorder(IEnumerable<string> ids){var visible=ids.Distinct().ToArray();foreach(string id in visible)if(!order.Contains(id))order.Add(id);int next=0;var set=new HashSet<string>(visible);for(int i=0;i<order.Count;i++)if(set.Contains(order[i]))order[i]=visible[next++];}
    internal XElement ToXml(){return new XElement("Displays",order.Select(id=>new XElement("Display",new XAttribute("id",id),new XAttribute("name",names.ContainsKey(id)?names[id]:""))));}
    internal void Read(XElement element){order.Clear();names.Clear();if(element==null)return;foreach(var item in element.Elements("Display")){string id=(string)item.Attribute("id");if(string.IsNullOrWhiteSpace(id)||order.Contains(id))continue;order.Add(id);SetName(id,(string)item.Attribute("name"));}}
}

internal sealed class DisplayArrangement:Form {
    internal sealed class Entry {internal string Id,HardwareName,Alias;public override string ToString(){return string.IsNullOrWhiteSpace(Alias)?HardwareName:Alias;}}
    readonly ListBox list=new ListBox();readonly TextBox name=new TextBox();readonly Button up=new Button(),down=new Button();bool binding;
    internal IEnumerable<Entry> Entries{get{return list.Items.Cast<Entry>();}}
    internal DisplayArrangement(IEnumerable<Hid.Display> displays){
        SuspendLayout();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;DoubleBuffered=true;
        Text="Arrange displays";Font=new Font("Segoe UI",10);ClientSize=new Size(540,380);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        var title=new Label{Text="Display names and order",Font=new Font("Segoe UI",14,FontStyle.Bold)};title.SetBounds(20,18,480,30);Controls.Add(title);
        list.SetBounds(20,64,390,150);list.IntegralHeight=false;list.DrawMode=DrawMode.OwnerDrawFixed;list.ItemHeight=32;list.BorderStyle=BorderStyle.FixedSingle;list.AccessibleName="Display order";list.BackColor=Theme.Field;
        Shown+=(s,e)=>list.ItemHeight=Theme.Px(list,32);list.DpiChangedAfterParent+=(s,e)=>list.ItemHeight=Theme.Px(list,32);
        list.DrawItem+=(s,e)=>{using(var brush=new SolidBrush((e.State&DrawItemState.Selected)!=0?Theme.Line:Theme.Field))e.Graphics.FillRectangle(brush,e.Bounds);if(e.Index>=0)TextRenderer.DrawText(e.Graphics,list.Items[e.Index].ToString(),list.Font,Rectangle.Inflate(e.Bounds,-Theme.Px(list,8),0),Theme.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};Controls.Add(list);
        up.Text="Move up";up.SetBounds(425,64,95,32);up.Click+=(s,e)=>MoveSelected(-1);Controls.Add(up);down.Text="Move down";down.SetBounds(425,106,95,32);down.Click+=(s,e)=>MoveSelected(1);Controls.Add(down);
        var label=new Label{Text="Display name"};label.SetBounds(20,232,390,25);Controls.Add(label);
        name.SetBounds(20,262,390,29);name.MaxLength=60;name.AccessibleName="Display name";Controls.Add(name);
        var reset=new Button{Text="Reset name"};reset.SetBounds(425,260,95,32);reset.Click+=(s,e)=>name.Text="";Controls.Add(reset);
        var hint=new Label{Text="Leave blank to use the original display name.",ForeColor=Color.FromArgb(85,91,104)};hint.SetBounds(20,299,490,24);Controls.Add(hint);
        var save=new Button{Text="Save",DialogResult=DialogResult.OK};save.SetBounds(315,334,95,32);Controls.Add(save);var cancel=new Button{Text="Cancel",DialogResult=DialogResult.Cancel};cancel.SetBounds(425,334,95,32);Controls.Add(cancel);AcceptButton=save;CancelButton=cancel;
        foreach(var d in displays)list.Items.Add(new Entry{Id=d.Id,HardwareName=d.Name,Alias=d.Alias});
        list.SelectedIndexChanged+=(s,e)=>BindName();name.TextChanged+=(s,e)=>{if(!binding&&list.SelectedItem!=null){((Entry)list.SelectedItem).Alias=name.Text;list.Invalidate();}};
        if(list.Items.Count>0)list.SelectedIndex=0;else{name.Enabled=save.Enabled=false;BindName();}
        Theme.Apply(this);name.BackColor=list.BackColor=Theme.Field;ResumeLayout(false);
    }
    void BindName(){binding=true;var entry=list.SelectedItem as Entry;name.Text=entry==null?"":entry.Alias??"";up.Enabled=list.SelectedIndex>0;down.Enabled=list.SelectedIndex>=0&&list.SelectedIndex<list.Items.Count-1;binding=false;}
    void MoveSelected(int offset){int from=list.SelectedIndex,to=from+offset;if(from<0||to<0||to>=list.Items.Count)return;var entry=list.Items[from];binding=true;list.Items.RemoveAt(from);list.Items.Insert(to,entry);list.SelectedIndex=to;binding=false;BindName();}
}
}
