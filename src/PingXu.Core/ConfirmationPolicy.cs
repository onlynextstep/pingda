using System.Security.Cryptography;
using System.Text;

namespace PingXu.Core;

public enum ConfirmationMode { Smart, Always, Never }

public record ConfirmationOptions(ConfirmationMode Mode = ConfirmationMode.Smart, int TimeoutSeconds = 20)
{
    /// <summary>未知模式或非20/30/60秒抛出参数异常，绝不将未知值解释成 Never。</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode), "未知的显示确认模式。");
        if (TimeoutSeconds is not (20 or 30 or 60))
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), "确认时间只能是20、30或60秒。");
    }
}

/// <summary>CanRemember 只表示本次人工确认成功后可以记忆，不表示已经写入信任。</summary>
public record ConfirmationDecision(bool RequiresConfirmation, int TimeoutSeconds, string? TrustKey, bool CanRemember);

/// <summary>
/// 纯函数：不读写设置、不调用显示API、不提交事务、不增加信任。
/// Always/Never 仅选择确认流程，不取代事务的验证、快照、严格回读和失败恢复。
/// </summary>
public static class ConfirmationPolicy
{
    /// <summary>
    /// Smart 仅对唯一同ID且目标内容未编辑的已保存预设生成信任键。
    /// 预设ID按 ProfileStore 语义区分大小写；显示器ID按 OrdinalIgnoreCase 语义规范为大写。
    /// environment 是调用者提供的非空、不含控制字符的不透明环境指纹，精确比较，不裁剪或折叠大小写；
    /// 本方法无法证明指纹是否完整，应由调用者绑定机器/会话/驱动等所需环境变化。
    /// v1键为版本前缀加SHA256：长度前缀UTF8字符串和定长整数编码，集合按规范ID排序。
    /// 绑定预设ID、所有目标字段（保守包含disabled目标）、所有连接屏ID集合及environment；
    /// 不绑定名称、DISPLAY编号、可用模式列表、硬件当前启用/位置/方向，避免正常往返丢失信任。
    /// 选项非法或顶层必需参数为null抛参数异常；Smart候选不合法/不明确则要求确认且不可记忆。
    /// 未信任合格候选返回键和CanRemember=true；命中信任不再允许因自动提交增加信任。
    /// 调用者须提供在本次求值期间不被并发修改的集合，并仅在人工确认且提交成功后持久化键。
    /// </summary>
    public static ConfirmationDecision Evaluate(ConfirmationOptions options, DisplayProfile requested,
        IReadOnlyList<DisplayProfile> saved, IReadOnlyList<DisplayInfo> hardware, string? environment,
        IReadOnlyCollection<string> trusted)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(trusted);
        var closed = new ConfirmationDecision(true, options.TimeoutSeconds, null, false);
        if (options.Mode == ConfirmationMode.Always) return closed;
        if (options.Mode == ConfirmationMode.Never) return closed with { RequiresConfirmation = false };
        if (!ValidText(environment) || !ValidText(requested.Id) || hardware.Any(d => d is null)) return closed;

        var connected = hardware.Where(d => d.Connected).Select(d => d.Id).ToArray();
        if (connected.Length == 0 || connected.Any(id => !ValidText(id)) ||
            connected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != connected.Length) return closed;
        // Ambiguous saved identity must not accidentally grant trust to one of several versions.
        if (saved.Any(p => p is null)) return closed;
        var matches = saved.Where(p => string.Equals(p.Id, requested.Id, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) return closed;
        try
        {
            var requestBytes = Content(requested, hardware);
            if (!requestBytes.SequenceEqual(Content(matches[0], hardware))) return closed;
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, new UTF8Encoding(false, true), leaveOpen: true))
            {
                writer.Write("PingXu.Confirmation.v1");
                writer.Write(requestBytes.Length); writer.Write(requestBytes);
                writer.Write(connected.Length);
                foreach (var id in connected.Select(id => id.ToUpperInvariant()).Order(StringComparer.Ordinal)) writer.Write(id);
                writer.Write(environment!);
            }
            var key = "v1:" + Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
            var known = trusted.Any(value => string.Equals(value, key, StringComparison.Ordinal));
            return new(!known, options.TimeoutSeconds, key, !known);
        }
        catch (ArgumentException) { return closed; }
        catch (OverflowException) { return closed; }
    }

    private static byte[] Content(DisplayProfile profile, IReadOnlyList<DisplayInfo> hardware)
    {
        if (!ValidText(profile.Id) || profile.Displays is not { Count: > 0 and <= 32 } ||
            profile.Displays.Any(t => t is null || !ValidText(t.Id) || (!t.Enabled && t.Primary)))
            throw new ArgumentException("无法确定有效的预设内容。");
        var targets = profile.Displays.ToArray();
        // Name is display-only, including at this validation boundary.
        LayoutPlanner.Check(profile with { Name = "确认策略", Displays = targets.ToList() }, hardware);
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, new UTF8Encoding(false, true), leaveOpen: true))
        {
            writer.Write(profile.Id); writer.Write(targets.Length);
            foreach (var t in targets.OrderBy(t => t.Id.ToUpperInvariant(), StringComparer.Ordinal))
            {
                writer.Write(t.Id.ToUpperInvariant()); writer.Write(t.Enabled); writer.Write(t.Primary);
                writer.Write(t.X); writer.Write(t.Y); writer.Write(t.Width); writer.Write(t.Height);
                writer.Write(t.Rotation); writer.Write(t.RefreshRate);
            }
        }
        return buffer.ToArray();
    }

    private static bool ValidText(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 4096 && !value.Any(char.IsControl);
}
