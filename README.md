# CRIX SMP website

The website for CRIX SMP, a Minecraft Bedrock survival server. It's plain HTML, CSS and JavaScript with no build step, so Vercel serves it straight from this repo and every commit goes live by itself, usually within a minute.

## Add your pictures

Open the [`images`](images) folder, then **Add file → Upload files**. The file names it expects (`logo.png`, `hero.png`, `spawn.png`, `crate-gold.png`, `gallery-1.png` …) are listed in [images/README.md](images/README.md), which GitHub shows under the folder.

To see which spot wants which picture, open the site with `#images` on the end: https://crixsmp.vercel.app/#images

## Change the Realm code, Discord link or server address

Open [`config.js`](config.js), click the pencil icon, change the value between the quotes, then **Commit changes**. The Realm code updates everywhere on the page at once, including the copy buttons and the "Open the invite in Minecraft" link.

To show your own server's address with a copy button and a live player count, fill in `serverIp` (and `serverPort` if it isn't 19132).

## Change the words

All the text is in [`index.html`](index.html). Search for the sentence you want to change, edit it, and commit.

## Vercel

[`vercel.json`](vercel.json) tells Vercel this is a plain static site: Framework "Other", no install and no build step, serving the files from the top of the repo. Those settings override whatever the project had before, so an old framework setting can't break the deploy.

- **Vercel project already linked to this repo:** nothing to do. Push or commit and it deploys.
- **New Vercel project:** on vercel.com choose **Add New → Project**, import this repo and press **Deploy**.
- **Different address:** if the site isn't at crixsmp.vercel.app, change the two `crixsmp.vercel.app` links near the top of `index.html` (`og:url` and `og:image`) so link previews on Discord use the right picture.

## Files

| File | What it is |
|---|---|
| `index.html` | The page and all its text |
| `style.css` | Colours and layout |
| `site.js` | Copy buttons, the hotbar, picture loading, the hero backdrop |
| `config.js` | Realm code, Discord link, server address |
| `images/` | Your pictures |
| `vercel.json` | Vercel settings (no build step) |

NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.
