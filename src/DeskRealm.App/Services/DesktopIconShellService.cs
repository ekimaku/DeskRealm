using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;

namespace DeskRealm.App.Services;

internal sealed class DesktopIconShellService
{
    private const int RestoreBatchSize = 6;
    private const int RestoreChunkDelayMs = 45;
    private const int RestoreVerificationDelayMs = 180;
    private const int RestoreVerificationRetryCount = 3;
    private const int RestoreVerificationTolerancePx = 4;

    private readonly FileLogger _logger;

    public DesktopIconShellService(FileLogger logger) => _logger = logger;

    public IReadOnlyList<DesktopIconPosition> CapturePositions()
    {
        _logger.Info("Icon layout capture phase: acquire desktop IFolderView.");
        var view = GetDesktopFolderView();
        IShellFolder? folder = null;

        try
        {
            folder = GetShellFolder(view);
            var count = GetFolderViewItemCount(view);
            _logger.Info($"Icon layout capture phase: visible item count = {count}.");

            var positions = new List<DesktopIconPosition>();
            for (var index = 0; index < count; index++)
            {
                IntPtr pidl = IntPtr.Zero;

                try
                {
                    var hr = view.Item(index, out pidl);
                    ThrowIfFailed(hr, $"IFolderView.Item({index})");
                    if (pidl == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"IFolderView.Item({index}) returned an empty PIDL.");
                    }

                    var itemKey = BuildPidlItemKey(pidl);
                    var shellDisplayName = TryGetShellDisplayName(folder, pidl, ShellConstants.SHGDN_INFOLDER);
                    var shellParsingName = TryGetShellDisplayName(folder, pidl, ShellConstants.SHGDN_FORPARSING);
                    var displayName = !string.IsNullOrWhiteSpace(shellDisplayName)
                        ? shellDisplayName
                        : BuildTechnicalDisplayName(index, itemKey);
                    var identityKeys = BuildIdentityKeys(itemKey, shellDisplayName, shellParsingName);

                    hr = view.GetItemPosition(pidl, out var point);
                    ThrowIfFailed(hr, $"IFolderView.GetItemPosition('{displayName}')");

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
                finally
                {
                    if (pidl != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(pidl);
                    }
                }
            }

            ValidateNoDuplicateItemKeys(positions, "save");
            _logger.Info($"Icon layout capture phase: captured {positions.Count} positions.");
            return positions;
        }
        finally
        {
            ReleaseComObject(folder);
            ReleaseComObject(view);
        }
    }

