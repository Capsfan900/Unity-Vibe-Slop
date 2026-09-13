# Parry Choreography

Use a real traversal run to describe when projectile parries should land, then let Level Studio place one
editable best-fit module. The recorder observes the player; it never changes movement, combat, or time scale.

## Record a run

1. Run **VibeGame1 → 10. Build Parry Recording Stages**, then open the flat, downhill, ice/water, or mixed
   `LevelDefinition` template from `Assets/Data/Levels/Recording/`.
2. Enter play mode, open the Backquote console, and enter the private developer passphrase.
3. Enter `timing prime`. The `0` key is inert until this succeeds.
4. Press `0` to start. Run the route and tap Parry at each intended empty-air contact beat.
5. Press `0` again. The take stops, atomically exports one JSON file, and returns to Primed.
6. Find the JSON in the dashboard's allowlisted **Open timing captures** action, or at the displayed local path.

## Generate and edit a module

1. Open **VibeGame1 → Level Studio**, create or resume a working copy, and select **Select Capture**.
2. Confirm the path and beat count, then choose **Generate Module**.
3. Read every beat row. The report retains time error, spatial error, visibility, view angle, confidence, selected
   enemy, and an explicit failure for any unsatisfied beat; it never silently drops a requested parry.
4. Adjust generated placements with the normal SceneView handles and numeric inspector. Undo remains available.
5. To change only part of the rhythm, make a short capture containing those selected beats and generate that
   module; existing manual objects remain untouched.
6. Use F10 to play the draft, return to Level Studio, validate, review the diff, and apply only when the copy earns it.

Generation is deterministic for the same capture, stage, settings, and shipped enemy data. Automated tests prove
that repeatability and structure; only a human run can decide whether the choreography feels fair and satisfying.
