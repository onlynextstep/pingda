using PingXu.Core;
var tests = new List<(string, Action)>();
void Test(string name, Action action) => tests.Add((name, action));
void Eq<T>(T actual,T expected) {if(!EqualityComparer<T>.Default.Equals(actual,expected)) throw new Exception($"expected {expected}, got {actual}");}
void Throws(Action action) {try {action();}catch(ArgumentException){return;} throw new Exception("expected validation rejection");}
var displays = new List<DisplayInfo> {
 new("4k","DISPLAY3","4K",true,true,false,-7280,0,3840,2160,0,60,[]),
 new("mid","DISPLAY1","带鱼",true,true,false,-3440,4,3440,1440,0,144,[]),
 new("right","DISPLAY2","带鱼",true,true,true,0,0,3440,1440,0,144,[])};
Test("portrait keeps right primary and closes left gap",()=> {var p=LayoutPlanner.Arrange(displays,"4k",90,false,false);Eq(p[0].Width,2160);Eq(p[0].Height,3840);Eq(p[0].X,-5600);Eq(p[1].X,-3440);Eq(p[2].X,0);Eq(p[2].Primary,true);});
Test("only 4k becomes primary at origin",()=>{var p=LayoutPlanner.Arrange(displays,"4k",270,true,false);Eq(p.Count(x=>x.Enabled),1);Eq(p[0].Primary,true);Eq(p[0].X,0);Eq(p[0].Rotation,270);});
Test("dual ultrawides disables 4k only",()=>{var p=LayoutPlanner.Arrange(displays,"4k",0,false,true);Eq(p[0].Enabled,false);Eq(p[1].Enabled,true);Eq(p[2].Enabled,true);Eq(p[1].X,-3440);});
Test("rotating already portrait does not double swap",()=>{var d=displays.ToList();d[0]=d[0] with {Width=2160,Height=3840,Rotation=90};var p=LayoutPlanner.Arrange(d,"4k",270,false,false);Eq(p[0].Width,2160);Eq(p[0].Height,3840);});
Test("all off rejected",()=>Throws(()=>LayoutPlanner.Check(new("p","test",displays.Select(d=>new DisplayTarget(d.Id,false,false,0,0,d.Width,d.Height,0,60)).ToList()),displays)));
Test("missing display rejected",()=>Throws(()=>LayoutPlanner.Check(new("p","test",[new("absent",true,true,0,0,1920,1080,0,60)]),displays)));
Test("duplicate identity rejected",()=>Throws(()=>LayoutPlanner.Check(new("p","test",[new("4k",true,true,0,0,3840,2160,0,60),new("4k",true,false,3840,0,3840,2160,0,60)]),displays)));
Test("invalid angle rejected",()=>Throws(()=>LayoutPlanner.Check(new("p","test",[new("4k",true,true,0,0,3840,2160,45,60)]),displays)));
Test("timeout wins over late confirmation",()=>{var t=new TrialState(20);Eq(t.Confirm(21),false);Eq(t.Status,TrialStatus.Revert);});
Test("confirmed trial never reverts from old timer",()=>{var t=new TrialState(20);Eq(t.Confirm(19),true);t.Tick(30);Eq(t.Status,TrialStatus.Keep);});
Test("cancel is terminal",()=>{var t=new TrialState(20);t.Cancel();Eq(t.Confirm(1),false);Eq(t.Status,TrialStatus.Revert);});
Test("profile store roundtrip",()=>{var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());try{var store=new ProfileStore(dir);var p=new DisplayProfile("unique","我的场景",LayoutPlanner.Arrange(displays,"4k",0,false,false));store.Save([p]);Eq(store.Load().Single().Name,"我的场景");}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}});
TransactionTests.Register(Test);
ConfirmationPolicyTests.Register(Test);
DirectConfirmationTransactionTests.Register(Test);
GenericLayoutTests.Register(Test);
DefaultProfileFactoryTests.Register(Test);
DisplayLimitTests.Register(Test);
ConfigurationBackupTests.Register(Test);
DiagnosticReportTests.Register(Test);
Test("import rejects negative dimensions before rendering",()=>Throws(()=>ProfileStore.Parse(System.Text.Json.JsonSerializer.Serialize(new[]{new DisplayProfile("x","bad",[new("4k",true,true,0,0,-1,2160,0,60)])}))));
Test("import rejects duplicate monitor identities",()=>Throws(()=>ProfileStore.Parse(System.Text.Json.JsonSerializer.Serialize(new[]{new DisplayProfile("x","bad",[new("4k",true,true,0,0,3840,2160,0,60),new("4K",false,false,0,0,3840,2160,0,60)])}))));
Test("import rejects no active display",()=>Throws(()=>ProfileStore.Parse(System.Text.Json.JsonSerializer.Serialize(new[]{new DisplayProfile("x","bad",[new("4k",false,false,0,0,3840,2160,0,60)])}))));
int failed=0;foreach(var (name,test) in tests){try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}Console.WriteLine($"{tests.Count-failed}/{tests.Count} passed");return failed==0?0:1;
