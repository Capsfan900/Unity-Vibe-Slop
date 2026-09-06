# The radio

Drop **mp3 / ogg / wav** files into the folder named after a SCENE and they become that level's playlist,
played in name order (prefix `01_`, `02_` ... to order them):

    Assets/Resources/Audio/Radio/Level_01/      <- the campaign level
    Assets/Resources/Audio/Radio/Sandbox/       <- the sandbox
    Assets/Resources/Audio/Radio/Default/       <- any scene with no folder of its own

Nothing in a folder (and nothing in `Default/`) means the radio stays off, its pane hides, and the ambient
bed plays as before. The folder is keyed to the SCENE because that is the only level identity a build has:
`LevelRegistry` is an editor asset under `Assets/Data` and nothing loads it at runtime.

Keys: `]` next, `[` previous (restarts the track after 3 s), backslash = radio on/off. See `LevelRadio.cs`.
Unity imports mp3 as an AudioClip automatically; set Load Type to *Streaming* on long tracks.
