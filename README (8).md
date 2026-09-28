# Sounds

The site has small built-in 8-bit sounds for its buttons. Upload your own here to replace them: name each file exactly as below. `.mp3`, `.ogg`, `.wav` and `.m4a` all work.

| File name | When it plays | Built-in sound if missing |
|---|---|---|
| `click.mp3` | Buttons and menu links | a short blip |
| `copy.mp3` | Copying the Realm code | a two-note chime |
| `hotbar.mp3` | Switching slots in the hotbar at the top | a tick |
| `tab.mp3` | Switching leaderboards | a quick up-blip |
| `open.mp3` | Opening a question in the FAQ, or a screenshot | a pop |
| `hover.mp3` | Pointing at cards and buttons (mouse only) | none |
| `music.mp3` | Background music, looped. A music button appears in the top bar, and it only plays when a visitor presses it. | no music |

Keep clicks short (under half a second) and small (under 50 KB). Music volume is `musicVolume` in [`config.js`](../config.js).

Visitors can turn sounds off with the speaker button in the top bar. To turn them off for everyone, set `sounds: false` in `config.js`.

Only use sounds and music you made or are allowed to use: sounds taken from Minecraft belong to Mojang.

Upload with **Add file → Upload files**, then **Commit changes**.
