using Undo;

static Checkpoint Save(int location, string player = "a", long run = 1) => new(run, location, $"save-{location}", player);

static void Check(string name, bool passed)
{
    if (!passed)
    {
        throw new InvalidOperationException($"Failed: {name}");
    }
    Console.WriteLine($"ok: {name}");
}

// Undo in combat removes the most recent action, then the one before it.
{
    var history = new UndoHistory();
    var fight = Save(1);
    history.Capture(fight);
    history.RecordAction(3, clean: true);
    history.RecordAction(7, clean: true);
    var plan = history.Plan(inCombat: true, null)!;
    Check("targets the latest action", plan.Target == fight && plan.KeepEvents == 7);
    history.Commit(plan);
    Check("then the action before it", history.Plan(true, null)!.KeepEvents == 3);
}

// An action queued behind another undoes together with it.
{
    var history = new UndoHistory();
    history.Capture(Save(1));
    history.RecordAction(3, clean: true);
    history.RecordAction(5, clean: false);
    Check("queued action merges into the previous step", history.Plan(true, null)!.KeepEvents == 3);
}

// A combat with no replayable actions, such as one started by an event, restarts from its checkpoint.
{
    var history = new UndoHistory();
    var eventRoom = Save(1);
    history.Capture(Save(0));
    history.Capture(eventRoom);
    history.RecordAction(0, clean: false);
    var plan = history.Plan(true, null)!;
    Check("unreplayable combat reloads its checkpoint", plan.Target == eventRoom && plan.KeepEvents == null);
    history.Commit(plan);
    Check("reloading clears the pending flag", history.Plan(false, "a")!.Target != eventRoom);
}

// The rewards screen undoes picks first, then the action that won the combat.
{
    var history = new UndoHistory();
    var fight = Save(1);
    history.Capture(fight);
    history.RecordAction(9, clean: true);
    var rewards = Save(2, "after-combat");
    history.Capture(rewards);

    var pickedReward = history.Plan(false, "took-gold")!;
    Check("changed player reloads the rewards", pickedReward.Target == rewards && pickedReward.KeepEvents == null);
    history.Commit(pickedReward);

    var untouched = history.Plan(false, "after-combat")!;
    Check("untouched rewards go back into the fight", untouched.Target == fight && untouched.KeepEvents == 9);
    history.Commit(untouched);
    Check("the later checkpoint is discarded", history.Top == fight && fight.Cuts.Count == 0);
    Check("the undone action's progress is forgotten", fight.Progress.Count == 0);
}

// Entering a new room can be undone back to the previous room.
{
    var history = new UndoHistory();
    var shop = Save(1);
    history.Capture(shop);
    history.Capture(Save(2));
    var plan = history.Plan(false, "a")!;
    Check("untouched room goes back a room", plan.Target == shop && plan.KeepEvents == null);
}

// There is nothing to undo at the very start.
{
    var history = new UndoHistory();
    Check("empty history has no plan", history.Plan(false, "a") == null);
    history.Capture(Save(1));
    Check("single untouched checkpoint has no plan", history.Plan(false, "a") == null);
    Check("single untouched combat has no plan", history.Plan(true, null) == null);
}

// Re-saving a room with no changes doesn't add a no-op undo step.
{
    var history = new UndoHistory();
    history.Capture(Save(0));
    var first = Save(1);
    history.Capture(first);
    var second = Save(1);
    history.Capture(second);
    Check("same room and player replaces the checkpoint", history.Top == second
        && history.Plan(false, "a")!.Target != first);

    var fight = Save(2);
    history.Capture(fight);
    history.RecordAction(1, clean: true);
    history.Capture(Save(2));
    Check("a checkpoint with actions is kept", history.Plan(false, "a")!.Target == fight);
}

// Starting another run forgets the old one.
{
    var history = new UndoHistory();
    history.Capture(Save(1, run: 1));
    history.Capture(Save(2, run: 2));
    Check("a new run clears the history", history.Plan(false, "a") == null);
}

// History is bounded, and undo still works at the limit.
{
    var history = new UndoHistory();
    for (var i = 0; i < 100; i++)
    {
        history.Capture(Save(i));
    }
    var count = 0;
    while (history.Plan(false, "a") is { } plan)
    {
        history.Commit(plan);
        count++;
    }
    Check("history keeps the last 32 checkpoints", count == 31);
}

Console.WriteLine("All behavior checks passed.");
