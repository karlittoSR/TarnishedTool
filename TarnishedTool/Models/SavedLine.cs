//

using System.ComponentModel;
using System.Text.Json.Serialization;
using TarnishedTool.Utilities;

namespace TarnishedTool.Models;

// One entry in the saved-lines library: a user-given name, its encoded segment
// definition (mandatory start plus a position or event-flag finish), the local
// personal best, and an optional read-only reference supplied by another runner.
//
// Persisted as JSON (see SavedLinesStore). Serialization uses this single
// constructor: System.Text.Json matches JSON properties to the parameters by
// name (case-insensitive), so adding future fields (e.g. character stats,
// equipment) means adding a property + constructor parameter here.
public class SavedLine : INotifyPropertyChanged
{
    private string _code;
    public string Code
    {
        get => _code;
        private set
        {
            if (_code == value) return;
            _code = value;
            Raise(nameof(Code));
        }
    }

    public SavedLine(string name, string code, uint bestMs = 0, uint referenceMs = 0)
    {
        _name = name;
        Code = code;
        _bestMs = bestMs;
        _referenceMs = referenceMs;
    }

    // Replaces the encoded segment definition while keeping the entry's identity,
    // PB and character snapshot intact.
    public void UpdateCode(string code) => Code = code;

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
    public string BestText => _bestMs > 0 ? $"PB {TimeFormatter.Mmssmmm(_bestMs)}" : "";

    // A shared runner's time is comparison-only. Local attempts never overwrite
    // it; it can only be removed explicitly from the protected Reference row.
    private uint _referenceMs;
    public uint ReferenceMs
    {
        get => _referenceMs;
        set
        {
            if (_referenceMs == value) return;
            _referenceMs = value;
            Raise(nameof(ReferenceMs));
            Raise(nameof(ReferenceText));
        }
    }

    [JsonIgnore]
    public string ReferenceText => _referenceMs > 0
        ? $"Ref {TimeFormatter.Mmssmmm(_referenceMs)}"
        : "";

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
