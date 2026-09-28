# Fonts

## Use your own font

Upload a font file here with one of these names and the site uses it automatically:

| File name | Used for |
|---|---|
| `heading.ttf` | Titles: "CRIX SMP", section headings, big numbers |
| `body.ttf` | All the normal text |

`.ttf`, `.otf`, `.woff` and `.woff2` all work (`heading.otf` is fine). Upload with **Add file → Upload files**, then **Commit changes**.

If your heading font looks too big or too small, change `headingSize` in [`config.js`](../config.js), for example to `0.8` or `1.2`.

Only use fonts you're allowed to put on a website. Free fonts from Google Fonts (fonts.google.com) are all fine.

## Or switch to a built-in pixel font

In [`config.js`](../config.js), set `heading` to one of these:

| Setting | Font |
|---|---|
| `"jersey"` | Jersey 10: tall pixel letters (the default) |
| `"pixelify"` | Pixelify Sans: rounder pixel letters |
| `"silkscreen"` | Silkscreen: small, square, all caps |
| `"press-start"` | Press Start 2P: classic arcade |

## The `default` folder

The built-in fonts live in [`default/`](default): Jersey 10, Archivo, JetBrains Mono, Pixelify Sans, Silkscreen and Press Start 2P. They're free under the SIL Open Font License, and their licences are in [`default/licenses/`](default/licenses). Leave that folder as it is.
