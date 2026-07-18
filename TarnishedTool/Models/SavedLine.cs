//

using System.ComponentModel;
using TarnishedTool.Utilities;

namespace TarnishedTool.Models;

// One entry in the saved-lines library: a user-given name, the shareable line
// code (the same TTLINE1 token produced by "Export position"), and the best
// (gold) time achieved on that line — a personal best that stays up to date.
public class SavedLine : INotifyPropertyChanged
{
    public string Code { get; }

    public SavedLine(string name, string code, uint bestMs = 0)
    {
        _name = name;
        Code = code;
        _bestMs = bestMs;
    }

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            Raise(nameof(Name));
        }
    }

    private uint _bestMs;
    public uint BestMs
    {
        get => _bestMs;
        set
        {
            if (_bestMs == value) return;
            _bestMs = value;
            Raise(nameof(BestMs));
            Raise(nameof(BestText));
        }
    }

    public string BestText => _bestMs > 0 ? TimeFormatter.Mmssmmm(_bestMs) : "";

    public event PropertyChangedEventHandler PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
