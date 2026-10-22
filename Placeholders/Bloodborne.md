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

# Artwork and Discord application id
The shipped configuration keeps its existing Discord application ID and uses a
1024x1024 SteamGridDB cover plus a 512x512 icon through direct HTTPS URLs.
The same artwork is used at the launcher and in game. No artwork upload or API
key is required. See [artwork sources and local originals](../MultiPresence/Artwork/README.md).

Edit `LargeAsset`, `SmallAsset` and their text fields in
`Assets/config/Bloodborne.json`. Image and text changes reload automatically.
Leave `SmallAsset` empty to hide the corner icon. Uploaded Discord artwork keys
are also supported; do not wrap keys or URLs in `{}`.

If you use your own Discord application, change `AppId` and start a new presence
session so the client reconnects with that ID. No rebuild is needed.
