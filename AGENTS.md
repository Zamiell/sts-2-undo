# Agent instructions

## Testing in the game

All audio must be disabled whenever you open the game for testing. `--headless` is not enough on its own, because it only silences Godot's driver and the game plays most of its sound through FMOD. Before every launch:

- Set `volume_master`, `volume_bgm`, `volume_sfx`, and `volume_ambience` to `0` in the `settings.save` of the profile being used.
- Pass `--audio-driver Dummy` on the command line.
- In any test harness, skip `NAudioManager`'s playback methods (`PlayOneShot`, `PlayLoop`, `PlayMusic`) with a Harmony prefix.
