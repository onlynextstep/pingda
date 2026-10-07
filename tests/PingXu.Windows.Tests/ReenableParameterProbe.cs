using System.Text.Json;
using PingXu.Core;
using PingXu.Windows;

internal static class ReenableParameterProbe
{
    internal static int Run(string profilesPath, string snapshotPath)
    {
        var api = new LiveValidation.ValidateOnlyApi(new Win32CcdApi());
        var before = api.Read();
        var saved = SnapshotCodec.Decode(JsonSerializer.Deserialize<DesktopSnapshot>(File.ReadAllText(snapshotPath), ProfileStore.Json)!.NativeData);
        foreach (var profile in ProfileStore.Parse(File.ReadAllText(profilesPath)))
        {
            var plan = CcdLogic.Build(before, profile);
            void Check(string label, Action<PathInfo[], List<ModeInfo>>? modify = null)
            {
                var paths = plan.Paths.ToArray(); var modes = plan.Modes.ToList();
                modify?.Invoke(paths, modes);
                Console.WriteLine($"{profile.Name} / {label}: {api.Set(new(paths, modes.ToArray()), 0x60)}");
            }
            bool Inactive(PathInfo p) => !before.Paths.Any(old => CcdLogic.Active(old) && CcdLogic.Target(old) == CcdLogic.Target(p));
            Check("production");
            Check("legacy unspecified scanline", (paths, _) =>
            {
                for (int i = 0; i < paths.Length; i++)
                    if (Inactive(paths[i]) && paths[i].Target.ModeIndex == uint.MaxValue)
                        paths[i].Target.ScanLineOrdering = 0;
            });
            Check("explicit measured target timing", (paths, modes) =>
            {
                for (int i = 0; i < paths.Length; i++) if (Inactive(paths[i]))
                {
                    var id = before.Outputs.Single(o => o.Target == CcdLogic.Target(paths[i])).Id;
                    var output = saved.Outputs.Single(o => o.Id == id);
                    var old = saved.Paths.Single(p => CcdLogic.Active(p) && CcdLogic.Target(p) == output.Target);
                    var mode = CcdLogic.Mode(saved, old, false);
                    mode.Id = paths[i].Target.Id; mode.AdapterId = paths[i].Target.AdapterId;
                    paths[i].Target.ModeIndex = (uint)modes.Count; modes.Add(mode);
                }
            });
            Check("progressive scanline", (paths, _) => { for(int i=0;i<paths.Length;i++) if(Inactive(paths[i])) paths[i].Target.ScanLineOrdering=1; });
            Check("identity scaling", (paths, _) => { for(int i=0;i<paths.Length;i++) if(Inactive(paths[i])) paths[i].Target.Scaling=1; });
            Check("observed rational refresh", (paths, _) =>
            {
                for(int i=0;i<paths.Length;i++) if(Inactive(paths[i]))
                {
                    var id=before.Outputs.Single(o=>o.Target==CcdLogic.Target(paths[i])).Id;
                    var output=saved.Outputs.Single(o=>o.Id==id);
                    paths[i].Target.RefreshRate=saved.Paths.Single(p=>CcdLogic.Active(p)&&CcdLogic.Target(p)==output.Target).Target.RefreshRate;
                }
            });
            if (plan.Paths.Count(Inactive) == 1)
            {
                int index=Array.FindIndex(plan.Paths,p=>Inactive(p));
                foreach(var candidate in before.Paths.Where(p=>CcdLogic.Target(p)==CcdLogic.Target(plan.Paths[index]) && p.Target.Available!=0
                    && !plan.Paths.Where((_,i)=>i!=index).Any(other=>CcdLogic.Source(other)==CcdLogic.Source(p))))
                    Check("source route "+candidate.Source.Id,(paths,modes)=>
                    {
                        paths[index].Source.Id=candidate.Source.Id;
                        paths[index].Source.AdapterId=candidate.Source.AdapterId;
                        int mi=(int)paths[index].Source.ModeIndex; var mode=modes[mi]; mode.Id=candidate.Source.Id; mode.AdapterId=candidate.Source.AdapterId; modes[mi]=mode;
                    });
            }
        }
        var after=api.Read();
        if(!SnapshotCodec.Bytes(before.Paths).SequenceEqual(SnapshotCodec.Bytes(after.Paths)) || !SnapshotCodec.Bytes(before.Modes).SequenceEqual(SnapshotCodec.Bytes(after.Modes))) throw new Exception("Native state changed during validation");
        Console.WriteLine("Native state unchanged. Strict validation only.");
        return 0;
    }
}
