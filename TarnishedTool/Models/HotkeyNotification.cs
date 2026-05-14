namespace TarnishedTool.Models
{
    public class HotkeyNotification
    {
        public string ActionName { get; set; }
        public bool? IsEnabled { get; set; } // null = action (not a toggle), true = ON, false = OFF
        public double Opacity { get; set; } = 1.0;

        public string DisplayText => IsEnabled.HasValue 
            ? $"{ActionName} {(IsEnabled.Value ? "ON" : "OFF")}" 
            : ActionName;

        public string StateColor => IsEnabled.HasValue
            ? (IsEnabled.Value ? "#4CAF50" : "#F44336") // Green for ON, Red for OFF
            : "#FFA726"; // Orange for actions
    }
}
