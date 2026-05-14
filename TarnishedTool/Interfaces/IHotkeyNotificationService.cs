using TarnishedTool.Enums;

namespace TarnishedTool.Interfaces
{
    public interface IHotkeyNotificationService
    {
        void ShowNotification(HotkeyActions action, bool? isEnabled = null);
        void ShowCustomNotification(string message);
        bool IsEnabled { get; set; }
    }
}
