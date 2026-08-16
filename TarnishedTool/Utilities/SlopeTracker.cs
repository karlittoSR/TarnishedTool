//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace TarnishedTool.Utilities;

public enum SlopeState
{
    // Not enough movement to read the ground: standing still, freshly loaded, or warped.
    Unknown,
    Uphill,
    Flat,
    Downhill
}

// Reads the ground the player is running over, so the jump indicator can say whether a
// jump is worth it. The quantity that matters is the slope along the direction of travel
// (rise divided by the horizontal distance actually covered), not the raw dY/dt: dividing
// by distance instead of time makes the reading independent of walking, sprinting or
// riding Torrent, so a single pair of thresholds holds everywhere. Samples older than a
// second are dropped, and that window is what smooths out stairs, roots and one-frame
// physics pops that would otherwise flip the colour constantly.
public sealed class SlopeTracker
{
    private const double WindowSeconds = 1.0;

    // A reading needs this much history behind it, otherwise the first few ticks after
    // enabling the overlay would classify off two samples.
    private const double MinWindowSeconds = 0.35;

    // Horizontal distance that must be covered inside the window before the gradient
    // means anything. Below it the player is essentially stationary and the division
    // amplifies idle-animation jitter into wild slopes.
    private const float MinPathLength = 0.6f;

    // ~3.4 degrees. Flatter than this counts as neutral ground, where a jump still gains
    // time, so it shares the "go" side of the indicator with uphill.
    private const float FlatGradient = 0.06f;

    // Deadband applied around FlatGradient so a run along a threshold slope does not
    // strobe between two colours.
    private const float Hysteresis = 0.02f;

    // No single 64 ms tick covers this much ground. Anything larger is a warp, a grace
    // load, or crossing out of the overworld into a legacy dungeon's local coordinates.
    private const float TeleportDistance = 25f;

    private readonly Queue<Sample> _samples = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private double _pathLength;
    private Vector3 _lastPos;
    private bool _hasLastPos;
    private Sample _newest;

    public SlopeState State { get; private set; } = SlopeState.Unknown;

    // Rise over horizontal run, e.g. 0.1 is a 10% slope. Zero while State is Unknown.
    public float Gradient { get; private set; }

    public void Reset()
    {
        _samples.Clear();
        _pathLength = 0;
        _hasLastPos = false;
        State = SlopeState.Unknown;
        Gradient = 0f;
    }

    public void Add(Vector3 worldPos)
    {
        var now = _clock.Elapsed.TotalSeconds;

        if (_hasLastPos)
        {
            var step = worldPos - _lastPos;
            if (step.Length() > TeleportDistance)
                Reset();
            else
                _pathLength += Math.Sqrt(step.X * step.X + step.Z * step.Z);
        }

        _lastPos = worldPos;
        _hasLastPos = true;

        _newest = new Sample(now, worldPos.Y, _pathLength);
        _samples.Enqueue(_newest);

        while (_samples.Count > 2 && now - _samples.Peek().Time > WindowSeconds)
            _samples.Dequeue();

        Evaluate();
    }

    private void Evaluate()
    {
        if (_samples.Count < 2)
        {
            Gradient = 0f;
            State = SlopeState.Unknown;
            return;
        }

        var oldest = _samples.Peek();
        var elapsed = _newest.Time - oldest.Time;
        var path = _newest.Path - oldest.Path;

        if (elapsed < MinWindowSeconds || path < MinPathLength)
        {
            Gradient = 0f;
            State = SlopeState.Unknown;
            return;
        }

        Gradient = (float)((_newest.Height - oldest.Height) / path);
        State = Classify(Gradient, State);
    }

    private static SlopeState Classify(float gradient, SlopeState current)
    {
        // Widen the band the current state sits in so leaving it costs more than entering.
        var upper = FlatGradient;
        var lower = -FlatGradient;

        switch (current)
        {
            case SlopeState.Uphill:
                upper -= Hysteresis;
                break;
            case SlopeState.Downhill:
                lower += Hysteresis;
                break;
            default:
                upper += Hysteresis;
                lower -= Hysteresis;
                break;
        }

        if (gradient >= upper) return SlopeState.Uphill;
        if (gradient <= lower) return SlopeState.Downhill;
        return SlopeState.Flat;
    }

    private readonly struct Sample(double time, float height, double path)
    {
        public double Time { get; } = time;
        public float Height { get; } = height;
        public double Path { get; } = path;
    }
}
