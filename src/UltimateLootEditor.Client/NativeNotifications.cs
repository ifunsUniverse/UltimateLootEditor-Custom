using System;
using EFT.Communications;

namespace ULE.SpawnEditor
{
    internal static class NativeNotifications
    {
        public static void Show(string message)
        {
            if (Plugin.EnableEditorNotifications != null && !Plugin.EnableEditorNotifications.Value)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var text = message.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                NotificationManager.DisplayMessageNotification(
                    text,
                    ENotificationDurationType.Default,
                    ENotificationIconType.Default,
                    null);
            }
            catch
            {
                // Notifications are cosmetic. If EFT has not initialized this UI yet, keep the editor flow intact.
            }
        }
    }
}
