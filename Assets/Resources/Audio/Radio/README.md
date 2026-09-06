# The radio

Drop **mp3 / ogg / wav** files into a folder here and they become a level's playlist, played in name order
(prefix `01_`, `02_` ... to order them). Folder = the level's `levelId` (`level_01`), or whatever
`LevelDefinition.radioFolder` says. `Default/` plays for a level with no folder of its own. Nothing here =
the radio stays off and the ambient bed plays.

Keys: `]` next, `[` previous (restarts the track after 3 s), backslash = radio on/off. See `LevelRadio.cs`.
Unity imports mp3 as AudioClip automatically; set Load Type to *Streaming* on long tracks.
