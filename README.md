# Undo

A Slay the Spire 2 mod that adds an **Undo** button to the top-right of the screen, below the top bar. It rewinds a singleplayer run to before your last action. Press it repeatedly to go further back.

The **Restart Turn** button beneath Undo rewinds to the start of your current playable turn, preserving earlier turns. It is enabled while your turn is idle.

The **Restart Combat** button underneath resets the current fight to its room-entry checkpoint without replaying any actions. Both restart buttons are disabled outside combat and for event-triggered fights.

## What it undoes

| Where you are | What Undo does |
| --- | --- |
| In combat | Undoes your last card, potion, or end of turn, restoring the exact state, including RNG, piles, and enemy intents. |
| On the rewards screen, untouched | Goes back into the fight, before the action that won it. |
| On the rewards screen, after taking something | Resets the screen, restoring **everything** you took on it. |
| In a shop, rest site, or event, after changing something | Restarts the room. |
| In an untouched room | Goes back to the previous room. |

Precise undo only exists in combat. Outside of combat, the game has no record of individual choices, so Undo goes back to the last point it saved.

Other limits:

- Actions queued while another is still resolving undo together with it.
- A combat started from an event restarts from before the event.
- Once a run has ended, it can't be undone, because the game has already written its history and deleted the save.
- History holds the last 32 checkpoints and is kept in memory, so it's lost when you close the game.
- Account progression, such as discoveries and statistics, isn't rolled back. Each undo counts as a reload in the run's stats, and the run timer goes back with the restored save.
- Singleplayer only. The button is hidden in multiplayer.

## How it works

The game can't save a fight in progress, but its combat is deterministic, because lockstep multiplayer depends on that. So an undo:

1. Rebuilds the run from the save made on entering the room, and writes that save back to disk so that quitting can't restore the undone actions.
2. Feeds the game's own combat replay recording back in, minus the last action, behind a cover screen at high speed with the sound muted.
3. Compares the game's state checksums against those from the original play. A mismatch shows a warning and logs `[Undo] Replay diverged`.

If a replay fails or stalls, the room restarts from its save and a message is shown.

## Build and install

Requires a .NET SDK supporting `net9.0` and a local game installation.

```sh
dotnet build -c Release
./install.sh
```

Default Steam paths are supported on Windows, macOS, and WSL. Override with `-p:GameDir="/path/to/Slay the Spire 2"` or `-p:GameDataDir="/path/to/data_sts2_platform"`.

Alternatively, copy `Undo.dll` and `Undo.json` from `dist/Undo/` into the game's `mods/Undo/` directory. On macOS, `mods/` is inside `SlayTheSpire2.app/Contents/MacOS/`.

## Development checks

```sh
dotnet run --project tests/Behavior
dotnet run --project tests/Compatibility -- \
  "/path/to/data_sts2_platform" \
  "dist/Undo/Undo.dll"
```

Behavior checks cover where each undo goes, without needing the game. Compatibility checks apply all patches to the installed game's assembly and resolve the private members the mod uses.
