using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DeskRealm.App.Services;

internal sealed class VirtualDesktopRegistryService
{
    private const string VirtualDesktopsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const string SessionInfoKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo";

    private readonly FileLogger _logger;
    private readonly HashSet<string> _loggedCurrentDesktopSources = new(StringComparer.OrdinalIgnoreCase);
    // Explorer can briefly recreate the VirtualDesktops metadata tree after a deliberate
    // Explorer restart. Keep the last confirmed label per immutable GUID so an in-process
    // registry reconciliation never turns an established realm into a transient "Desktop N".
    private readonly ConcurrentDictionary<Guid, string> _confirmedDesktopNames = new();
    private readonly ConcurrentDictionary<Guid, byte> _fallbackNameWarnings = new();

    public VirtualDesktopRegistryService(FileLogger logger) => _logger = logger;

    public IReadOnlyList<VirtualDesktopInfo> GetVirtualDesktops()
    {
        using var key = Registry.CurrentUser.OpenSubKey(VirtualDesktopsKey, writable: false)
            ?? throw new InvalidOperationException($"Registry not found: HKCU\\{VirtualDesktopsKey}");

        if (key.GetValue("VirtualDesktopIDs") is not byte[] ids || ids.Length < 16)
        {
            throw new InvalidOperationException("VirtualDesktopIDs is missing or empty. Open Win+Tab and create at least one virtual desktop.");
        }

        if (ids.Length % 16 != 0)
        {
            throw new InvalidOperationException($"Invalid VirtualDesktopIDs: length {ids.Length}, expected multiple of 16.");
        }

        var list = new List<VirtualDesktopInfo>();
        for (var offset = 0; offset < ids.Length; offset += 16)
        {
            var buffer = new byte[16];
            Array.Copy(ids, offset, buffer, 0, 16);
            var id = new Guid(buffer);
            var number = list.Count + 1;
            var registryName = GetDesktopName(key, id);
            if (!string.IsNullOrWhiteSpace(registryName))
            {
                var confirmed = registryName.Trim();
                _confirmedDesktopNames[id] = confirmed;
                _fallbackNameWarnings.TryRemove(id, out _);
                list.Add(new VirtualDesktopInfo(id, confirmed, number));
                continue;
            }

            if (_confirmedDesktopNames.TryGetValue(id, out var cachedName))
            {
                if (_fallbackNameWarnings.TryAdd(id, 0))
                {
                    _logger.Warn(
                        $"Explorer virtual-desktop name metadata is temporarily unavailable for {id:B}; " +
                        $"retaining the last confirmed label '{cachedName}' during shell recovery.");
                }

                list.Add(new VirtualDesktopInfo(id, cachedName, number));
                continue;
            }

            // This is the initial Windows default label, or metadata that has not settled yet.
            // The flag lets higher-level code distinguish it from an actual Registry-confirmed name.
            var fallback = $"Desktop {number}";
            if (_fallbackNameWarnings.TryAdd(id, 0))
            {
                _logger.Warn(
                    $"Explorer did not expose a stored name for virtual desktop {id:B}; using provisional label '{fallback}'. " +
                    "DeskRealm will not treat this as a confirmed rename.");
            }

            list.Add(new VirtualDesktopInfo(id, fallback, number, NameIsFallback: true));
        }

        return list;
    }

    public Guid GetCurrentVirtualDesktopId()
    {
        var candidates = GetCurrentVirtualDesktopCandidates();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(BuildCurrentDesktopError("No usable registry source provided CurrentVirtualDesktop."));
        }

