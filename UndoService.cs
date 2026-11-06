using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Settings;

namespace Undo;

internal static class UndoService
{
    // The screen is covered during a replay, so run it as fast as it will go.
    private const double ReplayTimeScale = 20;
    private const double ReplayTimeoutSeconds = 120;

    private static readonly MethodInfo SetState = AccessTools.PropertySetter(typeof(RunManager), "State")
        ?? throw new MissingMethodException(nameof(RunManager), "State");
    private static readonly MethodInfo InitializeShared = AccessTools.Method(typeof(RunManager), "InitializeShared")
        ?? throw new MissingMethodException(nameof(RunManager), "InitializeShared");
    private static readonly MethodInfo InitializeRunLobby = AccessTools.Method(typeof(RunManager), "InitializeRunLobby")
        ?? throw new MissingMethodException(nameof(RunManager), "InitializeRunLobby");
    private static readonly MethodInfo InitializeSavedRun = AccessTools.Method(typeof(RunManager), "InitializeSavedRun")
        ?? throw new MissingMethodException(nameof(RunManager), "InitializeSavedRun");

    public static UndoHistory History { get; } = new();
    public static bool Busy { get; private set; }

    public static bool IsSingleplayer => RunManager.Instance.IsSingleplayerOrFakeMultiplayer;

    public static bool CanUndo => Ready && CurrentPlan() != null;

    public static bool CanRestartCombat => Ready && CombatManager.Instance.IsInProgress
        && RunManager.Instance.DebugOnlyGetState()?.CurrentRoom is CombatRoom { ParentEventId: null }
        && History.Top != null;

    public static bool CanRestartTurn => CanRestartCombat
        && CombatManager.Instance.DebugOnlyGetState()?.CurrentSide == CombatSide.Player
        && !CombatManager.Instance.EndingPlayerTurnPhaseOne && !CombatManager.Instance.EndingPlayerTurnPhaseTwo
        && RunManager.Instance.ActionQueueSynchronizer.CombatState == ActionSynchronizerCombatState.PlayPhase
        && RunManager.Instance.ActionQueueSet.IsEmpty && RunManager.Instance.ActionExecutor.CurrentlyRunningAction == null
        && !RunManager.Instance.ActionExecutor.IsPaused
        && History.Top?.Replay is CombatReplay && History.PlanRestartTurn() != null;

    public static void RecordTurnStart(CombatState state)
    {
        if (!Busy && IsSingleplayer && state.CurrentSide == CombatSide.Player
            && RunManager.Instance.DebugOnlyGetState()?.CurrentRoom is CombatRoom { ParentEventId: null }
            && History.Top?.Replay is CombatReplay replay)
        {
            History.RecordTurnStart(replay.events.Count);
        }
    }

    private static bool Ready => !Busy && IsSingleplayer && !RunManager.Instance.IsCleaningUp && NRun.Instance != null
        && NGame.Instance is { Transition.InTransition: false }
        && !NRun.Instance.GlobalUi.MapScreen.IsTraveling
        && !CombatManager.Instance.IsEnding && !CombatTransitioning;

    /// <summary>In a combat room with no combat running: it hasn't started yet, or its final save is still on the way.</summary>
    private static bool CombatTransitioning => !CombatManager.Instance.IsInProgress
        && RunManager.Instance.DebugOnlyGetState()?.CurrentRoom is CombatRoom { IsPreFinished: false };

    public static void Capture(SerializableRun save)
    {
        History.Capture(new Checkpoint(save.StartTime, save.CurrentActIndex * 1000 + save.VisitedMapCoords.Count,
            JsonSerializationUtility.ToJson(save), PlayerFingerprint(save.Players[0])));
    }

    public static async Task Undo()
    {
        if (!CanUndo || CurrentPlan() is not { } plan)
        {
            return;
        }
        await Restore(plan);
    }

    public static async Task RestartCombat()
    {
        if (!CanRestartCombat || History.Top is not { } checkpoint)
        {
            return;
        }
        await Restore(new UndoPlan(checkpoint, null));
    }

    public static async Task RestartTurn()
    {
        if (!CanRestartTurn || History.PlanRestartTurn() is not { } plan)
        {
            return;
        }
        await Restore(plan);
    }

    private static async Task Restore(UndoPlan plan)
    {
        Busy = true;
        var replay = plan.KeepEvents.HasValue ? plan.Target.Replay as CombatReplay : null;
        var events = replay?.events.Take(plan.KeepEvents!.Value).ToList();
        History.Commit(plan);

        var game = NGame.Instance!;
        var viewport = game.GetViewport();
        var prefs = SaveManager.Instance.PrefsSave;
        var fastMode = prefs.FastMode;
        var cover = UndoButton.ShowCover();
        viewport.GuiDisableInput = true;
        prefs.FastMode = FastModeType.Instant;
        Engine.TimeScale = ReplayTimeScale;
        SetVolume(0);
        string? message = null;
        try
        {
            await game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (SaveManager.Instance.CurrentRunSaveTask is { } pending)
            {
                await pending;
            }
            message = await Restart(plan.Target, replay, events);
            Log.Info($"[Undo] Restored checkpoint, replayed {events?.Count ?? 0} events.");
        }
        catch (Exception exception)
        {
            // The old run is gone by now, so fall back to the checkpoint itself rather than leaving the player stuck.
            Log.Error($"[Undo] Replay failed, restarting from the checkpoint instead: {exception}");
            message = "Undo failed, so this room was restarted";
            History.Commit(new UndoPlan(plan.Target, null));
            await Restart(plan.Target, null, null);
        }
        finally
        {
            Engine.TimeScale = 1;
            prefs.FastMode = fastMode;
            SetVolume(SaveManager.Instance.SettingsSave.VolumeMaster);
            viewport.GuiDisableInput = false;
            cover.QueueFree();
            Busy = false;
        }
        if (message != null)
        {
            game.AddChild(NFullscreenTextVfx.Create(message));
        }
    }

