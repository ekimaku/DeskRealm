using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;

namespace DeskRealm.App.Services;

internal sealed class DesktopIconShellService
{
    private const int RestoreBatchSize = 16;
    private const int RestoreVerificationTolerancePx = 4;

    private readonly FileLogger _logger;

    public DesktopIconShellService(FileLogger logger) => _logger = logger;

    public IReadOnlyList<DesktopIconPosition> CapturePositions() => CapturePositions(transitionAware: false);

    private IReadOnlyList<DesktopIconPosition> CapturePositions(bool transitionAware)
    {
        _logger.Info("Icon layout capture phase: acquire desktop IFolderView.");
        var view = GetDesktopFolderView(transitionAware);
        IShellFolder? folder = null;
        var pidlsToFree = new List<IntPtr>();

        try
        {
            folder = GetShellFolder(view, transitionAware);
            pidlsToFree.AddRange(EnumerateViewPidls(view, transitionAware));
            _logger.Info($"Icon layout capture phase: visible item count = {pidlsToFree.Count}.");

            var positions = new List<DesktopIconPosition>(pidlsToFree.Count);
            for (var index = 0; index < pidlsToFree.Count; index++)
            {
                var pidl = pidlsToFree[index];
                var itemKey = BuildPidlItemKey(pidl);
                var shellDisplayName = TryGetShellDisplayName(folder, pidl, ShellConstants.SHGDN_INFOLDER);
                var shellParsingName = TryGetShellDisplayName(folder, pidl, ShellConstants.SHGDN_FORPARSING);
                var displayName = !string.IsNullOrWhiteSpace(shellDisplayName)
                    ? shellDisplayName
                    : BuildTechnicalDisplayName(index, itemKey);
                var identityKeys = BuildIdentityKeys(itemKey, shellDisplayName, shellParsingName);

                var hr = view.GetItemPosition(pidl, out var point);
                ThrowIfFailed(hr, $"IFolderView.GetItemPosition('{displayName}')", transitionAware);

                positions.Add(new DesktopIconPosition
                {
                    ItemKey = itemKey,
                    DisplayName = displayName,
                    ShellDisplayName = shellDisplayName,
                    ShellParsingName = shellParsingName,
                    IdentityKeys = identityKeys,
                    X = point.X,
                    Y = point.Y
                });
            }

            ValidateNoDuplicateItemKeys(positions, "save");
            _logger.Info($"Icon layout capture phase: captured {positions.Count} positions.");
            return positions;
        }
        finally
        {
            foreach (var pidl in pidlsToFree)
            {
                Marshal.FreeCoTaskMem(pidl);
            }

            ReleaseComObject(folder);
            ReleaseComObject(view);
        }
    }

