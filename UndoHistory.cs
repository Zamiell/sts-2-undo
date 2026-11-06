namespace Undo;

/// <summary>A resumable save, plus the combat that was played from it, if any.</summary>
internal sealed class Checkpoint(long runStart, int location, string save, string player)
{
    public long RunStart { get; } = runStart;
    public int Location { get; } = location;
    public string Save { get; } = save;

    /// <summary>The player's persistent state, used to tell whether anything has changed since the save.</summary>
    public string Player { get; } = player;

    /// <summary>The game's CombatReplay recorded from this checkpoint. Opaque here so this class has no game dependency.</summary>
    public object? Replay { get; set; }

    /// <summary>Replay event indexes of each undoable action, oldest first.</summary>
    public List<int> Cuts { get; } = [];

    /// <summary>Replay event indexes at the start of each playable player turn.</summary>
    public List<int> TurnStarts { get; } = [];

    /// <summary>
    /// For each player action's event index, how far the combat had progressed when it was played, in checksums
    /// generated. A replay must not submit the action before reaching that point, or the game can reject it as too early.
    /// </summary>
    public Dictionary<int, uint> Progress { get; } = [];

    /// <summary>The player acted in this checkpoint's combat, but in a way that can only be undone by restarting it.</summary>
    public bool NeedsReload { get; set; }
}

/// <summary>A checkpoint to restore. If KeepEvents is set, replay that many of its recorded events afterwards.</summary>
internal sealed record UndoPlan(Checkpoint Target, int? KeepEvents);

/// <summary>Decides where each undo goes. Holds no game types, so it can be tested without the game.</summary>
internal sealed class UndoHistory
{
    private const int MaxCheckpoints = 32;
    private readonly List<Checkpoint> _checkpoints = [];

    public Checkpoint? Top => _checkpoints.Count > 0 ? _checkpoints[^1] : null;

    public void Clear() => _checkpoints.Clear();

    public void Capture(Checkpoint checkpoint)
    {
        if (Top is { } top && top.RunStart != checkpoint.RunStart)
        {
            Clear();
        }
        // A re-save of the same room with nothing changed would otherwise be an undo step that visibly does nothing.
        if (Top is { Cuts.Count: 0, NeedsReload: false } same && same.Location == checkpoint.Location
            && same.Player == checkpoint.Player)
        {
            _checkpoints.RemoveAt(_checkpoints.Count - 1);
        }
        _checkpoints.Add(checkpoint);
        if (_checkpoints.Count > MaxCheckpoints)
        {
            _checkpoints.RemoveAt(0);
        }
    }

    /// <summary>Records one player action. A clean action can be undone on its own; see UndoPatches for what clean means.</summary>
    public void RecordAction(int eventIndex, bool clean, uint progress = 0)
    {
        if (Top is not { } top)
        {
            return;
        }
        top.Progress[eventIndex] = progress;
        if (clean)
        {
            top.Cuts.Add(eventIndex);
        }
        else if (top.Cuts.Count == 0)
        {
            top.NeedsReload = true;
        }
    }

    public UndoPlan? Plan(bool inCombat, string? livePlayer)
    {
        if (Top is not { } top)
        {
            return null;
        }
        if (top.Cuts.Count > 0)
        {
            return new(top, top.Cuts[^1]);
        }
        if (inCombat ? top.NeedsReload : livePlayer != top.Player)
        {
            return new(top, null);
        }
        // Nothing has happened since the latest checkpoint, so the last action is behind it.
        if (_checkpoints.Count < 2)
        {
            return null;
        }
        var previous = _checkpoints[^2];
        return new(previous, previous.Cuts.Count > 0 ? previous.Cuts[^1] : null);
    }

    public void RecordTurnStart(int eventIndex)
    {
        if (Top is { } top && (top.TurnStarts.Count == 0 || top.TurnStarts[^1] != eventIndex))
        {
            top.TurnStarts.Add(eventIndex);
        }
    }

    public UndoPlan? PlanRestartTurn() => Top is { TurnStarts.Count: > 0 } top
        ? new(top, top.TurnStarts[^1])
        : null;

    public void Commit(UndoPlan plan)
    {
        _checkpoints.RemoveRange(_checkpoints.IndexOf(plan.Target) + 1,
            _checkpoints.Count - _checkpoints.IndexOf(plan.Target) - 1);
        plan.Target.NeedsReload = false;
        var kept = plan.KeepEvents ?? 0;
        plan.Target.Cuts.RemoveAll(cut => cut >= kept);
        if (plan.KeepEvents.HasValue)
        {
            plan.Target.TurnStarts.RemoveAll(start => start > kept);
        }
        else
        {
            plan.Target.TurnStarts.Clear();
        }
        foreach (var index in plan.Target.Progress.Keys.Where(index => index >= kept).ToList())
        {
            plan.Target.Progress.Remove(index);
        }
    }
}
