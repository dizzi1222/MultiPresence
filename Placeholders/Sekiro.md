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

# Pre-made icons for Large-/Smallasset
**Do not use them with {}**

logo = Game logo

# Discord application id
Sekiro has no official Discord application, so this build needs one of your own:

1. Go to https://discord.com/developers/applications and click *Create Application*.
2. Name it, then open *Rich Presence → Artwork* and upload a 512x512 image named `logo`.
3. Copy the *Application ID* from *General Information*.
4. Paste it into the `"AppId"` field of `Assets/config/Sekiro.json` (inside the `MultiPresence`
   folder, next to `MultiPresence.exe`).

The file is reloaded automatically while MultiPresence runs, so you do not have to recompile.
`"Details"` and `"State"` above are only the defaults; edit them freely, the placeholders are
replaced every few seconds.