    public int RestorePositions(IReadOnlyList<DesktopIconPosition> savedPositions)
    {
        if (savedPositions.Count == 0)
        {
            _logger.Info("Icon layout restore skipped: saved layout is empty.");
            return 0;
        }

        ValidateLayoutHasStableKeys(savedPositions);

        _logger.Info("Icon layout restore phase: acquire desktop IFolderView.");
        var view = GetDesktopFolderView();
        IShellFolder? folder = null;
        var pidlsToFree = new List<IntPtr>();

        try
        {
            folder = GetShellFolder(view);
            var count = GetFolderViewItemCount(view);
            _logger.Info($"Icon layout restore phase: visible item count = {count}.");

            var currentItems = new List<CurrentDesktopItem>();
            var currentItemsByExactKey = new Dictionary<string, CurrentDesktopItem>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < count; index++)
            {
                var hr = view.Item(index, out var pidl);
                ThrowIfFailed(hr, $"IFolderView.Item({index})");
                if (pidl == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"IFolderView.Item({index}) returned an empty PIDL.");
                }

                var itemKey = BuildPidlItemKey(pidl);
                if (currentItemsByExactKey.ContainsKey(itemKey))
                {
                    Marshal.FreeCoTaskMem(pidl);
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

                hr = view.GetItemPosition(pidl, out var currentPosition);
                ThrowIfFailed(hr, $"IFolderView.GetItemPosition(current '{displayName}')");

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
                pidlsToFree.Add(pidl);
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
                ApplyPositionsInChunks(view, targets, "initial");
                unresolved = VerifyAndRetryPositions(view, targets);
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
                _logger.Warn(
                    $"Icon layout restore verification still has {unresolved.Count} icon(s) not at target position after retries: " +
                    $"{string.Join(", ", unresolved.Select(t => t.DisplayName).Take(12))}{(unresolved.Count > 12 ? ", ..." : string.Empty)}");
            }

            var verifiedRestored = Math.Max(0, attempted - unresolved.Count);
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

    private static IShellFolder GetShellFolder(IFolderView view)
    {
        var shellFolderId = ShellGuids.IID_IShellFolder;
        var hr = view.GetFolder(ref shellFolderId, out var folderObject);
        ThrowIfFailed(hr, "IFolderView.GetFolder(IShellFolder)");
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

    private void ApplyPositionsInChunks(IFolderView view, IReadOnlyList<RestoreTarget> targets, string phase)
    {
        foreach (var chunk in targets.Chunk(RestoreBatchSize))
        {
            var chunkTargets = chunk.ToArray();
            var hr = view.SelectAndPositionItems(
                (uint)chunkTargets.Length,
                chunkTargets.Select(t => t.Pidl).ToArray(),
                chunkTargets.Select(t => t.Target).ToArray(),
                ShellConstants.SVSI_POSITIONITEM | ShellConstants.SVSI_NOTAKEFOCUS);
            ThrowIfFailed(hr, $"IFolderView.SelectAndPositionItems({phase}, {chunkTargets.Length} icon(s))");

            if (RestoreChunkDelayMs > 0 && chunkTargets.Length == RestoreBatchSize)
            {
                Thread.Sleep(RestoreChunkDelayMs);
            }
        }
    }

    private List<RestoreTarget> VerifyAndRetryPositions(IFolderView view, IReadOnlyList<RestoreTarget> targets)
    {
        var unresolved = new List<RestoreTarget>();
        IReadOnlyList<RestoreTarget> pending = targets;

        for (var attempt = 1; attempt <= RestoreVerificationRetryCount; attempt++)
        {
            if (RestoreVerificationDelayMs > 0)
            {
                Thread.Sleep(RestoreVerificationDelayMs);
            }

            unresolved = FindTargetsNotAtPosition(view, pending);
            if (unresolved.Count == 0)
            {
                _logger.Info($"Icon layout restore verification passed on attempt {attempt}/{RestoreVerificationRetryCount}.");
                return unresolved;
            }

            _logger.Warn(
                $"Icon layout restore verification retry {attempt}/{RestoreVerificationRetryCount}: " +
                $"{unresolved.Count} icon(s) not yet at target position. Re-applying only unresolved icons.");
            ApplyPositionsInChunks(view, unresolved, $"verification-retry-{attempt}");
            pending = unresolved;
        }

        if (RestoreVerificationDelayMs > 0)
        {
            Thread.Sleep(RestoreVerificationDelayMs);
        }

        return FindTargetsNotAtPosition(view, targets);
    }

    private static List<RestoreTarget> FindTargetsNotAtPosition(IFolderView view, IReadOnlyList<RestoreTarget> targets)
    {
        var unresolved = new List<RestoreTarget>();
        foreach (var target in targets)
        {
            var hr = view.GetItemPosition(target.Pidl, out var current);
            ThrowIfFailed(hr, $"IFolderView.GetItemPosition(verify '{target.DisplayName}')");

            if (Math.Abs(current.X - target.Target.X) > RestoreVerificationTolerancePx ||
                Math.Abs(current.Y - target.Target.Y) > RestoreVerificationTolerancePx)
            {
                unresolved.Add(target);
            }
        }

        return unresolved;
    }

    private static int GetFolderViewItemCount(IFolderView view)
    {
        var hr = view.ItemCount(ShellConstants.SVGIO_ALLVIEW, out var count);
        ThrowIfFailed(hr, "IFolderView.ItemCount(SVGIO_ALLVIEW)");
        if (count < 0)
        {
            throw new InvalidOperationException($"Invalid Desktop icon count: {count}.");
        }

        return count;
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

    private static IFolderView GetDesktopFolderView()
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
            ThrowIfFailed(hr, "IServiceProvider.QueryService(SID_STopLevelBrowser, IShellBrowser)");

            browserObject = Marshal.GetObjectForIUnknown(browserPtr);
            var browser = (IShellBrowser)browserObject;
            hr = browser.QueryActiveShellView(out var shellView);
            ThrowIfFailed(hr, "IShellBrowser.QueryActiveShellView");

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

    private static void ThrowIfFailed(int hr, string operation)
    {
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
    }

    private static class ShellGuids
    {
        public static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        public static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
        public static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
        public static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
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
