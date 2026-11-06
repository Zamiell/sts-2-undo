using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace Undo;

// Every save the game writes is a state it knows how to resume, so each one is a checkpoint.
[HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun), [typeof(SerializableRun), typeof(bool)])]
internal static class SaveCapturePatch
{
    private static void Prefix(SerializableRun save, bool isMultiplayer)
    {
        // The save written during an undo is a checkpoint we already hold.
        if (!isMultiplayer && !UndoService.Busy)
        {
            UndoService.Capture(save);
        }
    }
}

// The writer forgets its replay when the combat ends, but we still need it to undo the winning action.
[HarmonyPatch(typeof(CombatReplayWriter), nameof(CombatReplayWriter.RecordInitialState))]
internal static class RecordInitialStatePatch
{
    private static readonly AccessTools.FieldRef<CombatReplayWriter, CombatReplay?> Replay =
        AccessTools.FieldRefAccess<CombatReplayWriter, CombatReplay?>("_replay");

    private static void Postfix(CombatReplayWriter __instance)
    {
        if (UndoService.History.Top is { } top)
        {
            top.Replay = Replay(__instance);
        }
    }
}

[HarmonyPatch(typeof(ActionQueueSet), nameof(ActionQueueSet.EnqueueWithoutSynchronizing))]
internal static class ActionCutPatch
{
    private static void Prefix(ActionQueueSet __instance, GameAction gameAction)
    {
        // Busy covers the replay itself, whose actions are already in the history.
        if (UndoService.Busy || !ActionQueueSet.IsGameActionPlayerDriven(gameAction)
            || !CombatManager.Instance.IsInProgress || UndoService.History.Top?.Replay is not CombatReplay replay)
        {
            return;
        }
        var run = RunManager.Instance;
        // An action queued behind another can't be removed on its own, because the earlier action's later events are
        // interleaved after it. A combat started by an event can't be replayed, because loading restarts the event.
        var clean = __instance.IsEmpty && run.ActionExecutor.CurrentlyRunningAction == null
            && run.ActionQueueSynchronizer.CombatState == ActionSynchronizerCombatState.PlayPhase
            && run.DebugOnlyGetState()?.CurrentRoom is CombatRoom { ParentEventId: null };
        UndoService.History.RecordAction(replay.events.Count, clean, run.ChecksumTracker.NextId);
    }
}

// Ending the run writes its history and deletes the save, which an undo couldn't take back.
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentRun))]
internal static class RunEndedPatch
{
    private static void Prefix() => UndoService.History.Clear();
}

// The cover blocks clicks, but keyboard shortcuts could still open screens over the run being rebuilt.
[HarmonyPatch(typeof(NHotkeyManager), nameof(NHotkeyManager._UnhandledInput))]
internal static class HotkeyPatch
{
    private static bool Prefix() => !UndoService.Busy;
}

[HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]
internal static class TopBarPatch
{
    private static void Postfix(NTopBar __instance) => UndoButton.AddTo(__instance);
}
