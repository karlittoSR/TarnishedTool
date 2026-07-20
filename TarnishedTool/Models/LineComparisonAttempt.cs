//

using System.ComponentModel;
using System.Runtime.CompilerServices;
using TarnishedTool.Utilities;

namespace TarnishedTool.Models;

public class LineComparisonAttempt : INotifyPropertyChanged
{
    public int Number { get; }
    public string NumberText => IsProtected ? "" : Number.ToString();
    public bool IsPersistentPb { get; }
    public bool IsReference { get; }
    public bool IsProtected => IsPersistentPb || IsReference;

    private uint _resultMs;
    public uint ResultMs => _resultMs;
    public string ResultText => TimeFormatter.Mmssmmm(ResultMs);

    public LineComparisonAttempt(int number, string name, uint resultMs,
        bool isPersistentPb = false, bool isReference = false)
    {
        Number = number;
        _name = name;
        _resultMs = resultMs;
        IsPersistentPb = isPersistentPb;
        IsReference = isReference;
    }

    // Only the view model may advance the persistent PB after a genuine record.
    public void UpdatePersistentPb(uint resultMs)
    {
        if (!IsProtected || _resultMs == resultMs) return;
        _resultMs = resultMs;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResultMs)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResultText)));
    }

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            if (IsProtected) return;
            SetProperty(ref _name, value);
        }
    }

    // Delta vs the current best attempt. Recomputed by the view model.
    private string _deltaText = "";
    public string DeltaText
    {
        get => _deltaText;
        set => SetProperty(ref _deltaText, value);
    }

    private bool _isBest;
    public bool IsBest
    {
        get => _isBest;
        set => SetProperty(ref _isBest, value);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
