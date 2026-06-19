using System.Diagnostics;

namespace DeskRealm.App.Services;

internal sealed class VirtualDesktopNavigatorService
{
    private readonly KeyboardInputService _keyboard;
    private readonly VirtualDesktopRegistryService _virtualDesktop;
    private readonly FileLogger _logger;

    public VirtualDesktopNavigatorService(
        KeyboardInputService keyboard,
        VirtualDesktopRegistryService virtualDesktop,
        FileLogger logger)
    {
        _keyboard = keyboard;
        _virtualDesktop = virtualDesktop;
        _logger = logger;
    }

    public VirtualDesktopInfo NavigateByNumber(
        VirtualDesktopInfo current,
        VirtualDesktopInfo target,
        IReadOnlyList<VirtualDesktopInfo> desktops,
        int stepConfirmationTimeoutMs)
    {
        if (desktops.Count < 1)
        {
            throw new InvalidOperationException("No virtual desktop available for navigation.");
        }

        if (current.Id == target.Id)
        {
            _logger.Info($"Hotkey desktop navigation ignored: already on #{target.Number}.");
            return current;
        }

        var currentIndex = desktops.ToList().FindIndex(d => d.Id == current.Id);
        var targetIndex = desktops.ToList().FindIndex(d => d.Id == target.Id);
        if (currentIndex < 0 || targetIndex < 0)
        {
            throw new InvalidOperationException(
                $"Virtual desktop navigation state changed before input. Current={current.Id:B}, target={target.Id:B}.");
        }

        var direction = targetIndex > currentIndex ? 1 : -1;
        var total = Math.Abs(targetIndex - currentIndex);
        var operation = Stopwatch.StartNew();
        _logger.Info(
            $"Hotkey desktop navigation: #{current.Number} -> #{target.Number} " +
            $"({total} confirmed step(s), direction {direction}).");

        var confirmed = current;
        for (var step = 1; step <= total; step++)
        {
            var expectedIndex = currentIndex + direction * step;
            var expected = desktops[expectedIndex];
            var stepWatch = Stopwatch.StartNew();

            _keyboard.SwitchVirtualDesktopStep(direction);
            confirmed = WaitForDesktop(expected.Id, stepConfirmationTimeoutMs);
            stepWatch.Stop();

            _logger.Info(
                $"[PERF] desktop-step {step}/{total}: expected=#{expected.Number} {expected.Id:B}, " +
                $"confirmed=#{confirmed.Number}, elapsed={stepWatch.Elapsed.TotalMilliseconds:0.0} ms.");
        }

        operation.Stop();
        _logger.Info(
            $"[PERF] desktop-navigation complete: #{current.Number} -> #{target.Number}, " +
            $"elapsed={operation.Elapsed.TotalMilliseconds:0.0} ms.");
        return confirmed;
    }

    public VirtualDesktopInfo WaitForDesktop(Guid expectedDesktopId, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastError = null;
        var probe = 0;

        while (stopwatch.ElapsedMilliseconds <= timeoutMs)
        {
            try
            {
                var current = _virtualDesktop.GetCurrentVirtualDesktop();
                if (current.Id == expectedDesktopId)
                {
                    return current;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            AdaptiveWait(probe++);
        }

        if (lastError is not null)
        {
            _logger.Warn($"Desktop confirmation had registry read errors: {lastError.Message}");
        }

        var detected = _virtualDesktop.GetCurrentVirtualDesktop();
        throw new TimeoutException(
            $"Windows did not confirm virtual desktop {expectedDesktopId:B} within {timeoutMs} ms. " +
            $"Detected current desktop: #{detected.Number} {detected.Name} {detected.Id:B}.");
    }

    private static void AdaptiveWait(int probe)
    {
        if (probe < 3)
        {
            Thread.Yield();
            return;
        }

        Thread.Sleep(Math.Min(48, 2 << Math.Min(4, probe - 3)));
    }
}
