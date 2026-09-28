// CRIX SMP website settings.
// On GitHub: click the pencil icon, change what's after the colons, then "Commit changes".
// Vercel puts the change live about a minute later.
window.CRIX_CONFIG = {
  // Realm invite code (Minecraft > Play > Realms > Join Realm). Capitals matter.
  realmCode: "WrxFsoWk5fc",

  // Discord invite link
  discord: "https://discord.gg/VmW6mXzU7z",

  // Your own server's address, to show it with a copy button and its player count.
  // Leave serverIp as "" to hide it.
  serverIp: "",
  serverPort: 19132,

  // Live player count + leaderboards from your server (see README: Live stats). false = never show them.
  liveStats: true,

  // Button sounds (visitors can still mute them with the speaker button). false = no sounds at all.
  sounds: true,
  // Background music volume, 0 to 1 (only if you add sounds/music.mp3)
  musicVolume: 0.35,

  fonts: {
    // Heading font. "auto" = your fonts/heading.ttf (or .otf/.woff/.woff2) if you add one, otherwise Jersey 10.
    // Built in: "jersey", "pixelify", "silkscreen", "press-start"
    heading: "auto",
    // Heading size: 1 = normal. If your font looks too big or small, try 0.8 or 1.2.
    headingSize: 1,
    // Text font. "auto" = your fonts/body.ttf (or .otf/.woff/.woff2) if you add one, otherwise Archivo.
    body: "auto",
  },

  // true = label every picture spot with the file name it wants
  // (same as opening the site with #images on the end of the address)
  showImageNames: false,
};
