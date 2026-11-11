using System;

namespace Installer.UI;

/// <summary>
///     Overall progress from the MSI progress messages (INSTALLMESSAGE_PROGRESS): script creation is the first tenth,
///     execution the rest. Fields as documented for MsiProcessMessage: [1] message kind, [2] ticks, [3] direction or
///     whether ActionData moves the bar, [4] the phase of a reset.
/// </summary>
public sealed class ProgressCounter
{
    private const double ScriptShare = 0.1;

    private long _total;
    private long _done;
    private long _step;
    private bool _forward = true;
    private bool _stepOnActionData;
    private bool _executing;

    /// <summary>From 0 to 1.</summary>
    public double Progress { get; private set; }

    /// <summary>Handles a Progress message; <paramref name="fields" /> are fields 1–4 of its record.</summary>
    public void OnProgress(int[] fields)
    {
        switch (fields[0])
        {
            case 0: // reset: total ticks, direction, phase (0 — execution, 1 — script creation)
                _total = Math.Max(1, fields[1]);
                _forward = fields[2] == 0;
                _executing = fields[3] == 0;
                _done = _forward ? 0 : _total;
                _stepOnActionData = false;
                break;
            case 1: // ticks for each ActionData of the current action
                _step = fields[1];
                _stepOnActionData = fields[2] != 0;
                break;
            case 2: // explicit move
                Move(fields[1]);
                break;
            case 3: // more work than announced
                _total += fields[1];
                break;
        }

        Update();
    }

    /// <summary>Handles an ActionData message: some actions move the bar by a fixed step per item.</summary>
    public void OnActionData()
    {
        if (!_stepOnActionData) return;
        Move(_step);
        Update();
    }

    private void Move(long ticks) => _done = Math.Max(0, Math.Min(_total, _done + (_forward ? ticks : -ticks)));

    private void Update()
    {
        var fraction = _total <= 0 ? 0 : (double)_done / _total;
        var value = _executing ? ScriptShare + (1 - ScriptShare) * fraction : ScriptShare * fraction;
        // Never back: the rollback after a failure is shown by the error page, not by the bar.
        if (value > Progress) Progress = Math.Min(1, value);
    }
}
