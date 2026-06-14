namespace DeskRealm.App.Services;

internal sealed class VirtualDesktopNavigatorService
{
    private readonly KeyboardInputService _keyboard;
    private readonly FileLogger _logger;

    public VirtualDesktopNavigatorService(KeyboardInputService keyboard, FileLogger logger)
    {
        _keyboard = keyboard;
        _logger = logger;
    }

    public void NavigateByNumber(int currentNumber, int targetNumber, int desktopCount, int stepDelayMs)
    {
        if (desktopCount < 1)
        {
            throw new InvalidOperationException("No virtual desktop available for navigation.");
        }

        if (targetNumber < 1 || targetNumber > desktopCount)
        {
            throw new InvalidOperationException($"Target virtual desktop #{targetNumber} not found. Available desktops: 1 to {desktopCount}.");
        }

        if (currentNumber < 1 || currentNumber > desktopCount)
        {
            throw new InvalidOperationException($"Invalid current virtual desktop #{currentNumber}. Available desktops: 1 to {desktopCount}.");
        }

        var delta = targetNumber - currentNumber;
        if (delta == 0)
        {
            _logger.Info($"Hotkey desktop navigation ignored: already on #{targetNumber}.");
            return;
        }

        var direction = delta > 0 ? 1 : -1;
        var steps = Math.Abs(delta);
        _logger.Info($"Hotkey desktop navigation: #{currentNumber} -> #{targetNumber} ({steps} step(s), direction {direction}).");

        for (var i = 0; i < steps; i++)
        {
            _keyboard.SwitchVirtualDesktopStep(direction);
            if (stepDelayMs > 0 && i < steps - 1)
            {
                Thread.Sleep(stepDelayMs);
            }
        }
    }
}