    public DesktopViewProbe ProbeCurrentView(string expectedRealmPath)
    {
        if (string.IsNullOrWhiteSpace(expectedRealmPath))
        {
            throw new ArgumentException("Expected realm path is empty.", nameof(expectedRealmPath));
        }

        var normalizedRealm = NormalizePath(expectedRealmPath);
        if (!Directory.Exists(normalizedRealm))
        {
            throw new DirectoryNotFoundException($"Expected realm path not found: {normalizedRealm}");
        }

        var expectedEntryNames = Directory.EnumerateFileSystemEntries(normalizedRealm)
            .Select(path => new
            {
                Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Attributes = File.GetAttributes(path)
            })
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Name) &&
                (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            .Select(entry => entry.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var positions = CapturePositions(transitionAware: true);
        var pathBackedItems = positions
            .Where(icon => !string.IsNullOrWhiteSpace(icon.ShellParsingName) && Path.IsPathFullyQualified(icon.ShellParsingName))
            .ToList();
        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        var normalizedCommonDesktop = string.IsNullOrWhiteSpace(commonDesktop) || !Directory.Exists(commonDesktop)
            ? null
            : NormalizePath(commonDesktop);
        var pathMatches = pathBackedItems.Count(icon => IsPathInsideRealm(icon.ShellParsingName, normalizedRealm));
        var commonDesktopMatches = normalizedCommonDesktop is null
            ? 0
            : pathBackedItems.Count(icon => IsPathInsideRealm(icon.ShellParsingName, normalizedCommonDesktop));
        var outsideRealmPaths = pathBackedItems.Count - pathMatches - commonDesktopMatches;

        var matchedExpectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var icon in positions)
        {
            foreach (var candidate in EnumerateVisibleNames(icon))
            {
                if (expectedEntryNames.Contains(candidate))
                {
                    matchedExpectedNames.Add(candidate);
                }
            }
        }

        var foreignPathBackedItems = pathBackedItems.Count(icon =>
            !IsPathInsideRealm(icon.ShellParsingName, normalizedRealm) &&
            (normalizedCommonDesktop is null || !IsPathInsideRealm(icon.ShellParsingName, normalizedCommonDesktop)));

        var fingerprintSource = string.Join(
            "\n",
            positions
                .Select(position => $"{position.ItemKey}|{position.ShellDisplayName}|{position.ShellParsingName}")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource)));

        var exactRealmMembership = matchedExpectedNames.Count == expectedEntryNames.Count &&
            pathMatches == expectedEntryNames.Count &&
            outsideRealmPaths == 0 &&
            foreignPathBackedItems == 0;

        return new DesktopViewProbe(
            fingerprint,
            positions.Count,
            expectedEntryNames.Count,
            matchedExpectedNames.Count,
            pathBackedItems.Count,
            pathMatches,
            commonDesktopMatches,
            outsideRealmPaths,
            foreignPathBackedItems,
            exactRealmMembership);
    }

    private static IEnumerable<string> EnumerateVisibleNames(DesktopIconPosition icon)
    {
        if (!string.IsNullOrWhiteSpace(icon.ShellDisplayName))
        {
            yield return icon.ShellDisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(icon.DisplayName))
        {
            yield return icon.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(icon.ShellParsingName) && Path.IsPathFullyQualified(icon.ShellParsingName))
        {
            var fileName = Path.GetFileName(icon.ShellParsingName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                yield return fileName;
            }
        }
    }

    private static bool IsPathInsideRealm(string? candidate, string realmPath)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate))
        {
            return false;
        }

        var normalizedCandidate = NormalizePath(candidate);
        return normalizedCandidate.StartsWith(
            realmPath + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public int RestorePositions(
        IReadOnlyList<DesktopIconPosition> savedPositions,
        int verificationTimeoutMs,
        bool transitionAware = false)
    {
        if (savedPositions.Count == 0)
        {
            _logger.Info("Icon layout restore skipped: saved layout is empty.");
            return 0;
        }

        ValidateLayoutHasStableKeys(savedPositions);

        _logger.Info("Icon layout restore phase: acquire desktop IFolderView.");
        var view = GetDesktopFolderView(transitionAware);
        IShellFolder? folder = null;
        var pidlsToFree = new List<IntPtr>();

        try
        {
            folder = GetShellFolder(view, transitionAware);
            pidlsToFree.AddRange(EnumerateViewPidls(view, transitionAware));
            _logger.Info($"Icon layout restore phase: visible item count = {pidlsToFree.Count}.");

            var currentItems = new List<CurrentDesktopItem>(pidlsToFree.Count);
            var currentItemsByExactKey = new Dictionary<string, CurrentDesktopItem>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < pidlsToFree.Count; index++)
            {
                var pidl = pidlsToFree[index];
                var itemKey = BuildPidlItemKey(pidl);
                if (currentItemsByExactKey.ContainsKey(itemKey))
                {
                    throw new InvalidOperationException(
                        $"Ambiguous icon layout: two current Desktop items share the same PIDL key '{itemKey}'. " +
                        "DeskRealm refuses to apply an ambiguous layout.");
                }

                var shellDisplayName = TryGetShellDisplayName(folder, pidl, ShellConstants.SHGDN_INFOLDER);
                var shellParsingName = TryGetShellDisplayName(folder, pidl, ShellConstants.SHGDN_FORPARSING);
                var displayName = !string.IsNullOrWhiteSpace(shellDisplayName)
                    ? shellDisplayName
                    : BuildTechnicalDisplayName(index, itemKey);
                var identityKeys = BuildIdentityKeys(itemKey, shellDisplayName, shellParsingName);

                var hr = view.GetItemPosition(pidl, out var currentPosition);
                ThrowIfFailed(hr, $"IFolderView.GetItemPosition(current '{displayName}')", transitionAware);

                var currentItem = new CurrentDesktopItem(
                    itemKey,
                    displayName,
                    shellDisplayName,
                    shellParsingName,
                    identityKeys,
                    pidl,
                    currentPosition);
                currentItems.Add(currentItem);
                currentItemsByExactKey.Add(itemKey, currentItem);
            }

            var missing = new List<string>();
            var targets = new List<RestoreTarget>();
            var usedCurrentItemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fallbackMatches = 0;

            foreach (var saved in savedPositions)
            {
                var savedDisplayName = BestDisplayName(saved);
                CurrentDesktopItem? matched = null;
                var exactMatch = currentItemsByExactKey.TryGetValue(saved.ItemKey, out var exactItem) &&
                    !usedCurrentItemKeys.Contains(exactItem.ItemKey);

                if (exactMatch)
                {
                    matched = exactItem;
                }
                else
                {
                    matched = FindBestFallbackMatch(saved, currentItems, usedCurrentItemKeys);
                    if (matched is not null)
                    {
                        fallbackMatches++;
                        _logger.Info(
                            $"Icon layout restore identity fallback: saved '{savedDisplayName}' [{ShortKey(saved.ItemKey)}] " +
                            $"matched current '{matched.DisplayName}' [{ShortKey(matched.ItemKey)}].");
                    }
                }

                if (matched is null)
                {
                    missing.Add(savedDisplayName);
                    continue;
                }

                usedCurrentItemKeys.Add(matched.ItemKey);
                targets.Add(new RestoreTarget(
                    saved.ItemKey,
                    savedDisplayName,
                    matched.Pidl,
                    new NativePoint(saved.X, saved.Y)));
            }

            var attempted = targets.Count;
            var unresolved = new List<RestoreTarget>();
            if (attempted > 0)
            {
                ApplyPositionsInChunks(view, targets, "initial", transitionAware);
                unresolved = VerifyAndRetryPositions(view, targets, verificationTimeoutMs, transitionAware);
            }

            if (missing.Count > 0)
            {
                _logger.Warn($"Icon layout restore partial: {missing.Count} saved icons not found in current desktop view: {string.Join(", ", missing.Take(12))}{(missing.Count > 12 ? ", ..." : string.Empty)}");
            }

            if (fallbackMatches > 0)
            {
                _logger.Info($"Icon layout restore phase: recovered {fallbackMatches} icon(s) through secondary Shell identity matching.");
            }

            if (attempted == 0)
            {
                _logger.Warn("Icon layout restore applied to 0 icon. Layout file kept, no fallback layout used.");
            }

            if (unresolved.Count > 0)
            {
                var unresolvedNames =
                    $"{string.Join(", ", unresolved.Select(t => t.DisplayName).Take(12))}{(unresolved.Count > 12 ? ", ..." : string.Empty)}";
                _logger.Warn(
                    $"Icon layout restore verification still has {unresolved.Count} icon(s) not at target position: {unresolvedNames}");
                throw new TimeoutException(
                    $"Icon layout verification failed for {unresolved.Count}/{attempted} visible target(s) within {verificationTimeoutMs} ms: {unresolvedNames}");
            }

            var verifiedRestored = attempted;
            _logger.Info(
                $"Icon layout restore phase: verified {verifiedRestored}/{savedPositions.Count} positions " +
                $"(attempted={attempted}, missing={missing.Count}, fallbackMatches={fallbackMatches}, unresolved={unresolved.Count}).");
            return verifiedRestored;
        }
        finally
        {
            foreach (var pidl in pidlsToFree)
            {
                Marshal.FreeCoTaskMem(pidl);
            }

            ReleaseComObject(folder);
            ReleaseComObject(view);
        }
    }

    private static CurrentDesktopItem? FindBestFallbackMatch(
        DesktopIconPosition saved,
        IReadOnlyList<CurrentDesktopItem> currentItems,
        HashSet<string> usedCurrentItemKeys)
    {
        var savedKeys = EffectiveIdentityKeys(saved).Where(k => !string.Equals(k, saved.ItemKey, StringComparison.OrdinalIgnoreCase));
        foreach (var savedKey in savedKeys)
        {
            var candidates = currentItems
                .Where(current =>
                    !usedCurrentItemKeys.Contains(current.ItemKey) &&
                    current.IdentityKeys.Any(k => string.Equals(k, savedKey, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(current => DistanceSquared(current.CurrentPosition.X, current.CurrentPosition.Y, saved.X, saved.Y))
                .ToList();

            if (candidates.Count > 0)
            {
                return candidates[0];
            }
        }

        return null;
    }

    private static IReadOnlyList<string> EffectiveIdentityKeys(DesktopIconPosition icon)
    {
        var keys = new List<string>();
        AddIdentityKey(keys, icon.ItemKey);
        foreach (var key in icon.IdentityKeys)
        {
            AddIdentityKey(keys, key);
        }

        foreach (var key in BuildIdentityKeys(icon.ItemKey, icon.ShellDisplayName, icon.ShellParsingName))
        {
            AddIdentityKey(keys, key);
        }

        // v0.5.5 and older layouts only stored DisplayName. Use it as a last-resort human-name fallback,
        // but only when it does not look like the old technical placeholder.
        if (!string.IsNullOrWhiteSpace(icon.DisplayName) &&
            !icon.DisplayName.StartsWith("Desktop item #", StringComparison.OrdinalIgnoreCase))
        {
            AddIdentityKey(keys, "name:" + NormalizeIdentityValue(icon.DisplayName));
        }

        return keys;
    }

    private static List<string> BuildIdentityKeys(string itemKey, string shellDisplayName, string shellParsingName)
    {
        var keys = new List<string>();
        AddIdentityKey(keys, itemKey);

        var normalizedDisplayName = NormalizeIdentityValue(shellDisplayName);
        if (!string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            AddIdentityKey(keys, "name:" + normalizedDisplayName);
            AddIdentityKey(keys, "stem:" + NormalizeIdentityValue(Path.GetFileNameWithoutExtension(normalizedDisplayName)));
        }

        var normalizedParsingName = NormalizeIdentityValue(shellParsingName);
        if (!string.IsNullOrWhiteSpace(normalizedParsingName))
        {
            AddIdentityKey(keys, "parse:" + normalizedParsingName);

            var fileName = Path.GetFileName(normalizedParsingName);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                AddIdentityKey(keys, "file:" + NormalizeIdentityValue(fileName));
                AddIdentityKey(keys, "stem:" + NormalizeIdentityValue(Path.GetFileNameWithoutExtension(fileName)));
            }
        }

        return keys;
    }

    private static void AddIdentityKey(List<string> keys, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!keys.Any(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)))
        {
            keys.Add(key);
        }
    }

    private static string NormalizeIdentityValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Trim()
            .Replace('\\', '/')
            .Replace('\u00a0', ' ')
            .ToLowerInvariant();
    }

    private static long DistanceSquared(int x1, int y1, int x2, int y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return (long)dx * dx + (long)dy * dy;
    }

    private static string BestDisplayName(DesktopIconPosition icon)
    {
        if (!string.IsNullOrWhiteSpace(icon.ShellDisplayName))
        {
            return icon.ShellDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(icon.DisplayName))
        {
            return icon.DisplayName;
        }

        return icon.ItemKey;
    }

    private static string ShortKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "no-key";
        }

        return key.Length <= 28 ? key : key[..28] + "…";
    }

    private static IShellFolder GetShellFolder(IFolderView view, bool transitionAware)
    {
        var shellFolderId = ShellGuids.IID_IShellFolder;
        var hr = view.GetFolder(ref shellFolderId, out var folderObject);
        ThrowIfFailed(hr, "IFolderView.GetFolder(IShellFolder)", transitionAware);
        return (IShellFolder)folderObject;
    }

    private static string TryGetShellDisplayName(IShellFolder folder, IntPtr pidl, uint flags)
    {
        try
        {
            var hr = folder.GetDisplayNameOf(pidl, flags, out var strRet);
            if (hr < 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(1024);
            hr = StrRetToBuf(ref strRet, pidl, builder, (uint)builder.Capacity);
            if (hr < 0)
            {
                return string.Empty;
            }

            return builder.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ApplyPositionsInChunks(
        IFolderView view,
        IReadOnlyList<RestoreTarget> targets,
        string phase,
        bool transitionAware)
    {
        foreach (var chunk in targets.Chunk(RestoreBatchSize))
        {
            var chunkTargets = chunk.ToArray();
            var hr = view.SelectAndPositionItems(
                (uint)chunkTargets.Length,
                chunkTargets.Select(t => t.Pidl).ToArray(),
                chunkTargets.Select(t => t.Target).ToArray(),
                ShellConstants.SVSI_POSITIONITEM | ShellConstants.SVSI_NOTAKEFOCUS);
            ThrowIfFailed(
                hr,
                $"IFolderView.SelectAndPositionItems({phase}, {chunkTargets.Length} icon(s))",
                transitionAware);

        }
    }

    private List<RestoreTarget> VerifyAndRetryPositions(
        IFolderView view,
        IReadOnlyList<RestoreTarget> targets,
        int verificationTimeoutMs,
        bool transitionAware)
    {
        if (verificationTimeoutMs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(verificationTimeoutMs));
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var pending = FindTargetsNotAtPosition(view, targets, transitionAware);
        var previousCount = int.MaxValue;
        var probe = 0;
        var reapplyCount = 0;

        while (pending.Count > 0 && stopwatch.ElapsedMilliseconds < verificationTimeoutMs)
        {
            if (pending.Count >= previousCount)
            {
                ApplyPositionsInChunks(
                    view,
                    pending,
                    $"adaptive-retry-{reapplyCount + 1}",
                    transitionAware);
                reapplyCount++;
            }

            previousCount = pending.Count;
            AdaptiveWait(probe++);
            pending = FindTargetsNotAtPosition(view, pending, transitionAware);
        }

        stopwatch.Stop();
        if (pending.Count == 0)
        {
            _logger.Info(
                $"Icon layout restore verification passed: elapsed={stopwatch.Elapsed.TotalMilliseconds:0.0} ms, " +
                $"reapplyCount={reapplyCount}.");
            return pending;
        }

        _logger.Warn(
            $"Icon layout restore verification timeout: unresolved={pending.Count}/{targets.Count}, " +
            $"elapsed={stopwatch.Elapsed.TotalMilliseconds:0.0} ms, reapplyCount={reapplyCount}.");
        return FindTargetsNotAtPosition(view, targets, transitionAware);
    }

    private static void AdaptiveWait(int probe)
    {
        if (probe < 3)
        {
            Thread.Yield();
            return;
        }

        Thread.Sleep(Math.Min(64, 2 << Math.Min(5, probe - 3)));
    }

    private static List<RestoreTarget> FindTargetsNotAtPosition(
        IFolderView view,
        IReadOnlyList<RestoreTarget> targets,
        bool transitionAware)
    {
        var unresolved = new List<RestoreTarget>();
        foreach (var target in targets)
        {
            var hr = view.GetItemPosition(target.Pidl, out var current);
            ThrowIfFailed(
                hr,
                $"IFolderView.GetItemPosition(verify '{target.DisplayName}')",
                transitionAware);

            if (Math.Abs(current.X - target.Target.X) > RestoreVerificationTolerancePx ||
                Math.Abs(current.Y - target.Target.Y) > RestoreVerificationTolerancePx)
            {
                unresolved.Add(target);
            }
        }

        return unresolved;
    }

    private static IReadOnlyList<IntPtr> EnumerateViewPidls(IFolderView view, bool transitionAware)
    {
        object? enumeratorObject = null;
        var pidls = new List<IntPtr>();

        try
        {
            var enumIdListId = ShellGuids.IID_IEnumIDList;
            var hr = view.Items(
                ShellConstants.SVGIO_ALLVIEW,
                ref enumIdListId,
                out var rawEnumerator);
            enumeratorObject = rawEnumerator;
            ThrowIfFailed(hr, "IFolderView.Items(SVGIO_ALLVIEW, IEnumIDList)", transitionAware);

            if (enumeratorObject is not IEnumIDList enumerator)
            {
                throw new InvalidOperationException(
                    "IFolderView.Items did not return the requested IEnumIDList interface.");
            }

            while (true)
            {
                hr = enumerator.Next(1, out var pidl, out var fetched);
                if (hr == ShellConstants.S_FALSE || fetched == 0)
                {
                    return pidls;
                }

                ThrowIfFailed(hr, "IEnumIDList.Next", transitionAware);
                if (fetched != 1 || pidl == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        $"IEnumIDList.Next returned an invalid item (fetched={fetched}, pidl=0x{pidl.ToInt64():X}).");
                }

                pidls.Add(pidl);
            }
        }
        catch
        {
            foreach (var pidl in pidls)
            {
                Marshal.FreeCoTaskMem(pidl);
            }

            throw;
        }
        finally
        {
            ReleaseComObject(enumeratorObject);
        }
    }

    private static string BuildPidlItemKey(IntPtr pidl)
    {
        var bytes = ReadPidlBytes(pidl);
        var hash = SHA256.HashData(bytes);
        return "pidl-sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] ReadPidlBytes(IntPtr pidl)
    {
        const int maxPidlBytes = 65535;
        const int maxSegmentBytes = 4096;

        if (pidl == IntPtr.Zero)
        {
            throw new InvalidOperationException("Empty PIDL: item key unavailable.");
        }

        var bytes = new List<byte>();
        var offset = 0;
        while (offset < maxPidlBytes)
        {
            var cb = (ushort)Marshal.ReadInt16(pidl, offset);
            if (cb == 0)
            {
                bytes.Add(0);
                bytes.Add(0);
                return bytes.ToArray();
            }

            if (cb < 2 || cb > maxSegmentBytes)
            {
                throw new InvalidOperationException($"Invalid PIDL: segment cb={cb}, offset={offset}.");
            }

            var segment = new byte[cb];
            Marshal.Copy(IntPtr.Add(pidl, offset), segment, 0, cb);
            bytes.AddRange(segment);
            offset += cb;
        }

        throw new InvalidOperationException($"Invalid PIDL: terminator not found before {maxPidlBytes} bytes.");
    }

    private static string BuildTechnicalDisplayName(int index, string itemKey)
    {
        var suffix = itemKey.Length > 24 ? itemKey[..24] : itemKey;
        return $"Desktop item #{index} [{suffix}…]";
    }

    private static void ValidateNoDuplicateItemKeys(IEnumerable<DesktopIconPosition> positions, string action)
    {
        var duplicate = positions
            .Where(p => !string.IsNullOrWhiteSpace(p.ItemKey))
            .GroupBy(p => p.ItemKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Ambiguous icon layout: multiple icons share the same item key '{duplicate.Key}'. " +
                $"DeskRealm refuses to {action} an ambiguous layout.");
        }
    }

    private static void ValidateLayoutHasStableKeys(IReadOnlyList<DesktopIconPosition> savedPositions)
    {
        var legacy = savedPositions.Where(p => string.IsNullOrWhiteSpace(p.ItemKey)).ToList();
        if (legacy.Count > 0)
        {
            throw new InvalidOperationException(
                "Legacy icon layout format detected: it does not contain the stable item keys added in v0.3.3. " +
                "Run 'Save icon layout now' once on this realm to regenerate the layout.");
        }

        ValidateNoDuplicateItemKeys(savedPositions, "restore");
    }

    private static IFolderView GetDesktopFolderView(bool transitionAware)
    {
        var shellWindowsType = Type.GetTypeFromCLSID(ShellGuids.CLSID_ShellWindows)
            ?? throw new InvalidOperationException("CLSID_ShellWindows not found.");

        object? shellWindowsObject = null;
        object? dispatch = null;
        IntPtr browserPtr = IntPtr.Zero;
        object? browserObject = null;

        try
        {
            shellWindowsObject = Activator.CreateInstance(shellWindowsType)
                ?? throw new InvalidOperationException("Cannot create ShellWindows.");

            dynamic shellWindows = shellWindowsObject;
            object loc = ShellConstants.CSIDL_DESKTOP;
            object locRoot = null!;
            int hwnd = 0;

            // IShellWindows::FindWindowSW exposes ppdispOut as retval in Automation.
            // Using dynamic here avoids fragile partial vtable declarations for this dual IDispatch interface.
            dispatch = shellWindows.FindWindowSW(
                ref loc,
                ref locRoot,
                ShellConstants.SWC_DESKTOP,
                ref hwnd,
                ShellConstants.SWFO_NEEDDISPATCH);

            if (dispatch is null)
            {
                throw new InvalidOperationException("FindWindowSW did not return any Desktop dispatch.");
            }

            var serviceProvider = (IServiceProvider)dispatch;
            var topLevelBrowser = ShellGuids.SID_STopLevelBrowser;
            var shellBrowserId = ShellGuids.IID_IShellBrowser;
            var hr = serviceProvider.QueryService(ref topLevelBrowser, ref shellBrowserId, out browserPtr);
            ThrowIfFailed(
                hr,
                "IServiceProvider.QueryService(SID_STopLevelBrowser, IShellBrowser)",
                transitionAware);

            browserObject = Marshal.GetObjectForIUnknown(browserPtr);
            var browser = (IShellBrowser)browserObject;
            hr = browser.QueryActiveShellView(out var shellView);
            ThrowIfFailed(hr, "IShellBrowser.QueryActiveShellView", transitionAware);

            return (IFolderView)shellView;
        }
        finally
        {
            if (browserPtr != IntPtr.Zero)
            {
                Marshal.Release(browserPtr);
            }

            ReleaseComObject(browserObject);
            ReleaseComObject(dispatch);
            ReleaseComObject(shellWindowsObject);
        }
    }

    private static void ThrowIfFailed(int hr, string operation, bool transitionAware = false)
    {
        if (hr == ShellConstants.E_BOUNDS ||
            hr == ShellConstants.E_CHANGED_STATE ||
            (transitionAware && hr == ShellConstants.E_FAIL))
        {
            throw new ShellViewTransitionException(
                $"{operation} observed a changing Shell view (HRESULT 0x{hr:X8}).",
                Marshal.GetExceptionForHR(hr));
        }

        if (hr < 0)
        {
            throw new InvalidOperationException($"{operation} failed with HRESULT 0x{hr:X8}.", Marshal.GetExceptionForHR(hr));
        }
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    private sealed record RestoreTarget(string ItemKey, string DisplayName, IntPtr Pidl, NativePoint Target);

    private sealed record CurrentDesktopItem(
        string ItemKey,
        string DisplayName,
        string ShellDisplayName,
        string ShellParsingName,
        IReadOnlyList<string> IdentityKeys,
        IntPtr Pidl,
        NativePoint CurrentPosition);

    [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrRetToBuf(ref ShellStrRet pstr, IntPtr pidl, StringBuilder pszBuf, uint cchBuf);

    private static class ShellConstants
    {
        public const int CSIDL_DESKTOP = 0;
        public const int SWC_DESKTOP = 8;
        public const int SWFO_NEEDDISPATCH = 1;
        public const uint SVGIO_ALLVIEW = 0x00000002;
        public const uint SVSI_POSITIONITEM = 0x00000080;
        public const uint SVSI_NOTAKEFOCUS = 0x40000000;
        public const uint SHGDN_INFOLDER = 0x00000001;
        public const uint SHGDN_FORPARSING = 0x00008000;
        public const int S_FALSE = 1;
        public const int E_FAIL = unchecked((int)0x80004005);
        public const int E_BOUNDS = unchecked((int)0x8000000B);
        public const int E_CHANGED_STATE = unchecked((int)0x8000000C);
    }

    private static class ShellGuids
    {
        public static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        public static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
        public static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
        public static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
        public static readonly Guid IID_IEnumIDList = new("000214F2-0000-0000-C000-000000000046");
    }

    [StructLayout(LayoutKind.Explicit, Size = 520)]
    private struct ShellStrRet
    {
        [FieldOffset(0)] public uint UType;
        [FieldOffset(4)] public IntPtr POleStr;
        [FieldOffset(4)] public IntPtr PStr;
        [FieldOffset(4)] public uint UOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public readonly int X;
        public readonly int Y;
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig]
        int GetWindow(out IntPtr phwnd);

        [PreserveSig]
        int ContextSensitiveHelp(int fEnterMode);

        [PreserveSig]
        int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);

        [PreserveSig]
        int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuReserved, IntPtr hwndActiveObject);

        [PreserveSig]
        int RemoveMenusSB(IntPtr hmenuShared);

        [PreserveSig]
        int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string pszStatusText);

        [PreserveSig]
        int EnableModelessSB(int fEnable);

        [PreserveSig]
        int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);

        [PreserveSig]
        int BrowseObject(IntPtr pidl, uint wFlags);

        [PreserveSig]
        int GetViewStateStream(uint grfMode, out IStream ppStrm);

        [PreserveSig]
        int GetControlWindow(uint id, out IntPtr lphwnd);

        [PreserveSig]
        int SendControlMsg(uint id, uint uMsg, UIntPtr wParam, IntPtr lParam, out IntPtr pret);

        [PreserveSig]
        int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellView ppshv);

        [PreserveSig]
        int OnViewWindowActive(IShellView pshv);

        [PreserveSig]
        int SetToolbarItems(IntPtr lpButtons, uint nButtons, uint uFlags);
    }

    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(
            IntPtr hwnd,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            out uint pchEaten,
            out IntPtr ppidl,
            ref uint pdwAttributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);

        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetAttributesOf(uint cidl, IntPtr[] apidl, ref uint rgfInOut);

        [PreserveSig]
        int GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, out ShellStrRet pName);

        [PreserveSig]
        int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport]
    [Guid("000214F2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumIDList
    {
        [PreserveSig]
        int Next(uint celt, out IntPtr rgelt, out uint pceltFetched);

        [PreserveSig]
        int Skip(uint celt);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone(out IEnumIDList ppenum);
    }

    [ComImport]
    [Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        [PreserveSig]
        int GetCurrentViewMode(out uint pViewMode);

        [PreserveSig]
        int SetCurrentViewMode(uint ViewMode);

        [PreserveSig]
        int GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int Item(int iItemIndex, out IntPtr ppidl);

        [PreserveSig]
        int ItemCount(uint uFlags, out int pcItems);

        [PreserveSig]
        int Items(uint uFlags, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [PreserveSig]
        int GetSelectionMarkedItem(out int piItem);

        [PreserveSig]
        int GetFocusedItem(out int piItem);

        [PreserveSig]
        int GetItemPosition(IntPtr pidl, out NativePoint ppt);

        [PreserveSig]
        int GetSpacing(out NativePoint ppt);

        [PreserveSig]
        int GetDefaultSpacing(out NativePoint ppt);

        [PreserveSig]
        int GetAutoArrange();

        [PreserveSig]
        int SelectItem(int iItem, uint dwFlags);

        [PreserveSig]
        int SelectAndPositionItems(
            uint cidl,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] NativePoint[] apt,
            uint dwFlags);
    }
}

internal sealed class ShellViewTransitionException : Exception
{
    public ShellViewTransitionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed record DesktopViewProbe(
    string Fingerprint,
    int VisibleItemCount,
    int ExpectedEntryCount,
    int ExpectedNameMatchCount,
    int PathBackedItemCount,
    int PathMatchCount,
    int CommonDesktopPathCount,
    int OutsideRealmPathCount,
    int ForeignPathBackedItemCount,
    bool HasExactRealmMembership);
