using PingXu.Core;
static class TransactionTests
{
 public static void Register(Action<string,Action> test)
 {
  void Assert(bool value){if(!value)throw new Exception("transaction assertion failed");}
  var old=new DesktopSnapshot([new("a","D1","Screen",true,true,true,0,0,1920,1080,0,60,[])],"exact-original");
  var next=new DisplayProfile("p","portrait",[new("a",true,true,0,0,1080,1920,90,60)]);
  test("seven connected screens reject transaction before any display or recovery writes",()=>
  {
   var hardware=GenericLayoutTests.Desk(7).Select((d,i)=>d with{Enabled=i==0,Primary=i==0}).ToList();
   var snapshot=new DesktopSnapshot(hardware,"seven-original");
   var profile=new DisplayProfile("one","单屏",[new("panel-0",true,true,0,0,2560,1600,0,75)]);
   var fake=new Fake(snapshot);var journaled=false;var saved=false;
   var tx=new DisplayTransaction(fake,fake.RestorePersistent,_=>journaled=true,_=>saved=true);
   var result=tx.Begin(snapshot,profile);
   Assert(!result.Success && result.Message.Contains('7') && result.Message.Contains('6'));
   Assert(fake.Applies.Count==0 && fake.Restored==null && fake.Current==snapshot);
   Assert(!journaled && !saved && tx.SafeToContinue);
  });
  test("transaction rejects stale preflight before mutation",()=>{var f=new Fake(old);var tx=new DisplayTransaction(f,f.RestorePersistent,_=>{},_=>{});f.Current=old with{Displays=[old.Displays[0] with{RefreshRate=75}]};Assert(!tx.Begin(old,next).Success);Assert(f.Applies.Count==0);});
  foreach(var connected in new[]{true,false})
   test($"trial adds inactive seventh record connected={connected} before confirmation",()=>
   {
    var hardware=GenericLayoutTests.Desk(6);
    var snapshot=new DesktopSnapshot(hardware,"six-original-native");
    var profile=new DisplayProfile("six","六屏旋转",LayoutPlanner.ArrangeHorizontal(hardware,"panel-0",90));
    var fake=new Fake(snapshot);var saved=false;
    var tx=new DisplayTransaction(fake,fake.RestorePersistent,_=>{},_=>saved=true);
    Assert(tx.Begin(snapshot,profile).Success);
    fake.Current.Displays.Add(new("hotplug","D7","新屏幕",connected,false,false,0,0,0,0,0,0,[]));
    var result=tx.Finish(true);
    if(connected)
    {
     Assert(!result.Success);
     Assert(fake.Applies.SequenceEqual([false]) && !saved);
     Assert(fake.Restored==snapshot && fake.Current==snapshot && !fake.PersistRestore && tx.SafeToContinue);
     Assert(result.Message.Contains('7') && result.Message.Contains('6') && result.Message.Contains("已恢复"));
     Assert(!result.Message.Contains("未更改"));
    }
    else
    {
     Assert(result.Success && fake.Applies.SequenceEqual([false,true]) && saved);
     Assert(fake.Restored==null && tx.SafeToContinue);
    }
   });
  test("transaction cancel restores exact snapshot",()=>{var f=new Fake(old);var tx=new DisplayTransaction(f,f.RestorePersistent,_=>{},_=>{});Assert(tx.Begin(old,next).Success);Assert(tx.Finish(false).Success);Assert(f.Restored==old);Assert(!f.PersistRestore);});
  test("transaction backup failure prevents persistent apply",()=>{var f=new Fake(old);var tx=new DisplayTransaction(f,f.RestorePersistent,_=>{},_=>throw new IOException("disk full"));Assert(tx.Begin(old,next).Success);Assert(!tx.Finish(true).Success);Assert(f.Applies.SequenceEqual([false]));Assert(f.Restored==old);});
  test("failed persistent commit restores persistent original",()=>{var f=new Fake(old){FailPersist=true};var tx=new DisplayTransaction(f,f.RestorePersistent,_=>{},_=>{});Assert(tx.Begin(old,next).Success);Assert(!tx.Finish(true).Success);Assert(f.PersistRestore);Assert(f.Restored==old);});
  test("external layout change cannot silently commit",()=>{var f=new Fake(old);var tx=new DisplayTransaction(f,f.RestorePersistent,_=>{},_=>{});Assert(tx.Begin(old,next).Success);f.Current=old;Assert(!tx.Finish(true).Success);Assert(!f.Applies.Contains(true));});
  test("successful transaction commits once",()=>{var f=new Fake(old);var tx=new DisplayTransaction(f,f.RestorePersistent,_=>{},_=>{});Assert(tx.Begin(old,next).Success);Assert(tx.Finish(true).Success);Assert(!tx.Finish(false).Success);Assert(f.Restored==null);Assert(f.Applies.SequenceEqual([false,true]));});
  test("journal failure leaves hardware unchanged",()=>{var f=new Fake(old);var tx=new DisplayTransaction(f,f.RestorePersistent,_=>throw new IOException("disk full"),_=>{});Assert(!tx.Begin(old,next).Success);Assert(f.Applies.Count==0);Assert(f.Restored==null);});
  test("partial temporary apply failure rolls back",()=>{var f=new Fake(old){FailTemporary=true};var tx=new DisplayTransaction(f,f.RestorePersistent,_=>{},_=>{});Assert(!tx.Begin(old,next).Success);Assert(f.Restored==old);});
  test("pending trial stays locked until successful restore",()=>{var f=new Fake(old);var tx=new DisplayTransaction(f,f.RestorePersistent,_=>{},_=>{});Assert(tx.SafeToContinue);Assert(tx.Begin(old,next).Success);Assert(!tx.SafeToContinue);Assert(tx.Finish(false).Success);Assert(tx.SafeToContinue);});
  test("rollback failure remains locked",()=>{var f=new Fake(old);var tx=new DisplayTransaction(f,(_,_)=>OperationResult.Fail("driver lost"),_=>{},_=>{});Assert(tx.Begin(old,next).Success);Assert(!tx.Finish(false).Success);Assert(!tx.SafeToContinue);});
  test("rollback exception remains locked",()=>{var f=new Fake(old);var tx=new DisplayTransaction(f,(_,_)=>throw new IOException("driver lost"),_=>{},_=>{});Assert(tx.Begin(old,next).Success);Assert(!tx.Finish(false).Success);Assert(!tx.SafeToContinue);});
 }
 sealed class Fake(DesktopSnapshot snapshot):IDisplayService
 {
  public DesktopSnapshot Current=snapshot;
  public List<bool> Applies=[];
  public bool FailPersist,FailTemporary,PersistRestore;
  public DesktopSnapshot? Restored;
  public DesktopSnapshot Capture()=>Current;
  public OperationResult Validate(DisplayProfile p)=>OperationResult.Ok();
  public OperationResult Apply(DisplayProfile p,bool persist){Applies.Add(persist);Current=new(p.Displays.Select(d=>new DisplayInfo(d.Id,"D1","Screen",true,d.Enabled,d.Primary,d.X,d.Y,d.Width,d.Height,d.Rotation,d.RefreshRate,[])).ToList(),"changed");return (persist?FailPersist:FailTemporary)?OperationResult.Fail("driver failed"):OperationResult.Ok();}
  public OperationResult Restore(DesktopSnapshot s)=>RestorePersistent(s,false);
  public OperationResult RestorePersistent(DesktopSnapshot s,bool persist){Restored=s;PersistRestore=persist;Current=s;return OperationResult.Ok();}
 }
}
