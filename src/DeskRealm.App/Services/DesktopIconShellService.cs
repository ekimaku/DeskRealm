using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;

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

        try
        {
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
                        throw new InvalidOperationException($"IFolderView.Item({index}) a retourné un PIDL vide.");
                    }

                    var itemKey = BuildPidlItemKey(pidl);

                    hr = view.GetItemPosition(pidl, out var point);
                    ThrowIfFailed(hr, $"IFolderView.GetItemPosition('{itemKey}')");

                    positions.Add(new DesktopIconPosition
                    {
                        ItemKey = itemKey,
                        DisplayName = BuildTechnicalDisplayName(index, itemKey),
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

            ValidateNoDuplicateItemKeys(positions, "sauvegarder");
            _logger.Info($"Icon layout capture phase: captured {positions.Count} positions.");
            return positions;
        }
        finally
        {
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
        var pidlsToFree = new List<IntPtr>();

        try
        {
            var count = GetFolderViewItemCount(view);
            _logger.Info($"Icon layout restore phase: visible item count = {count}.");

            var currentItems = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < count; index++)
            {
                var hr = view.Item(index, out var pidl);
                ThrowIfFailed(hr, $"IFolderView.Item({index})");
                if (pidl == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"IFolderView.Item({index}) a retourné un PIDL vide.");
                }

                var itemKey = BuildPidlItemKey(pidl);
                if (currentItems.ContainsKey(itemKey))
                {
                    Marshal.FreeCoTaskMem(pidl);
                    throw new InvalidOperationException(
                        $"Layout icônes ambigu : deux items Desktop actuels ont la même clé PIDL '{itemKey}'. " +
                        "DeskRealm refuse d'appliquer un layout ambigu.");
                }

                currentItems.Add(itemKey, pidl);
                pidlsToFree.Add(pidl);
            }

            var missing = new List<string>();
            var targets = new List<RestoreTarget>();

            foreach (var saved in savedPositions)
            {
                if (!currentItems.TryGetValue(saved.ItemKey, out var pidl))
                {
                    missing.Add(string.IsNullOrWhiteSpace(saved.DisplayName) ? saved.ItemKey : saved.DisplayName);
                    continue;
                }

                targets.Add(new RestoreTarget(
                    saved.ItemKey,
                    string.IsNullOrWhiteSpace(saved.DisplayName) ? saved.ItemKey : saved.DisplayName,
                    pidl,
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
                $"(attempted={attempted}, missing={missing.Count}, unresolved={unresolved.Count}).");
            return verifiedRestored;
        }
        finally
        {
            foreach (var pidl in pidlsToFree)
            {
                Marshal.FreeCoTaskMem(pidl);
            }

            ReleaseComObject(view);
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
            throw new InvalidOperationException($"Nombre d'icônes Desktop invalide : {count}.");
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
            throw new InvalidOperationException("PIDL vide : clé item impossible.");
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
                throw new InvalidOperationException($"PIDL invalide : segment cb={cb}, offset={offset}.");
            }

            var segment = new byte[cb];
            Marshal.Copy(IntPtr.Add(pidl, offset), segment, 0, cb);
            bytes.AddRange(segment);
            offset += cb;
        }

        throw new InvalidOperationException($"PIDL invalide : terminateur introuvable avant {maxPidlBytes} bytes.");
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
                $"Layout icônes ambigu : plusieurs icônes portent la même clé item '{duplicate.Key}'. " +
                $"DeskRealm refuse de {action} un layout ambigu.");
        }
    }

    private static void ValidateLayoutHasStableKeys(IReadOnlyList<DesktopIconPosition> savedPositions)
    {
        var legacy = savedPositions.Where(p => string.IsNullOrWhiteSpace(p.ItemKey)).ToList();
        if (legacy.Count > 0)
        {
            throw new InvalidOperationException(
                "Layout icônes ancien format détecté : il ne contient pas les clés item stables ajoutées en v0.3.3. " +
                "Lance 'Save icon layout now' une fois sur ce realm pour régénérer le layout.");
        }

        ValidateNoDuplicateItemKeys(savedPositions, "restaurer");
    }

    private static IFolderView GetDesktopFolderView()
    {
        var shellWindowsType = Type.GetTypeFromCLSID(ShellGuids.CLSID_ShellWindows)
            ?? throw new InvalidOperationException("CLSID_ShellWindows introuvable.");

        object? shellWindowsObject = null;
        object? dispatch = null;
        IntPtr browserPtr = IntPtr.Zero;
        object? browserObject = null;

        try
        {
            shellWindowsObject = Activator.CreateInstance(shellWindowsType)
                ?? throw new InvalidOperationException("Impossible de créer ShellWindows.");

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
                throw new InvalidOperationException("FindWindowSW n'a retourné aucun dispatch Desktop.");
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
            throw new InvalidOperationException($"{operation} a échoué avec HRESULT 0x{hr:X8}.", Marshal.GetExceptionForHR(hr));
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

    private static class ShellConstants
    {
        public const int CSIDL_DESKTOP = 0;
        public const int SWC_DESKTOP = 8;
        public const int SWFO_NEEDDISPATCH = 1;
        public const uint SVGIO_ALLVIEW = 0x00000002;
        public const uint SVSI_POSITIONITEM = 0x00000080;
        public const uint SVSI_NOTAKEFOCUS = 0x40000000;
    }

    private static class ShellGuids
    {
        public static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        public static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
        public static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
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
