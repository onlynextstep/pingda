var failures = 0;
void Test(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; Console.WriteLine($"FAIL {name}: {e.GetBaseException().Message}"); }
}
if (args.Length == 3 && args[0] is "--inspect-reenable" or "--probe-reenable-live")
    return ReenableDiagnostics.Run(args[1], args[2], args[0] == "--probe-reenable-live");
if (args.Length == 2 && args[0] == "--replay-snapshots")
    ReenableTests.ReplaySnapshots(args[1], Test);
else if (args.Length == 0) ReenableTests.Run(Test);
else throw new ArgumentException("Use --replay-snapshots <directory>, --inspect-reenable <snapshot> <profiles> or --probe-reenable-live <snapshot> <profiles>; no live apply exists here.");
Console.WriteLine($"Failed: {failures}");
return failures == 0 ? 0 : 1;
