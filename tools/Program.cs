using Mono.Cecil;
using System.Text.Json;
using System.Text.RegularExpressions;
var asm=AssemblyDefinition.ReadAssembly(args[0]);
Dictionary<string,string> Names(string lang) {
 var result=new Dictionary<string,string>();
 foreach(var resource in asm.MainModule.Resources.OfType<EmbeddedResource>().Where(r=>r.Name.Contains("."+lang+"." )||r.Name.EndsWith("."+lang+".json"))) {
  using var doc=JsonDocument.Parse(resource.GetResourceStream(),new JsonDocumentOptions {CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true});
  if(doc.RootElement.TryGetProperty("ItemName",out var names)) foreach(var n in names.EnumerateObject()) result[n.Name]=n.Value.GetString()??n.Name;
 }
 return result;
}
var ru=Names("ru-RU"); var en=Names("en-US");
string Resolve(string key,Dictionary<string,string> names) { string value=names.GetValueOrDefault(key,en.GetValueOrDefault(key,key)); for(int i=0;i<8 && value.Contains("{$ItemName.");i++) value=Regex.Replace(value,@"\{\$ItemName\.([^}]+)\}",m=>names.GetValueOrDefault(m.Groups[1].Value,en.GetValueOrDefault(m.Groups[1].Value,m.Groups[1].Value))); return string.IsNullOrWhiteSpace(value)?key:value; }
var items=asm.MainModule.GetType("Terraria.ID.ItemID").Fields.Where(f=>f.HasConstant && f.Name!="Count").Select(f=>new {Id=Convert.ToInt32(f.Constant),Key=f.Name}).Where(f=>f.Id>0).GroupBy(f=>f.Id).Select(g=>g.First()).Select(f=>new {f.Id,f.Key,Name=Resolve(f.Key,ru),English=Resolve(f.Key,en)}).OrderBy(f=>f.Id).ToArray();
Directory.CreateDirectory("src/Assets"); File.WriteAllText("src/Assets/items.json",JsonSerializer.Serialize(items,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
Console.WriteLine($"Extracted {items.Length} items, ru={ru.Count}, en={en.Count}");
foreach(var f in asm.MainModule.GetType("Terraria.Main").Fields.Where(f=>f.Name.Contains("GameViewMatrix")||f.Name=="screenPosition")) Console.WriteLine(f.FullName);

