//

using System.ComponentModel;
using System.Runtime.CompilerServices;
using TarnishedTool.Utilities;

namespace TarnishedTool.Models;

public class LineComparisonAttempt : INotifyPropertyChanged
{
    public int Number { get; }
    public uint ResultMs { get; }
    public string ResultText => TimeFormatter.Mmssmmm(ResultMs);

    public LineComparisonAttempt(int number, string name, uint resultMs)
    {
        Number = number;
        _name = name;
        ResultMs = resultMs;
    }

    private string _name;
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
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
