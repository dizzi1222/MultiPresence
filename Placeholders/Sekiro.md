# Placeholders
|Placeholder|Function|
|--|--|
|{hp}|Shows your current vitality (HP)|
|{maxhp}|Shows your maximum vitality|
|{posture}|Shows your current posture (the tsuba meter)|
|{maxposture}|Shows your maximum posture|
|{level}|Shows your current level|
|{xp}|Shows your experience points|
|{sen}|Shows your amount of sen|
|{attackpower}|Shows your attack power|
|{deaths}|Shows your death counter|
|{clearcount}|Shows your NG+ counter|

# Artwork and Discord application id
The shipped configuration keeps its existing Discord application ID and uses a
1024x1024 SteamGridDB cover plus a 512x512 icon through direct HTTPS URLs.
The same artwork is used in the main menu and in game. No artwork upload or API
key is required. See [artwork sources and local originals](../MultiPresence/Artwork/README.md).

Edit `LargeAsset`, `SmallAsset` and their text fields in `Assets/config/Sekiro.json`
(next to `MultiPresence.exe`). Image and text changes reload automatically.
Leave `SmallAsset` empty to hide the corner icon. Uploaded Discord artwork keys
are also supported; do not wrap keys or URLs in `{}`.

If you use your own Discord application, change `AppId` and start a new presence
session so the client reconnects with that ID. No rebuild is needed.
`"Details"` and `"State"` above are only the defaults; edit them freely, the placeholders are
replaced every few seconds.