    private static UndoPlan? CurrentPlan()
    {
        var inCombat = CombatManager.Instance.IsInProgress;
        return History.Plan(inCombat, inCombat ? null : LivePlayerFingerprint());
    }

    private static string? LivePlayerFingerprint()
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        return state == null ? null : PlayerFingerprint(LocalContext.GetMe(state)!.ToSerializable());
    }

    /// <summary>Only what the player can see. RNG counters advance on their own when a room is opened.</summary>
    private static string PlayerFingerprint(SerializablePlayer player) => string.Join("|",
        player.CurrentHp, player.MaxHp, player.Gold, player.MaxEnergy, player.MaxPotionSlotCount,
        ToJson(player.Deck), ToJson(player.Relics), ToJson(player.Potions));

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonSerializationUtility.GetTypeInfo<T>());

    private static void SetVolume(float volume)
    {
        NGame.Instance!.AudioManager.SetMasterVol(volume);
        NGame.Instance.DebugAudio.SetMasterAudioVolume(volume);
    }

    /// <summary>
    /// Follows the game's own load flow, plus the replay setup from its debug replay player. Returns a message for the
    /// player if the restored state may not be exact.
    /// </summary>
    private static async Task<string?> Restart(Checkpoint checkpoint, CombatReplay? replay, List<CombatReplayEvent>? events)
    {
        var result = JsonSerializationUtility.FromJson<SerializableRun>(checkpoint.Save);
        if (!result.Success || result.SaveData is not { } save)
        {
            throw new InvalidDataException($"Cannot read checkpoint: {result.Status}: {result.ErrorMessage}");
        }
        var state = RunState.FromSerializable(save);
        var run = RunManager.Instance;
        var game = NGame.Instance!;

        await game.Transition.FadeOut();
        NAudioManager.Instance?.StopMusic();
        run.CleanUp();
        game.RootSceneContainer.SetCurrentScene(new Control());

        // If this checkpoint is older than the save on disk, quitting now must not bring the undone actions back.
        await SaveManager.Instance.IncrementNumReloads(save, NetGameType.Singleplayer);
        var service = new UndoNetService();
        SetUpRun(state, save, service);
        game.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
        await PreloadManager.LoadRunAssets(state.Players.Select(player => player.Character));
        await PreloadManager.LoadActAssets(state.Act);
        run.Launch();
        game.RootSceneContainer.SetCurrentScene(NRun.Create(state));
        await run.GenerateMap();

        var divergence = new Divergence(replay);
        if (replay != null)
        {
            run.CombatStateSynchronizer.IsDisabled = true;
            run.ActionQueueSet.FastForwardNextActionId(replay.nextActionId);
            run.ActionQueueSynchronizer.FastForwardHookId(replay.nextHookId);
            run.ChecksumTracker.LoadReplayChecksums(replay.checksumData, replay.nextChecksumId);
            run.ChecksumTracker.ChecksumGenerated += divergence.Check;
            run.PlayerChoiceSynchronizer.FastForwardChoiceIds(replay.choiceIds);
            run.RewardsSetSynchronizer.FastForwardRewardIds(replay.rewardIds);
            service.Replaying = true;
        }
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(ReplayTimeoutSeconds);
            var entering = run.LoadIntoLatestMapCoord(AbstractRoom.FromSerializable(save.PreFinishedRoom, state));
            if (replay != null)
            {
                // Entering a combat can itself wait on recorded choices, so it can hang if the replay goes wrong.
                await WaitUntil(() => entering.IsCompleted, deadline);
                await entering;
                await Play(state, events!, checkpoint.Progress, deadline);
            }
            else
            {
                await entering;
            }
        }
        finally
        {
            service.Replaying = false;
            if (replay != null)
            {
                run.ChecksumTracker.ChecksumGenerated -= divergence.Check;
                // Without this, every later action logs an error for having no recorded checksum.
                run.ChecksumTracker.LoadReplayChecksums(null!, run.ChecksumTracker.NextId);
                run.CombatStateSynchronizer.IsDisabled = false;
            }
        }
        if (run.MapDrawingsToLoad != null)
        {
            NRun.Instance!.GlobalUi.MapScreen.Drawings.LoadDrawings(run.MapDrawingsToLoad);
            run.MapDrawingsToLoad = null;
        }
        // Turn-start notifications are suppressed during restoration; a full reload starts fresh history.
        if (replay == null && state.CurrentRoom is CombatRoom { ParentEventId: null }
            && CombatManager.Instance.DebugOnlyGetState()?.CurrentSide == CombatSide.Player
            && checkpoint.Replay is CombatReplay restartedReplay)
        {
            History.RecordTurnStart(restartedReplay.events.Count);
        }
        await game.Transition.FadeIn();
        return divergence.Found ? "Undo may not have restored this exactly" : null;
    }

    private static void SetUpRun(RunState state, SerializableRun save, UndoNetService service)
    {
        var run = RunManager.Instance;
        // SetUpSavedSingleplayer, except that it would create its own net service.
        SetState.Invoke(run, [state]);
        InitializeShared.Invoke(run, [service, new PeerInputSynchronizer(service), true, save.DailyTime,
            save.StartTime, save.RunTime, save.WinTime, save.NumReloads]);
        IEnumerable<RunLobbyPlayer> players =
            [new RunLobbyPlayer { id = service.NetId, isModded = service.LocalVersion.IsModded() }];
        InitializeRunLobby.Invoke(run, [service, state, players]);
        InitializeSavedRun.Invoke(run, [save]);
    }

    /// <summary>
    /// Feeds recorded events back in, in order, the same way as the game's debug replay player. That player can submit
    /// an end-turn action during the previous enemy turn, where it's ignored, so actions also wait for their progress.
    /// </summary>
    private static async Task Play(RunState state, List<CombatReplayEvent> events, Dictionary<int, uint> progress,
        DateTime deadline)
    {
        var run = RunManager.Instance;
        var combat = CombatManager.Instance;
        await WaitUntil(() => !run.ActionExecutor.IsPaused, deadline);
        for (var index = 0; index < events.Count; index++)
        {
            var replayEvent = events[index];
            switch (replayEvent.eventType)
            {
                case CombatReplayEventType.GameAction:
                    if (progress.TryGetValue(index, out var reached))
                    {
                        await WaitUntil(() => run.ChecksumTracker.NextId >= reached, deadline);
                    }
                    await WaitUntil(() => !combat.EndingPlayerTurnPhaseOne && !combat.EndingPlayerTurnPhaseTwo, deadline);
                    var action = replayEvent.action!.ToGameAction(state.GetPlayer(replayEvent.playerId!.Value)!);
                    if (action.ActionType == GameActionType.CombatPlayPhaseOnly)
                    {
                        await WaitUntil(() => combat.DebugOnlyGetState()!.CurrentSide != CombatSide.Enemy, deadline);
                    }
                    run.ActionQueueSet.EnqueueWithoutSynchronizing(action);
                    if (action is EndPlayerTurnAction or ReadyToBeginEnemyTurnAction)
                    {
                        var finished = run.ActionExecutor.FinishedExecutingActions();
                        await WaitUntil(() => finished.IsCompleted, deadline);
                    }
                    break;
                case CombatReplayEventType.HookAction:
                    run.ActionQueueSet.EnqueueWithoutSynchronizing(run.ActionQueueSynchronizer.GetHookActionForId(
                        replayEvent.hookId!.Value, replayEvent.playerId!.Value, replayEvent.gameActionType!.Value));
                    break;
                case CombatReplayEventType.ResumeAction:
                    run.ActionQueueSet.ResumeActionWithoutSynchronizing(replayEvent.actionId!.Value);
                    break;
                case CombatReplayEventType.PlayerChoice:
                    run.PlayerChoiceSynchronizer.ReceiveReplayChoice(state.GetPlayer(replayEvent.playerId!.Value)!,
                        replayEvent.choiceId!.Value, replayEvent.playerChoiceResult!.Value);
                    break;
                default:
                    throw new InvalidDataException($"Unknown replay event {replayEvent.eventType}.");
            }
        }
        // Every undo target was recorded with the queue idle in the play phase, so the replay must settle there too.
        await WaitUntil(() => run.ActionQueueSynchronizer.CombatState == ActionSynchronizerCombatState.PlayPhase
            && run.ActionQueueSet.IsEmpty && run.ActionExecutor.CurrentlyRunningAction == null
            && !run.ActionExecutor.IsPaused, deadline);
    }

    private static async Task WaitUntil(Func<bool> condition, DateTime deadline)
    {
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The replay stopped making progress.");
            }
            await NGame.Instance!.AwaitProcessFrame();
        }
    }

    /// <summary>Compares the replay's state checksums against the ones from when the actions were first played.</summary>
    private sealed class Divergence(CombatReplay? replay)
    {
        private readonly Dictionary<uint, uint> _recorded = replay?.checksumData.ToDictionary(
            data => data.checksumData.id, data => data.checksumData.checksum) ?? [];

        public bool Found { get; private set; }

        public void Check(NetChecksumData checksum, string context, NetFullCombatState _)
        {
            if (_recorded.TryGetValue(checksum.id, out var recorded) && recorded != checksum.checksum)
            {
                Found = true;
                Log.Error($"[Undo] Replay diverged at checksum {checksum.id} ({context}).");
            }
        }
    }
}
