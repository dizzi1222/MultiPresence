# Placeholders
|Placeholder|Function|
|--|--|
|{state}|`In Game` once bbport has committed the guest image, `At Launcher` otherwise|

# Current state
Bloodborne support here only covers **detection**. There are no health, area or boss values
yet, because no public offset table for Bloodborne exists.

The game runs through bbport, a shadPS4 derived host:

- `Bloodborne.exe` is the launcher. It reserves the guest address space but never commits
  it, so reading that process finds nothing.
- `bb-probe.exe` is the emulator host and the one that holds the game.
- bbport maps the PlayStation 4 address space by adding `0x1_0000_0000` to every guest
  offset, so a guest address `X` is read at `X + 0x1_0000_0000`.

Once health is located the placeholders below become available. They are declared in
`BB.cs` so the config can already reference them:

|Placeholder|Function|
|--|--|
|{hp}|Shows your current vitality|
|{maxhp}|Shows your maximum vitality|

# Discord application id
Bloodborne has no official Discord application:

1. Go to https://discord.com/developers/applications and click *Create Application*.
2. Name it, then open *Rich Presence → Artwork* and upload a 512x512 image named `logo`.
3. Copy the *Application ID* from *General Information*.
4. Paste it into the `"AppId"` field of `Assets/config/Bloodborne.json`.

The file is reloaded automatically, so no rebuild is needed.