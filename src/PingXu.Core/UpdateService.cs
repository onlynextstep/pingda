using System.Security.Cryptography;
using System.Text.Json;

namespace PingXu.Core;

public sealed record UpdateRelease(string Version, string Notes, string Url, long Size, string Sha256);

/// <summary>Checks HTTPS release metadata and downloads a bounded, hash-verified installer. Never executes files.</summary>
public sealed class UpdateService(HttpClient http)
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    const long MaximumSize = 512L * 1024 * 1024;
    public static void Validate(UpdateRelease release)
    {
        if (!Version.TryParse(release.Version, out var v) || v.Build < 0 || v.Revision > 0 ||
            release.Size < 2 || release.Size > MaximumSize || release.Sha256 is not { Length: 64 } ||
            !release.Sha256.All(Uri.IsHexDigit) || release.Notes is null || release.Notes.Length > 12000)
            throw new InvalidDataException("更新信息不完整，请稍后重试。");
        SecureUri(release.Url);
    }
    static Uri SecureUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
            throw new InvalidDataException("更新地址必须使用 HTTPS。");
        return uri;
    }
    async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SecureUri(uri.ToString()));
            request.Headers.UserAgent.ParseAdd("PingDa-Updater/0.6.1");
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            SecureUri(response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString());
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location == null) throw new InvalidDataException("更新下载地址缺失。");
            uri = SecureUri(new Uri(uri, location).ToString());
        }
        throw new InvalidDataException("更新地址重定向次数过多。");
    }
    public async Task<UpdateRelease?> CheckAsync(Uri endpoint, Version current, CancellationToken cancellationToken = default)
    {
        SecureUri(endpoint.ToString());
        using var response = await GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();
        SecureUri(response.RequestMessage?.RequestUri?.ToString() ?? endpoint.ToString());
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var data = new MemoryStream();
        var buffer = new byte[4096]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (data.Length + count > 65536) throw new InvalidDataException("更新信息过大。");
            data.Write(buffer, 0, count);
        }
        var release = JsonSerializer.Deserialize<UpdateRelease>(data.ToArray(), Json) ?? throw new InvalidDataException("无法读取更新信息。");
        Validate(release);
        var version = Version.Parse(release.Version);
        return new Version(version.Major, version.Minor, version.Build) > new Version(current.Major,current.Minor,Math.Max(current.Build,0)) ? release : null;
    }
    public async Task<string> DownloadAsync(UpdateRelease release, string directory, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        Validate(release);
        using var response = await GetAsync(SecureUri(release.Url), cancellationToken);
        response.EnsureSuccessStatusCode();
        SecureUri(response.RequestMessage?.RequestUri?.ToString() ?? release.Url);
        if (response.Content.Headers.ContentLength is long size && size != release.Size)
            throw new InvalidDataException("安装包大小不符，请重新下载。");
        Directory.CreateDirectory(directory);
        var partial = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("安装包大小不符，请重新下载。");
                    await file.WriteAsync(buffer.AsMemory(0,count), cancellationToken);
                    progress?.Report((int)(total * 100 / release.Size));
                }
                if (total != release.Size) throw new InvalidDataException("下载不完整，请重新下载。");
            }
            await VerifyAsync(partial, release, cancellationToken);
            var destination = Path.ChangeExtension(partial, ".exe");
            File.Move(partial, destination);
            return destination;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    public static async Task VerifyAsync(string path, UpdateRelease release, CancellationToken token = default)
    {
        Validate(release);
        using var file = File.OpenRead(path);
        if (file.Length != release.Size || file.ReadByte() != 77 || file.ReadByte() != 90)
            throw new InvalidDataException("安装包无效，请重新下载。");
        file.Position = 0;
        if (!Convert.ToHexString(await SHA256.HashDataAsync(file,token)).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装包校验失败，请重新下载。");
    }
}
