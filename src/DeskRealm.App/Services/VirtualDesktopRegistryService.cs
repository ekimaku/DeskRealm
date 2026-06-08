using Microsoft.Win32;
using System.Diagnostics;

namespace DeskRealm.App.Services;

internal sealed class VirtualDesktopRegistryService
{
    private const string VirtualDesktopsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const string SessionInfoKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo";

    private readonly FileLogger _logger;
    private readonly HashSet<string> _loggedCurrentDesktopSources = new(StringComparer.OrdinalIgnoreCase);

    public VirtualDesktopRegistryService(FileLogger logger) => _logger = logger;

    public IReadOnlyList<VirtualDesktopInfo> GetVirtualDesktops()
    {
        using var key = Registry.CurrentUser.OpenSubKey(VirtualDesktopsKey, writable: false)
            ?? throw new InvalidOperationException($"Registry introuvable : HKCU\\{VirtualDesktopsKey}");

        if (key.GetValue("VirtualDesktopIDs") is not byte[] ids || ids.Length < 16)
        {
            throw new InvalidOperationException("VirtualDesktopIDs absent ou vide. Ouvre Win+Tab et crée au moins un bureau virtuel.");
        }

        if (ids.Length % 16 != 0)
        {
            throw new InvalidOperationException($"VirtualDesktopIDs invalide : longueur {ids.Length}, attendue multiple de 16.");
        }

        var list = new List<VirtualDesktopInfo>();
        for (var offset = 0; offset < ids.Length; offset += 16)
        {
            var buffer = new byte[16];
            Array.Copy(ids, offset, buffer, 0, 16);
            var id = new Guid(buffer);
            var number = list.Count + 1;
            var name = GetDesktopName(key, id) ?? $"Desktop {number}";
            list.Add(new VirtualDesktopInfo(id, name, number));
        }

        return list;
    }

    public Guid GetCurrentVirtualDesktopId()
    {
        var candidates = GetCurrentVirtualDesktopCandidates();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(BuildCurrentDesktopError("Aucune source registry exploitable n'a fourni CurrentVirtualDesktop."));
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

            _logger.Warn($"CurrentVirtualDesktop ignoré : {candidate.Id:B} depuis {candidate.Source} n'est pas présent dans VirtualDesktopIDs.");
        }

        var knownIds = string.Join(", ", desktopIds.Select(id => id.ToString("B")));
        var candidateIds = candidates.Count == 0
            ? "aucun"
            : string.Join(", ", candidates.Select(c => $"{c.Id:B} via {c.Source}"));

        throw new InvalidOperationException(BuildCurrentDesktopError(
            $"Aucun CurrentVirtualDesktop valide ne correspond à VirtualDesktopIDs. Candidates: {candidateIds}. Known: {knownIds}"));
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
            diagnostics.Add($"HKCU\\{SessionInfoKey} introuvable.");
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
               "DeskRealm ne bascule vers aucun realm par défaut. Ouvre Win+Tab une fois, vérifie qu'au moins deux bureaux virtuels existent, puis relance Refresh now.";
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
            diagnostics.Add($"HKCU\\{keyPath} introuvable.");
            return;
        }

        var raw = key.GetValue(valueName);
        if (!TryParseGuidValue(raw, out var id, out var detail))
        {
            diagnostics.Add($"HKCU\\{keyPath}\\{valueName} invalide : {detail}.");
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
                detail = "valeur absente";
                return false;

            case byte[] bytes when bytes.Length == 16:
                id = new Guid(bytes);
                detail = "REG_BINARY 16 bytes";
                return true;

            case byte[] bytes:
                detail = $"REG_BINARY longueur {bytes.Length}, attendue 16";
                return false;

            case string text when TryParseGuidText(text, out id):
                detail = "REG_SZ guid/hex";
                return true;

            case string text:
                detail = $"REG_SZ illisible '{text}'";
                return false;

            default:
                detail = $"type {raw.GetType().FullName} non supporté";
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
