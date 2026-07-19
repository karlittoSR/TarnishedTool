// 

using System.Threading;
using System.Threading.Tasks;
using TarnishedTool.Models;

namespace TarnishedTool.Interfaces;

public interface IFlaskService
{
    Task TryUpgradeFlask(CancellationToken ct = default);
    Task TryIncreaseCharges(CancellationToken ct = default);

    // Read the current flask upgrade level and HP/FP charge split.
    FlaskSnapshot CaptureFlasks();

    // Restore a captured flask level + HP/FP split, swapping the physical flask
    // goods to match. Safe to call anywhere the flask talk-commands work.
    void ApplyFlasks(FlaskSnapshot snapshot);
}