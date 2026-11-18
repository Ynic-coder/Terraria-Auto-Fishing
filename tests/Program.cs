using Fishing;
int passed=0;
void Check(bool condition, string description) { if(!condition) throw new Exception(description); passed++; }
FishState waiting = new(false,false,false,false,false,true,42,1,false,0,0,0,"Test");
FishingLogic Logic() { var x=new FishingLogic(); x.Reset(0); return x; }
var logic=Logic();
Check(logic.Evaluate(waiting,true,5000).Action==ActionKind.None,"No click while waiting");
var bite=waiting with {Bite=true, Ai1=-100, CatchId=1};
Check(logic.Evaluate(bite,true,5000).Action==ActionKind.None,"Reaction delay");
Check(logic.Evaluate(bite,true,5080).Action==ActionKind.Hook,"Hook exact bite");
logic.Clicked(ActionKind.Hook,5080);
Check(logic.Evaluate(bite,true,6000).Action==ActionKind.None,"Do not hook twice");
Check(logic.Evaluate(bite,true,12000).Stop,"Stop if bobber never returns");
foreach(var blocked in new[]{bite with{Menu=true},bite with{Paused=true},bite with{Dead=true},bite with{Inventory=true},bite with{HasRod=false},bite with{Busy=true}}) {
 var l=Logic(); l.Evaluate(blocked,true,5000); Check(l.Evaluate(blocked,true,6000).Action==ActionKind.None,"Blocked game state");
}
logic=Logic(); logic.Evaluate(bite,false,5000); Check(logic.Evaluate(bite,false,6000).Action==ActionKind.None,"Foreground gate");
var empty=waiting with{Bobbers=0}; logic=Logic();
Check(logic.Evaluate(empty,true,3000).Action==ActionKind.None,"Recast delay begins on disappearance");
Check(logic.Evaluate(empty,true,3800).Action==ActionKind.Cast,"Recast once ready");
logic.Clicked(ActionKind.Cast,3800);
Check(logic.Evaluate(empty,true,4000).Action==ActionKind.None,"Prevent rapid casts");
Check(logic.Evaluate(empty,true,6300).Action==ActionKind.Cast,"Retry missing bobber");
logic.Clicked(ActionKind.Cast,6300); logic.Evaluate(empty,true,7000); logic.Clicked(ActionKind.Cast,9000); logic.Evaluate(empty,true,9500);
Check(logic.Evaluate(empty,true,12000).Stop,"Stop failed casting loop");
Check(Logic().Evaluate(empty with{Bait=0},true,5000).Stop,"Stop without bait");
logic=Logic(); logic.AutoCast=false; logic.Evaluate(empty,true,5000);
Check(logic.Evaluate(empty,true,10000).Action==ActionKind.None,"Manual cast mode");
logic=Logic(); logic.Evaluate(bite,true,5000); logic.Evaluate(bite,false,5050);
Check(logic.Evaluate(bite,true,6000).Action==ActionKind.None,"Discard stale bite delay on focus change");
logic=Logic(); logic.Background=true;
Check(logic.Evaluate(bite,false,5000).Action==ActionKind.None,"Background reaction delay");
Check(logic.Evaluate(bite,false,5080).Action==ActionKind.Hook,"Hook without foreground");
logic.Clicked(ActionKind.Hook,5080);
Check(logic.Evaluate(bite,false,6000).Action==ActionKind.None,"No duplicate background hook");
logic=Logic(); logic.Background=true; logic.Evaluate(empty,false,3000);
Check(logic.Evaluate(empty,false,3800).Action==ActionKind.Cast,"Cast without foreground");
foreach(var blocked in new[]{bite with{Menu=true},bite with{Paused=true},bite with{Dead=true},bite with{Inventory=true},bite with{HasRod=false},bite with{Busy=true}}) {
 var l=Logic(); l.Background=true; l.Evaluate(blocked,false,5000); Check(l.Evaluate(blocked,false,6000).Action==ActionKind.None,"Background retains gameplay guards");
}
logic=Logic(); logic.Background=true; logic.Evaluate(bite,true,5000);
Check(logic.Evaluate(bite,false,5080).Action==ActionKind.Hook,"Focus change does not reset background bite");
Console.WriteLine($"PASS: {passed} state-machine checks");

