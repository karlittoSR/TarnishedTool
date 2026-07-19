//

using System.ComponentModel;
using System.Text.Json.Serialization;
using TarnishedTool.Utilities;

namespace TarnishedTool.Models;

// One entry in the saved-lines library: a user-given name, the shareable line
// code (the same TTLINE1 token produced by "Export position"), and the best
// (gold) time achieved on that line — a personal best that stays up to date.
//
// Persisted as JSON (see SavedLinesStore). Serialization uses this single
// constructor: System.Text.Json matches JSON properties to the parameters by
// name (case-insensitive), so adding future fields (e.g. character stats,
// equipment) means adding a property + constructor parameter here.
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

    [JsonIgnore]
    public string BestText => _bestMs > 0 ? TimeFormatter.Mmssmmm(_bestMs) : "";

    // Optional character state (equipment + stats + rune level) captured when the
    // line was saved. Null for position-only lines and for legacy entries.
    private CharacterSnapshot _snapshot;
    public CharacterSnapshot Snapshot
    {
        get => _snapshot;
        set
        {
            _snapshot = value;
            Raise(nameof(Snapshot));
            Raise(nameof(HasSnapshot));
            Raise(nameof(SnapshotMarker));
        }
    }

    [JsonIgnore]
    public bool HasSnapshot => _snapshot != null;

    // Small marker shown in the library list for lines that carry a snapshot.
    [JsonIgnore]
    public string SnapshotMarker => HasSnapshot ? "★" : "";

    public event PropertyChangedEventHandler PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
