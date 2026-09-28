# CRIX SMP website

The website for CRIX SMP, a Minecraft Bedrock survival server. Plain HTML, CSS and JavaScript on Vercel: every commit to this repo goes live by itself, usually within a minute.

## Add pictures, fonts and sounds

Open a folder on GitHub, then **Add file → Upload files**, drop your files in and **Commit changes**. Each folder's README (GitHub shows it under the file list) says exactly what to name things. Anything you haven't added keeps the built-in version, so there's never a broken picture.

| Folder | What goes in it |
|---|---|
| [`images/`](images) | `logo`, `hero`, `banner.png`, `favicon` |
| [`images/features/`](images/features) | a picture for each feature card: `spawn`, `shop`, `auction`, `crates`, `spawners`, `duels`, `afk`, `teams` |
| [`images/crates/`](images/crates) | `common`, `gold`, `amethyst`, `prime`, `crimson` |
| [`images/armor/`](images/armor) | `crimson`, `crix` |
| [`images/ranks/`](images/ranks) | small rank icons (optional) |
| [`images/gallery/`](images/gallery) | screenshots, any names (the name becomes the caption) |
| [`fonts/`](fonts) | `heading.ttf` and `body.ttf` to use your own fonts |
| [`sounds/`](sounds) | `click`, `copy`, `hotbar`, `tab`, `open`, `hover`, `music` |

**To see where each picture goes,** open the site with `#images` on the end: https://crixsmp.vercel.app/#images. Every picture spot shows the file it wants and turns green once it's found. The **Site setup** box at the bottom shows what's been added and whether live stats are linked.

## Change the settings

[`config.js`](config.js) (pencil icon on GitHub, then **Commit changes**):

- **Realm code, Discord link:** updated everywhere on the page, including the copy buttons.
- **Server address:** fill in `serverIp` to show your server's address with a copy button and its player count.
- **Fonts:** switch to a built-in pixel font (`"pixelify"`, `"silkscreen"`, `"press-start"`) or make headings bigger or smaller.
- **Sounds:** `sounds: false` turns them off for everyone; `musicVolume` sets the music volume.

All the text is in [`index.html`](index.html): search for a sentence, edit it, commit.

## Live stats: player count and leaderboards

The site can show a live **"12 online"** badge, who's online (with ranks and staff roles), the four spawn leaderboards (balance, kills, playtime, shards) and the event running right now.

**Realms can't do this:** Minecraft doesn't let add-ons on a Realm use the internet. It works from your own dedicated server (Bedrock Dedicated Server).

### Easiest: player count only

Put your server's address in `serverIp` in `config.js`. The site asks mcstatus.io how many players are on. No names or leaderboards.

### Everything: your server sends its stats

The CRIX add-on (1.0.31 and newer) comes with an optional **CRIX Web** pack that sends the stats here every minute, and whenever someone joins or leaves.

**1. Add storage on Vercel** (free):
Vercel → your project → **Storage** → **Create Database** → **Upstash (Redis)** → pick the free plan → **Connect** it to this project.

**2. Add the key on Vercel:**
Vercel → your project → **Settings** → **Environment Variables** → add:

- Name: `CRIX_WEB_KEY`
- Value: the `key` from `behavior_packs/CRIX_Web/scripts/config.js` in the add-on download

Keep the key private: anyone who has it could send fake leaderboards.

**3. Redeploy:** Vercel → **Deployments** → **⋯** on the newest one → **Redeploy**. New settings only apply to new deploys.

**4. Check it:** open https://crixsmp.vercel.app/api/stats. It should say `"storage":true,"key":true` and `"reason":"waiting"`: the website is ready and waiting for your server.

**5. On your server** (full steps in the add-on's README, section "Website link"):

- upload the `CRIX_Web` behavior pack and add it to the world,
- turn on the **Beta APIs** experiment for the world,
- add `"@minecraft/server-net"` to `config/default/permissions.json`,
- restart the server.

The server console then shows `[CRIX-WEB] website updated`, and in game **/crixadmin → Website link** shows when it last updated. The Live section appears on the site within a minute.

## How it's built

- [`vercel.json`](vercel.json) tells Vercel this is a plain static site: no install step, and one small build step (`node build.mjs`).
- [`build.mjs`](build.mjs) runs on every deploy and writes `assets.json`, a list of the pictures, sounds and fonts in the folders. That's how the site knows what you've added.
- [`api/stats.mjs`](api/stats.mjs) is the live stats address (`/api/stats`): your server sends stats to it, and the website reads them.
- If the site ever moves away from crixsmp.vercel.app, change the two `crixsmp.vercel.app` links near the top of `index.html` (they're for link previews on Discord).

| File | What it is |
|---|---|
| `index.html` | The page and all its text |
| `style.css` | Colours and layout |
| `site.js` | Pictures, sounds, fonts, live stats, the moving hero |
| `config.js` | Your settings |
| `404.html` | The "page not found" page |
| `api/stats.mjs` | Live stats |
| `build.mjs`, `vercel.json` | Vercel setup |

NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.
