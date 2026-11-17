# Working on this repo

## The default branch is the release

The owner installs and updates the SloppyMods mods straight from GitHub: RimSort
clones this repository's default branch (`main`) into RimWorld's Mods
folder and pulls it in place. There is no separate release step, so **every
commit pushed to `main` is what the game loads next**.

- Push finished work to `main`. Nothing half-done: each commit must load
  and play as-is.
- **Snapshot every build on its own branch.** Every commit carries a build
  number (`0.9.<commit count>`, stamped below). Push the same commit to
  `main` AND to a branch named after that build, so any build can be
  restored:

      git push origin HEAD:main HEAD:refs/heads/build/0.9.N

  Build branches are restore points: never move or delete one. To roll back,
  reset `main` to an earlier `build/...` branch - only when the owner asks.
  The release workflow only runs on `main`, so build branches publish nothing.
- **Every update carries its change notes** as BBCode (the Steam Workshop
  change-notes format), in `Changelog/<build>.bbcode`, committed in the same
  commit as the change. Write them for players: what was added, changed and
  fixed, not how. Format:

      [h2]Build 0.9.N[/h2]
      [h3]Added[/h3]
      [list]
      [*]...
      [/list]

  Use only the sections that apply (Added, Changed, Fixed, Removed).
- **Commit the compiled assembly.** RimWorld loads the DLL, not the source,
  and a clone gets only what is in git. This mod needs
  `Assemblies/FactoryLogistics.dll`; `.gitignore` un-ignores exactly that file.
  Rebuild and commit it in the same commit as any C# change; a stale DLL is
  what players get.
- **Stamp the build number before every commit**: `<modVersion>` in
  `About/About.xml`, and the `Build x.y` line at the top of its description,
  set to `0.9.<commit count after this commit>`. Never leave a placeholder.
- Keep `packageId` (`sloppymod.factorylogistics`) unchanged: saves and RimSort
  key on it. The display `<name>` is "SloppyMods Factory Logistics".
- Def names carry the `FL_` prefix; translation keys and the C# namespace are
  `FactoryLogistics`.
- Everything in the repository root lands in the Mods folder, so keep
  anything RimWorld might try to load (Defs, Patches, Textures, LoadFolders)
  deliberate; `Source/`, docs and images are ignored by the game.
- **Every push to the default branch publishes a GitHub Release**,
  `v<build>`, with the mod zip attached (`.github/workflows/release.yml`,
  which runs `.github/release.sh`). The script compiles the assembly from
  source with mcs against Krafs' reference assemblies and Vanilla Expanded
  Framework's PipeSystem.dll at a pinned commit. mcs is stricter than Roslyn
  in places (e.g. two `out var` of the same name in one method), so keep the
  source compiling under it: no pattern-matching beyond `as`, no
  expression-bodied members, `out` variables declared beforehand. Check with
  `bash .github/release.sh --no-release` (zip in `dist/`).

## Layout: one mod, modules

The repository root is the core module, which only needs Vanilla Expanded
Framework: the pneumatic tubes (`Assemblies/FactoryLogistics.dll`, from
`Source/FactoryLogistics`). Anything that needs Vanilla Furniture Expanded -
Factory lives in its own module folder under `Mods/`, with its own assembly,
and `LoadFolders.xml` loads it only when that mod is active (`IfModActive`),
so the core never requires VFE Factory. Core code must not reference a
module's types; a module may reference the core.

## Building

    dotnet build Source/FactoryLogistics/FactoryLogistics.csproj -c Release \
      -p:PipeSystemDll=<path to VEF>/1.6/Assemblies/PipeSystem.dll

The assembly targets net472 against RimWorld 1.6 and runs unchanged on 1.7.
On a machine without the game it falls back to Krafs.Rimworld.Ref from NuGet.
The source also compiles against the 1.7 reference assemblies
(Krafs.Rimworld.Ref 1.7.x, net10.0) and VEF's 1.7 PipeSystem.dll; keep it
that way, and check both when touching vanilla or framework API.

Art is drawn by `python3 Source/Art/tubes_art.py` (Pillow only), which also
verifies the tube atlas layout.

## How the mod hangs together

Read README.md first: it lists the vanilla behaviour each piece leans on.
The three places most likely to break on a RimWorld or VEF update:

- `PackContext` / `RecipeWorker_PackCapsule` depend on the order of calls in
  `Toils_Recipe.FinishRecipeAndStartStoringProduct` (products made, then
  ingredients consumed, then `Notify_IterationCompleted`, then storage).
- `Building_TubeStation.GetParentStoreSettings` re-implements
  `IStoreSettingsParent` to gate storage per thing, because
  `Building_Storage.Accepts` is not virtual and vanilla's storage search asks
  `StorageSettings.AllowedToAccept` anyway.
- The bill "take to" list only shows storage buildings that are
  `IRenameable`, and needs a non-null `building.groupingLabel`.

No Harmony patches. Keep it that way if at all possible.

## Standing preferences

- Licence is CC0 for every SloppyMods mod.
- Replies to the owner: concise summaries; attach a build zip when they will
  test in game.
- For art changes, show the owner before/after comparisons.
