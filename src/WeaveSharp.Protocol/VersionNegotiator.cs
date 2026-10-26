using System;
using System.Collections.Generic;
using System.Linq;

namespace WeaveSharp.Protocol;

public sealed record NegotiationResult(ProtocolVersion? Version, string? Refusal,
    IReadOnlyDictionary<string, ProtocolExtension> EnabledExtensions, IReadOnlyList<string> IgnoredExtensions)
{
    public bool Accepted => Version is not null && Refusal is null;
}

public static class VersionNegotiator
{
    public static NegotiationResult Negotiate(IReadOnlyList<ProtocolVersion> localVersions,
        IReadOnlyList<ProtocolVersion> peerVersions, IReadOnlySet<string> knownExtensions,
        IReadOnlyDictionary<string, ProtocolExtension> offeredExtensions)
    {
        var enabled = new Dictionary<string, ProtocolExtension>(StringComparer.Ordinal);
        var ignored = new List<string>();
        foreach (var (name, extension) in offeredExtensions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (knownExtensions.Contains(name))
                enabled.Add(name, extension);
            else if (extension.Mandatory)
                return new(null, $"Unsupported required extension: {name}",
                    new Dictionary<string, ProtocolExtension>(), []);
            else
                ignored.Add(name);
        }

        var version = localVersions.Intersect(peerVersions)
            .OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).FirstOrDefault();
        return version is null
            ? new(null, "The offered version sets do not intersect.", new Dictionary<string, ProtocolExtension>(), [])
            : new(version, null, enabled, ignored);
    }
}
