using DeskRealm.App.Services;
using System.Drawing;
using System.Windows.Forms;

namespace DeskRealm.App.UI;

internal static class DeskRealmIcon
{
    public static Icon Load(FileLogger logger)
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null)
            {
                logger.Info($"DeskRealm application icon loaded from executable: {Application.ExecutablePath}");
                return (Icon)icon.Clone();
            }

            logger.Warn($"DeskRealm application icon could not be extracted from executable: {Application.ExecutablePath}");
        }
        catch (Exception ex)
        {
            logger.Warn($"DeskRealm application icon extraction failed: {ex.Message}");
        }

        logger.Warn("DeskRealm is using the default Windows application icon because the embedded icon was unavailable.");
        return (Icon)SystemIcons.Application.Clone();
    }
}
