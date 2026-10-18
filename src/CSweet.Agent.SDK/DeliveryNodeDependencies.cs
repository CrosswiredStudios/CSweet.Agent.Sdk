using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CSweet.Agent.SDK.Compute;

namespace CSweet.Agent.SDK;

/// <summary>Reads exact candidate lockfile identities; network and registry authority remain provider-enforced.</summary>
public static class DeliveryNodeDependencies
{
    public static string LockfileHash(byte[] sourceArchive)
    {
        using var zip = new ZipArchive(new MemoryStream(sourceArchive), ZipArchiveMode.Read);
        var entry = zip.GetEntry("package-lock.json") ?? throw new InvalidDataException("Dependency restore requires package-lock.json.");
        if (entry.Length > 1024 * 1024) throw new InvalidDataException("The dependency lockfile exceeds its bound.");
        using var stream = entry.Open();
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    public static ComputeNodeDependencies Read(byte[] sourceArchive)
    {
        try { return ReadCore(sourceArchive); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("The current source lockfile is invalid.", error); }
    }
    private static ComputeNodeDependencies ReadCore(byte[] sourceArchive)
    {
        using var zip = new ZipArchive(new MemoryStream(sourceArchive), ZipArchiveMode.Read);
        var entry = zip.GetEntry("package-lock.json") ?? throw new InvalidDataException("Managed dependency restore requires package-lock.json.");
        if (entry.Length > 1024 * 1024) throw new InvalidDataException("The dependency lockfile exceeds its bound.");
        using var bytes = new MemoryStream(); using (var stream = entry.Open()) stream.CopyTo(bytes);
        using var document = JsonDocument.Parse(bytes.ToArray()); var root = document.RootElement;
        if (!root.TryGetProperty("lockfileVersion", out var version) || version.GetInt32() is not (2 or 3) ||
            !root.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Use an npm lockfile version 2 or 3 with exact package integrity.");
        var result = new Dictionary<string, ComputeNodePackage>(StringComparer.Ordinal);
        foreach (var item in packages.EnumerateObject().Where(p => p.Name != ""))
        {
            if (!item.Value.TryGetProperty("resolved", out var resolved) || resolved.ValueKind != JsonValueKind.String ||
                !item.Value.TryGetProperty("integrity", out var integrity) || integrity.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Every dependency needs a pinned registry tarball and integrity.");
            var url = resolved.GetString()!; var sri = integrity.GetString()!;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "registry.npmjs.org" ||
                uri.Port != 443 || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" || !uri.AbsolutePath.EndsWith(".tgz", StringComparison.Ordinal) ||
                sri.Length != 95 || !sri.StartsWith("sha512-", StringComparison.Ordinal))
                throw new InvalidDataException("Managed restore requires SHA-512-pinned public npm tarballs; other registries need a supported provider.");
            try { if (Convert.FromBase64String(sri[7..]).Length != 64) throw new FormatException(); }
            catch (FormatException) { throw new InvalidDataException("The lockfile contains invalid package integrity."); }
            if (result.TryGetValue(url, out var previous) && previous.Integrity != sri)
                throw new InvalidDataException("Conflicting dependency integrity.");
            result[url] = new(url, sri);
            if (result.Count > 256) throw new InvalidDataException("The dependency set exceeds the managed restore bound.");
        }
        return new(Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())), result.Values.OrderBy(p => p.Url, StringComparer.Ordinal).ToArray());
    }
}
