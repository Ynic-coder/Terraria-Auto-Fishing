using Mono.Cecil;
var asm = AssemblyDefinition.ReadAssembly(args[0]);
foreach(var type in new[]{asm.MainModule.GetType("Terraria.Player"),asm.MainModule.GetType("Terraria.Main")}) {
 foreach(var m in type.Methods.Where(m=>m.HasBody && (m.Name=="ItemCheck" || m.Name=="ItemCheck_Inner" || m.Name=="Update"))) {
  Console.WriteLine("METHOD "+m.FullName);
  var il=m.Body.Instructions;
  var indices=new HashSet<int>();
  for(int k=0;k<il.Count;k++) if(k<35 || il[k].ToString().Contains("delayUseItem") || il[k].ToString().Contains("FocusHelper") || il[k].ToString().Contains("controlUseItem") || il[k].ToString().Contains("releaseUseItem")) for(int j=Math.Max(0,k-5);j<Math.Min(il.Count,k+9);j++) indices.Add(j);
  foreach(var k in indices.OrderBy(k=>k)) Console.WriteLine(il[k]);
 }
}
