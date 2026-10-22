# Bloodborne and Sekiro artwork

These original files were downloaded from SteamGridDB on 2026-10-22. The square
compositions fit Discord Rich Presence without stretching a vertical game cover.
The covers are 1024x1024; the complementary icons are 512x512. No image was
upscaled, cropped or re-encoded.

| Game | Asset | Contributor | Source |
| --- | --- | --- | --- |
| Bloodborne | `Bloodborne/cover.png` | RealZombro | [Grid 137883](https://www.steamgriddb.com/grid/137883) |
| Bloodborne | `Bloodborne/icon.png` | Sertall | [Icon 66152](https://www.steamgriddb.com/icon/66152) |
| Sekiro: Shadows Die Twice | `Sekiro/cover.jpg` | Comcord | [Grid 164683](https://www.steamgriddb.com/grid/164683) |
| Sekiro: Shadows Die Twice | `Sekiro/icon.png` | vruhed | [Icon 33748](https://www.steamgriddb.com/icon/33748) |

`sources.json` records the original URLs, dimensions, contributors and SHA-256
hashes. These are third-party game images; their inclusion does not make them
part of MultiPresence's code license. Credit remains with the respective game
rights holders and the contributors linked above.

The default JSON templates use the full-resolution HTTPS URLs directly in
`LargeAsset` and `SmallAsset`. The menu, launcher and in-game presences all read
those fields. Discord fetches the images remotely, so their availability depends
on the SteamGridDB CDN. No SteamGridDB API key or artwork upload is needed to use
these defaults.

Builds include this folder as `Assets/Artwork`. These local originals are for
inspection, backup and optional upload to your own Discord application's Rich
Presence artwork. Discord cannot display a local filesystem path as an asset.
If you upload them yourself, set `AppId` to your application's ID and replace the
URLs with the uploaded artwork keys. Changing `AppId` requires starting a new
presence session; image and text fields reload while MultiPresence is running.
Set `SmallAsset` and `SmallAssetText` to empty strings to hide the corner icon.
