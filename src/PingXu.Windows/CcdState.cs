using PingXu.Core;

namespace PingXu.Windows;

internal readonly record struct Endpoint(Luid AdapterId, uint Id);
internal sealed record Output(Endpoint Target, string Id, string Name, string DeviceName,
    DisplayMode[] Modes, VideoSignal? PreferredSignal = null);
internal sealed record CcdState(PathInfo[] Paths, ModeInfo[] Modes, Output[] Outputs);
internal sealed record CcdPlan(PathInfo[] Paths, ModeInfo[] Modes);
internal interface ICcdApi
{
    CcdState Read();
    int Set(CcdPlan plan, uint flags);
}
