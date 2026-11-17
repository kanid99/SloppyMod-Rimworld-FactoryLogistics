# Handoff: pneumatic tubes into Factory Logistics

This branch is a complete, separate mod (SloppyMods Pneumatic Tubes, build 0.9.1),
built in another session and pushed here so it can be merged into Factory Logistics.
It shares no history with `main`.

## State

- Compiles against RimWorld 1.6 (net472) and against the 1.7 reference assemblies
  with VEF's 1.7 PipeSystem.dll. Defs validated field-by-field against decompiled
  1.6 source; every texture, def reference and translation key resolves.
- **Never loaded in game.** First thing to do after merging is a load test.
- The release script's mcs compile was never run.

## What to decide when merging

The tree is laid out as its own mod root, so several files collide with any other
mod: `About/`, `LoadFolders.xml`, `CLAUDE.md`, `README.md`, `.github/`, `LICENSE`,
`.gitignore`. Pick one of:

1. **Fold in as a feature.** Move `Source/SloppyTubes/*.cs` into the factory mod's
   project (rename the namespace), and `Defs/`, `Textures/`, `Languages/` content
   alongside its own. Drop this branch's About/LoadFolders/release files. Keep the
   `SloppyTubes_` def prefix or rename it before anyone has a save with it.
2. **Keep it a separate mod** that Factory Logistics depends on or integrates with.

Either way it needs Vanilla Expanded Framework (PipeSystem) for the tubes.

## Where to look

README.md explains the design and which vanilla behaviour each part leans on.
CLAUDE.md lists the three places most likely to break on an update. No Harmony.
