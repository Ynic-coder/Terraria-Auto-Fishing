using System.Text.Json;
namespace Fishing;
record GameItem(int Id,string Key,string Name,string English) { public override string ToString()=>$"{Name}  /  {English}  [#{Id}]"; }
static class ItemCatalog {
 public static readonly GameItem[] Items=Load();
 static GameItem[] Load() { using var s=typeof(ItemCatalog).Assembly.GetManifestResourceStream("Fishing.Items")!; return JsonSerializer.Deserialize<GameItem[]>(s)!; }
 public static string Name(int id)=>Items.FirstOrDefault(i=>i.Id==id)?.Name??$"ID {id}";
}
sealed class ItemFilterForm : Form {
 readonly HashSet<int> selected; readonly CheckedListBox list=new(){Dock=DockStyle.Fill,CheckOnClick=true,HorizontalScrollbar=true};
 readonly TextBox search=new(){Dock=DockStyle.Top,PlaceholderText="Поиск: название, English или ID…"};
 readonly Label counter=new(){Dock=DockStyle.Bottom,Height=30}; bool refreshing;
 public int[] SelectedIds=>selected.Order().ToArray();
 public ItemFilterForm(IEnumerable<int> ids) {
  selected=new(ids); Text="Какие предметы подсекать"; Size=new(900,650); StartPosition=FormStartPosition.CenterParent;
  var help=new Label{Dock=DockStyle.Top,Height=42,Text="Отметьте нужные предметы. Список извлечён из Terraria 1.4.5.8.\nНеподходящие поклёвки будут пропускаться. Поиск сохраняет отметки."};
  var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=44};
  var done=new Button{Text="Готово",DialogResult=DialogResult.OK,AutoSize=true}; var clear=new Button{Text="Снять всё",AutoSize=true};
  var crates=new Button{Text="Отметить ящики",AutoSize=true};
  clear.Click+=(_,_)=>{selected.Clear(); RefreshList();}; crates.Click+=(_,_)=>{foreach(var i in ItemCatalog.Items.Where(i=>i.Key.EndsWith("Crate")||i.Key.Contains("CrateHard"))) selected.Add(i.Id); RefreshList();};
  buttons.Controls.AddRange([done,clear,crates]); Controls.Add(list); Controls.Add(counter); Controls.Add(buttons); Controls.Add(search); Controls.Add(help);
  search.TextChanged+=(_,_)=>RefreshList(); list.ItemCheck+=(_,e)=>{if(refreshing)return; var item=(GameItem)list.Items[e.Index]; if(e.NewValue==CheckState.Checked)selected.Add(item.Id);else selected.Remove(item.Id); Count();};
  AcceptButton=done; RefreshList();
 }
 void Count()=>counter.Text=$"Выбрано: {selected.Count}. Пустой список: подсечек не будет.";
 void RefreshList(){ refreshing=true; list.BeginUpdate(); list.Items.Clear(); string q=search.Text.Trim(); foreach(var i in ItemCatalog.Items.Where(i=>q.Length==0||$"{i.Name} {i.English} {i.Key} {i.Id}".Contains(q,StringComparison.OrdinalIgnoreCase)).OrderBy(i=>i.Name))list.Items.Add(i,selected.Contains(i.Id)); list.EndUpdate();refreshing=false;Count(); }
}
sealed class AimForm : Form {
 readonly ComboBox mode=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
 readonly NumericUpDown distance=new(){Minimum=40,Maximum=1200,Increment=20,Width=120};
 readonly NumericUpDown vertical=new(){Minimum=-600,Maximum=600,Increment=20,Width=120};
 public int Mode=>mode.SelectedIndex; public int Distance=>(int)distance.Value; public int Vertical=>(int)vertical.Value;
 public AimForm(int m,int d,int v){ Text="Направление заброса"; ClientSize=new(600,290);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(18),ColumnCount=2,RowCount=5};layout.ColumnStyles.Add(new(SizeType.Percent,45));layout.ColumnStyles.Add(new(SizeType.Percent,55));
  mode.DrawMode=DrawMode.OwnerDrawFixed; mode.DrawItem+=(_,e)=>{ e.DrawBackground(); if(e.Index>=0) { using var brush=new SolidBrush(e.ForeColor); e.Graphics.DrawString(mode.Items[e.Index]!.ToString(),e.Font??Font,brush,e.Bounds); } e.DrawFocusRectangle(); };
  mode.Items.AddRange(["Точка курсора (F8)","Вправо от персонажа","Влево от персонажа"]);mode.SelectedIndex=Math.Clamp(m,0,2);distance.Value=Math.Clamp(d,40,1200);vertical.Value=Math.Clamp(v,-600,600);
  layout.Controls.Add(new Label{Text="Прицеливание",AutoSize=true},0,0);layout.Controls.Add(mode,1,0);layout.Controls.Add(new Label{Text="Дальность, пиксели",AutoSize=true},0,1);layout.Controls.Add(distance,1,1);layout.Controls.Add(new Label{Text="Смещение по Y",AutoSize=true},0,2);layout.Controls.Add(vertical,1,2);
  var note=new Label{Text="Y < 0 — выше, Y > 0 — ниже.\nНаправление работает в фоновом режиме и следует за персонажем. Дальность прицела влияет на траекторию, но не гарантирует точное место падения.",Dock=DockStyle.Fill};layout.Controls.Add(note,0,3);layout.SetColumnSpan(note,2);
  layout.RowStyles.Add(new(SizeType.Absolute,34));layout.RowStyles.Add(new(SizeType.Absolute,34));layout.RowStyles.Add(new(SizeType.Absolute,34));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,32));
  var ok=new Button{Text="Сохранить",DialogResult=DialogResult.OK,Dock=DockStyle.Right,Width=120};layout.Controls.Add(ok,1,4);Controls.Add(layout);AcceptButton=ok;
 }
}