        return candidates[0].Id;
    }

    public VirtualDesktopInfo GetCurrentVirtualDesktop()
    {
        var desktops = GetVirtualDesktops();
        var desktopIds = desktops.Select(d => d.Id).ToHashSet();
        var candidates = GetCurrentVirtualDesktopCandidates();

        foreach (var candidate in candidates)
        {
            var current = desktops.FirstOrDefault(d => d.Id == candidate.Id);
            if (current is not null)
            {
                LogCurrentDesktopSourceOnce(candidate.Source);
                return current;
            }

            _logger.Warn($"CurrentVirtualDesktop ignored: {candidate.Id:B} from {candidate.Source} is not present in VirtualDesktopIDs.");
        }

        var knownIds = string.Join(", ", desktopIds.Select(id => id.ToString("B")));
        var candidateIds = candidates.Count == 0
            ? "none"
            : string.Join(", ", candidates.Select(c => $"{c.Id:B} via {c.Source}"));

        throw new InvalidOperationException(BuildCurrentDesktopError(
            $"No valid CurrentVirtualDesktop matches VirtualDesktopIDs. Candidates: {candidateIds}. Known: {knownIds}"));
    }


    public void SetDesktopName(Guid desktopId, string name)
    {
        if (desktopId == Guid.Empty) throw new InvalidOperationException("Cannot rename an empty Windows virtual-desktop GUID.");
        var normalized = string.IsNullOrWhiteSpace(name) ? throw new InvalidOperationException("A Windows virtual desktop name cannot be empty.") : name.Trim();
        using var key = Registry.CurrentUser.OpenSubKey(VirtualDesktopsKey, writable: true)
            ?? throw new InvalidOperationException($"Registry not found: HKCU\\{VirtualDesktopsKey}");

        foreach (var formattedId in new[] { desktopId.ToString("B"), desktopId.ToString("D"), desktopId.ToString("N") })
        {
            using var desktopKey = key.OpenSubKey($@"Desktops\{formattedId}", writable: true);
            if (desktopKey is null) continue;
            desktopKey.SetValue("Name", normalized, RegistryValueKind.String);
            _confirmedDesktopNames[desktopId] = normalized;
            _fallbackNameWarnings.TryRemove(desktopId, out _);
            _logger.Info($"Windows virtual desktop name persisted in Explorer Registry metadata: {desktopId:B} -> '{normalized}'.");
            return;
        }

        throw new InvalidOperationException($"Windows desktop metadata key was not found for {desktopId:B}. Open Win+Tab once and retry.");
    }

    private IReadOnlyList<CurrentDesktopCandidate> GetCurrentVirtualDesktopCandidates()
    {
        var candidates = new List<CurrentDesktopCandidate>();
        var diagnostics = new List<string>();

        TryAddCandidate(candidates, diagnostics, VirtualDesktopsKey, "CurrentVirtualDesktop", "Explorer\\VirtualDesktops");

        var sessionId = Process.GetCurrentProcess().SessionId;
        var currentSessionPath = $@"{SessionInfoKey}\{sessionId}\VirtualDesktops";
        TryAddCandidate(candidates, diagnostics, currentSessionPath, "CurrentVirtualDesktop", $"Explorer\\SessionInfo\\{sessionId}\\VirtualDesktops");

        // Windows builds differ. Some expose CurrentVirtualDesktop only under SessionInfo, and the active
        // numeric session is not always the same value Process.SessionId reports inside every launch context.
        // This is still strict: we never choose D1/default silently; we only accept GUIDs that match VirtualDesktopIDs.
        using var sessionInfo = Registry.CurrentUser.OpenSubKey(SessionInfoKey, writable: false);
        if (sessionInfo is not null)
        {
            foreach (var subKeyName in sessionInfo.GetSubKeyNames())
            {
                if (string.Equals(subKeyName, sessionId.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var path = $@"{SessionInfoKey}\{subKeyName}\VirtualDesktops";
                TryAddCandidate(candidates, diagnostics, path, "CurrentVirtualDesktop", $"Explorer\\SessionInfo\\{subKeyName}\\VirtualDesktops");
            }
        }
        else
        {
            diagnostics.Add($"HKCU\\{SessionInfoKey} not found.");
        }

        if (candidates.Count == 0)
        {
            _logger.Warn("CurrentVirtualDesktop probes failed: " + string.Join(" | ", diagnostics));
        }

        return candidates
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .ToList();
    }

    private static string BuildCurrentDesktopError(string reason)
    {
        return reason + Environment.NewLine +
               "DeskRealm does not switch to any default realm. Open Win+Tab once, verify that at least two virtual desktops exist, then run Refresh now again.";
    }

    private void TryAddCandidate(
        List<CurrentDesktopCandidate> candidates,
        List<string> diagnostics,
        string keyPath,
        string valueName,
        string source)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        if (key is null)
        {
            diagnostics.Add($"HKCU\\{keyPath} not found.");
            return;
        }

        var raw = key.GetValue(valueName);
        if (!TryParseGuidValue(raw, out var id, out var detail))
        {
            diagnostics.Add($"HKCU\\{keyPath}\\{valueName} invalid: {detail}.");
            return;
        }

        candidates.Add(new CurrentDesktopCandidate(id, source));
    }

    private static bool TryParseGuidValue(object? raw, out Guid id, out string detail)
    {
        id = Guid.Empty;

        switch (raw)
        {
            case null:
                detail = "missing value";
                return false;

            case byte[] bytes when bytes.Length == 16:
                id = new Guid(bytes);
                detail = "REG_BINARY 16 bytes";
                return true;

            case byte[] bytes:
                detail = $"REG_BINARY length {bytes.Length}, expected 16";
                return false;

            case string text when TryParseGuidText(text, out id):
                detail = "REG_SZ guid/hex";
                return true;

            case string text:
                detail = $"REG_SZ illisible '{text}'";
                return false;

            default:
                detail = $"unsupported type {raw.GetType().FullName}";
                return false;
        }
    }

    private static bool TryParseGuidText(string value, out Guid id)
    {
        id = Guid.Empty;
        var trimmed = value.Trim();

        if (Guid.TryParse(trimmed, out id))
        {
            return true;
        }

        var normalized = trimmed
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("{", string.Empty, StringComparison.Ordinal)
            .Replace("}", string.Empty, StringComparison.Ordinal);

        if (normalized.Length != 32)
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromHexString(normalized);
            if (bytes.Length != 16)
            {
                return false;
            }

            id = new Guid(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void LogCurrentDesktopSourceOnce(string source)
    {
        if (_loggedCurrentDesktopSources.Add(source))
        {
            _logger.Info($"CurrentVirtualDesktop source active: {source}");
        }
    }

    private static string? GetDesktopName(RegistryKey virtualDesktopsKey, Guid id)
    {
        foreach (var formattedId in new[] { id.ToString("B"), id.ToString("D"), id.ToString("N") })
        {
            using var nameKey = virtualDesktopsKey.OpenSubKey($@"Desktops\{formattedId}", writable: false);
            if (nameKey?.GetValue("Name") is string name && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        return null;
    }

    private sealed record CurrentDesktopCandidate(Guid Id, string Source);
}
