using Microsoft.Win32;

namespace PingXu.App;

/// <summary>Read-only trust boundary. Failure disables remembered-profile shortcuts, never validation.</summary>
public static class DisplayEnvironment
{
    public static string? Capture()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (root == null) return null;
            var drivers = new List<string>();
            // The display class also has a protected Properties branch; it is not a driver instance.
            // Only 0000..9999 contain the driver metadata relevant to this fingerprint.
            foreach (var name in root.GetSubKeyNames().Where(IsDriverInstance).Order(StringComparer.Ordinal))
            {
                using var driver = root.OpenSubKey(name);
                var version = driver?.GetValue("DriverVersion") as string;
                if (!string.IsNullOrWhiteSpace(version))
                    drivers.Add($"{name}:{driver!.GetValue("MatchingDeviceId")}:{version}:{driver.GetValue("DriverDate")}");
            }
            return drivers.Count == 0 ? null : "PingXu-confirmation-v1|" + Environment.OSVersion.Version + "|" + string.Join("|", drivers);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        { Program.Log(e); return null; }
    }
    public static bool IsDriverInstance(string name) => name.Length == 4 && name.All(c => c is >= '0' and <= '9');
}
