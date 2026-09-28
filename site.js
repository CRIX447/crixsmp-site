// CRIX SMP website: pictures, sounds and fonts from your folders, live stats, and the moving parts.
// Settings live in config.js (edit that file, not this one).
"use strict";

const CONFIG = Object.assign(
  {
    realmCode: "WrxFsoWk5fc",
    discord: "https://discord.gg/VmW6mXzU7z",
    serverIp: "",
    serverPort: 19132,
    liveStats: true,
    sounds: true,
    musicVolume: 0.35,
    fonts: { heading: "auto", headingSize: 1, body: "auto" },
    showImageNames: false,
  },
  window.CRIX_CONFIG || {},
);

const root = document.documentElement;
const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];
const IMG_EXTS = ["png", "jpg", "jpeg", "webp", "gif"];
const SOUND_EXTS = ["mp3", "ogg", "wav", "m4a"];
const FONT_EXTS = ["woff2", "woff", "ttf", "otf"];
const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
const store = {
  get(k) { try { return localStorage.getItem(k); } catch { return null; } },
  set(k, v) { try { localStorage.setItem(k, v); } catch { /* private mode */ } },
};

// ------------------------------------------------------------------ numbers like the game shows them
function compact(n) {
  const a = Math.abs(n);
  for (const [v, u] of [[1e12, "t"], [1e9, "b"], [1e6, "m"], [1e3, "k"]]) if (a >= v) return (Math.floor((a / v) * 10) / 10).toFixed(1) + u;
  return String(Math.floor(a));
}
function dur(ms) {
  let s = Math.max(0, Math.floor(ms / 1000));
  const d = Math.floor(s / 86400); s -= d * 86400;
  const h = Math.floor(s / 3600); s -= h * 3600;
  const m = Math.floor(s / 60); s -= m * 60;
  if (d) return `${d}d ${h}h`;
  if (h) return `${h}h ${m}m`;
  if (m) return `${m}m ${s}s`;
  return `${s}s`;
}
const ago = (ms) => (ms < 60_000 ? `${Math.max(1, Math.round(ms / 1000))}s ago` : `${dur(ms).split(" ")[0]} ago`);

// ------------------------------------------------------------------ pixel icons (drawn here, 16 x 16)
const PAL = {
  k: "#05070b", w: "#eef2f8", g: "#8f99ac", d: "#3a4458", s: "#5d687d", b: "#2a5ff0", B: "#6f98ff", n: "#153694",
  r: "#d22f3b", R: "#ff6b73", m: "#7a131b", y: "#f0b44a", Y: "#ffe08a", o: "#8a5a12", p: "#8b55de", P: "#d9b8ff",
  v: "#4e2a8b", c: "#7a4a2a",
};
function pixelIcon(draw, size = 16) {
  const px = Array.from({ length: size }, () => Array(size).fill(null));
  const inside = (x, y) => x >= 0 && y >= 0 && x < size && y < size;
  const d = {
    set(x, y, c) { x = Math.round(x); y = Math.round(y); if (inside(x, y)) px[y][x] = c; },
    rect(x, y, w, h, c) { for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) d.set(x + i, y + j, c); },
    disc(cx, cy, r, c) { for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) if ((x - cx) ** 2 + (y - cy) ** 2 <= r * r) d.set(x, y, c); },
    line(x0, y0, x1, y1, c, t = 1) {
      const n = Math.max(Math.abs(x1 - x0), Math.abs(y1 - y0), 1);
      for (let i = 0; i <= n; i++) d.rect(Math.round(x0 + ((x1 - x0) * i) / n - (t - 1) / 2), Math.round(y0 + ((y1 - y0) * i) / n - (t - 1) / 2), t, t, c);
    },
    poly(pts, c) {
      for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
        let hit = false;
        const X = x + 0.5, Y = y + 0.5;
        for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
          const [xi, yi] = pts[i], [xj, yj] = pts[j];
          if (yi > Y !== yj > Y && X < ((xj - xi) * (Y - yi)) / (yj - yi) + xi) hit = !hit;
        }
        if (hit) d.set(x, y, c);
      }
    },
  };
  draw(d);
  const out = px.map((row) => row.slice());
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++)
    if (!px[y][x] && [[1, 0], [-1, 0], [0, 1], [0, -1]].some(([a, b]) => inside(x + a, y + b) && px[y + b][x + a])) out[y][x] = "k";
  const scale = 4, cv = document.createElement("canvas");
  cv.width = cv.height = size * scale;
  const g = cv.getContext("2d");
  out.forEach((row, y) => row.forEach((c, x) => { if (c) { g.fillStyle = PAL[c] || c; g.fillRect(x * scale, y * scale, scale, scale); } }));
  return cv.toDataURL();
}
const ICONS = {
  coin: (d) => { d.disc(7.5, 7.5, 6.6, "y"); d.disc(7.5, 7.5, 4.4, "o"); d.disc(7.5, 7.5, 3.3, "y"); d.rect(7, 5, 2, 6, "Y"); d.set(4, 3, "Y"); d.set(3, 4, "Y"); d.set(4, 4, "Y"); },
  gem: (d) => { d.poly([[8, 1], [14, 6], [8, 15], [2, 6]], "p"); d.poly([[8, 1], [14, 6], [2, 6]], "P"); d.line(5, 6, 8, 13, "v"); d.line(11, 6, 8, 13, "v"); d.set(6, 3, "w"); d.set(5, 4, "w"); },
  crate: (d) => { d.rect(2, 4, 12, 10, "r"); d.rect(2, 4, 12, 3, "m"); d.rect(2, 7, 12, 1, "R"); d.rect(7, 8, 2, 3, "Y"); d.rect(7, 9, 2, 1, "k"); d.rect(2, 12, 12, 2, "m"); d.set(3, 5, "R"); },
  sword: (d) => { d.line(6, 9, 13, 2, "w", 2); d.line(7, 10, 13, 4, "g"); d.line(3, 8, 7, 12, "y", 2); d.line(2, 13, 4, 11, "c", 2); },
  cage: (d) => { d.rect(2, 2, 12, 12, "d"); d.rect(3, 3, 10, 10, "n"); [5, 8, 11].forEach((x) => d.rect(x, 3, 1, 10, "g")); d.rect(3, 7, 10, 1, "g"); d.rect(6, 10, 2, 2, "R"); d.rect(9, 10, 2, 2, "R"); d.set(6, 9, "y"); d.set(10, 9, "y"); },
  clock: (d) => { d.disc(7.5, 7.5, 6.6, "y"); d.disc(7.5, 7.5, 5.1, "w"); d.rect(7, 4, 2, 4, "d"); d.rect(8, 7, 3, 2, "d"); d.set(7, 7, "r"); d.set(4, 3, "Y"); },
  flag: (d) => { d.rect(3, 1, 2, 13, "g"); d.rect(2, 13, 4, 2, "d"); d.poly([[5, 2], [14, 2], [11, 5.5], [14, 9], [5, 9]], "r"); d.rect(5, 4, 5, 2, "R"); },
  beacon: (d) => { d.rect(7, 1, 2, 5, "B"); d.rect(6, 5, 4, 3, "b"); d.set(6, 5, "B"); d.rect(4, 8, 8, 2, "s"); d.rect(2, 10, 12, 2, "d"); d.rect(1, 12, 14, 2, "s"); d.rect(1, 12, 14, 1, "g"); },
  tag: (d) => { d.poly([[2, 4], [10, 4], [14, 8], [10, 12], [2, 12]], "y"); d.rect(2, 4, 8, 1, "Y"); d.rect(4, 7, 2, 2, "k"); d.rect(7, 7, 4, 1, "o"); d.rect(7, 9, 3, 1, "o"); },
  star: (d) => { const pts = []; for (let i = 0; i < 10; i++) { const a = -Math.PI / 2 + (i * Math.PI) / 5, r = i % 2 ? 2.8 : 6.6; pts.push([7.5 + r * Math.cos(a), 8.2 + r * Math.sin(a)]); } d.poly(pts, "y"); d.set(7, 5, "Y"); d.set(6, 7, "Y"); d.set(7, 6, "Y"); },
  trophy: (d) => { d.rect(4, 2, 8, 5, "y"); d.rect(5, 7, 6, 1, "y"); d.rect(6, 8, 4, 1, "y"); d.rect(7, 9, 2, 2, "o"); d.rect(5, 11, 6, 1, "y"); d.rect(4, 12, 8, 2, "o"); d.rect(2, 3, 2, 1, "y"); d.rect(2, 4, 1, 2, "y"); d.rect(12, 3, 2, 1, "y"); d.rect(13, 4, 1, 2, "y"); d.rect(5, 3, 1, 3, "Y"); },
  helmet: (d) => { d.rect(3, 2, 10, 11, "b"); d.set(3, 2, null); d.set(12, 2, null); d.rect(4, 3, 7, 1, "B"); d.rect(4, 6, 8, 4, "n"); d.rect(5, 7, 6, 2, "R"); d.rect(3, 12, 10, 2, "d"); },
  speaker: (d) => { d.rect(2, 6, 3, 4, "w"); d.poly([[5, 6], [9, 2.5], [9, 13.5], [5, 10]], "w"); d.rect(11, 6, 1, 4, "B"); d.set(12, 4, "B"); d.rect(13, 5, 1, 6, "B"); d.set(12, 11, "B"); },
  mute: (d) => { d.rect(2, 6, 3, 4, "g"); d.poly([[5, 6], [9, 2.5], [9, 13.5], [5, 10]], "g"); d.line(11, 5, 14, 10, "R"); d.line(14, 5, 11, 10, "R"); },
  note: (d) => { d.rect(7, 2, 2, 9, "w"); d.rect(9, 2, 3, 2, "w"); d.rect(11, 4, 2, 2, "w"); d.rect(12, 6, 1, 2, "w"); d.disc(5.5, 11.5, 2.6, "w"); },
};
const iconCache = {};
const iconURL = (name) => (iconCache[name] ??= ICONS[name] ? pixelIcon(ICONS[name]) : "");
function paintIcons(scope = document) {
  for (const img of $$("img[data-icon]", scope)) {
    const url = iconURL(img.dataset.icon);
    if (url && img.src !== url) img.src = url;
  }
}
paintIcons();

// ------------------------------------------------------------------ Realm code + links
(function links() {
  const code = String(CONFIG.realmCode || "").trim();
  $$("[data-link=discord]").forEach((a) => (a.href = CONFIG.discord));
  $$("[data-discord-text]").forEach((el) => (el.textContent = CONFIG.discord.replace(/^https?:\/\//, "")));
  $$("[data-link=realm]").forEach((a) => (a.href = "https://realms.gg/" + encodeURIComponent(code)));
  $$("[data-realm]").forEach((el) => (el.textContent = code));
  const box = $("[data-code]");
  if (box) {
    box.textContent = "";
    box.setAttribute("aria-label", "Realm code " + code);
    for (const ch of code) {
      const s = document.createElement("span");
      s.textContent = ch;
      if (/[A-Z]/.test(ch)) s.className = "up";
      else if (/[0-9]/.test(ch)) s.className = "num";
      box.appendChild(s);
    }
  }
  $$("[data-year]").forEach((el) => (el.textContent = String(new Date().getFullYear())));
})();

// ------------------------------------------------------------------ sounds (built in, or yours from sounds/)
const Sound = (() => {
  let ctx = null;
  let on = CONFIG.sounds !== false && store.get("crix-sound") !== "off";
  let music = null;
  const files = {};
  function audio() {
    if (!ctx) { try { ctx = new (window.AudioContext || window.webkitAudioContext)(); } catch { ctx = null; } }
    if (ctx && ctx.state === "suspended") ctx.resume().catch(() => {});
    return ctx;
  }
  function tone(freqs, { type = "square", len = 0.05, gain = 0.045, slide = 0 } = {}) {
    const a = audio();
    if (!a) return;
    let t = a.currentTime + 0.005;
    for (const f of freqs) {
      const o = a.createOscillator(), g = a.createGain();
      o.type = type;
      o.frequency.setValueAtTime(f, t);
      if (slide) o.frequency.exponentialRampToValueAtTime(Math.max(40, f * slide), t + len);
      g.gain.setValueAtTime(gain, t);
      g.gain.exponentialRampToValueAtTime(0.0001, t + len);
      o.connect(g).connect(a.destination);
      o.start(t);
      o.stop(t + len + 0.02);
      t += len * 0.9;
    }
  }
  const SYNTH = {
    click: () => tone([520], { len: 0.05, slide: 0.7 }),
    hotbar: () => tone([880], { type: "triangle", len: 0.035, gain: 0.07 }),
    copy: () => tone([784, 1175], { len: 0.07, gain: 0.04 }),
    tab: () => tone([660, 990], { type: "triangle", len: 0.04, gain: 0.06 }),
    open: () => tone([330], { type: "sine", len: 0.08, gain: 0.08, slide: 1.8 }),
  };
  let lastHover = 0;
  function play(name) {
    if (!on) return;
    if (name === "hover") {
      if (!files.hover || Date.now() - lastHover < 90) return;
      lastHover = Date.now();
    }
    if (files[name]) {
      try { const a = new Audio(files[name]); a.volume = name === "hover" ? 0.35 : 0.6; a.play().catch(() => {}); } catch { /* ignore */ }
      return;
    }
    if (SYNTH[name]) SYNTH[name]();
  }
  function paintButton() {
    const b = $("#sound-btn");
    if (!b) return;
    b.hidden = CONFIG.sounds === false;
    b.setAttribute("aria-pressed", String(on));
    b.title = on ? "Sounds on (tap to mute)" : "Sounds off (tap to turn on)";
    const img = $("img", b);
    img.dataset.icon = on ? "speaker" : "mute";
    paintIcons(b);
  }
  function setOn(v) { on = v; store.set("crix-sound", v ? "on" : "off"); paintButton(); if (v) play("click"); }
  function useFiles(list) {
    for (const name of ["click", "copy", "hotbar", "tab", "open", "hover", "music"]) {
      const f = list && pick(list, "sounds/" + name, SOUND_EXTS);
      if (f) files[name] = encodeURI(f);
    }
    const mb = $("#music-btn");
    if (files.music && mb) {
      mb.hidden = false;
      mb.addEventListener("click", () => {
        if (!music) { music = new Audio(files.music); music.loop = true; music.volume = Math.max(0, Math.min(1, Number(CONFIG.musicVolume) || 0.35)); }
        const playing = !music.paused;
        if (playing) music.pause(); else music.play().catch(() => {});
        mb.setAttribute("aria-pressed", String(!playing));
        mb.title = playing ? "Play music" : "Pause music";
        store.set("crix-music", playing ? "off" : "on");
      });
    }
  }
  paintButton();
  $("#sound-btn")?.addEventListener("click", () => setOn(!on));
  return { play, useFiles, files, get on() { return on; } };
})();

// which sound a click makes
document.addEventListener("click", (e) => {
  const t = e.target.closest("button, a, summary, figure[data-shot]");
  if (!t || t.id === "sound-btn") return;
  if (t.matches("[data-copy], [data-copy-plain], .hotbar button, [role=tab]")) return; // they play their own
  if (t.matches("summary, figure[data-shot]")) return Sound.play("open");
  if (t.matches("button, .btn, .nav a, .live-pill")) Sound.play("click");
});
document.addEventListener("pointerover", (e) => {
  if (e.pointerType === "mouse" && e.target.closest(".feature, .crate, .btn, .hotbar button, .slots li") && !e.relatedTarget?.closest?.(".feature, .crate, .btn, .hotbar button, .slots li")) Sound.play("hover");
});

// ------------------------------------------------------------------ toast + copy buttons
let toastTimer;
function toast(text) {
  const t = $("#toast");
  if (!t) return;
  t.textContent = text;
  t.hidden = false;
  t.style.animation = "none";
  void t.offsetWidth;
  t.style.animation = "";
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => (t.hidden = true), 2200);
}
async function copyText(text, btn, selectEl) {
  try {
    await navigator.clipboard.writeText(text);
    Sound.play("copy");
    toast(`Copied ${text}`);
    const code = $("[data-code]");
    if (code && selectEl === code) { code.classList.remove("flash"); void code.offsetWidth; code.classList.add("flash"); }
    if (btn && !btn.matches(".linkish")) {
      const old = btn.dataset.label || (btn.dataset.label = btn.textContent);
      btn.textContent = "Copied!";
      clearTimeout(btn._t);
      btn._t = setTimeout(() => (btn.textContent = old), 2000);
    }
  } catch {
    if (selectEl) {
      const r = document.createRange();
      r.selectNodeContents(selectEl);
      const sel = window.getSelection();
      sel.removeAllRanges();
      sel.addRange(r);
    }
    toast("Selected: copy it now");
  }
}
$$("[data-copy], [data-copy-plain]").forEach((b) => b.addEventListener("click", () => copyText(String(CONFIG.realmCode).trim(), b, $("[data-code]"))));

// ------------------------------------------------------------------ hotbar (1-9 on the keyboard)
(function hotbar() {
  const btns = $$(".hotbar button");
  const held = $("#held");
  if (!btns.length || !held) return;
  function pick(i, sound = true) {
    btns.forEach((b, j) => b.setAttribute("aria-pressed", String(i === j)));
    held.textContent = "";
    const c = document.createElement("span");
    c.className = "held-cmd";
    c.textContent = btns[i].textContent.replace(/^\d/, "");
    held.append(c, btns[i].dataset.what);
    if (sound) Sound.play("hotbar");
  }
  btns.forEach((b, i) => b.addEventListener("click", () => pick(i)));
  document.addEventListener("keydown", (e) => {
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    const t = e.target;
    if (t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))) return;
    if (/^[1-9]$/.test(e.key)) pick(Number(e.key) - 1);
  });
})();

// ------------------------------------------------------------------ menu, active section, scroll-in
(function navigation() {
  const bar = $("#top-bar"), btn = $("#menu-btn");
  const close = () => { bar?.classList.remove("nav-open"); btn?.setAttribute("aria-expanded", "false"); };
  btn?.addEventListener("click", () => {
    const open = !bar.classList.contains("nav-open");
    bar.classList.toggle("nav-open", open);
    btn.setAttribute("aria-expanded", String(open));
  });
  $$("#nav a").forEach((a) => a.addEventListener("click", close));
  document.addEventListener("keydown", (e) => { if (e.key === "Escape") close(); });
  document.addEventListener("click", (e) => { if (bar && !bar.contains(e.target)) close(); });

  if (!("IntersectionObserver" in window)) { $$(".reveal").forEach((el) => el.classList.add("in")); return; }
  const links = new Map($$("#nav a").map((a) => [a.getAttribute("href").slice(1), a]));
  const spy = new IntersectionObserver((entries) => {
    for (const en of entries) if (en.isIntersecting) { links.forEach((a) => a.classList.remove("on")); links.get(en.target.id)?.classList.add("on"); }
  }, { rootMargin: "-45% 0px -50% 0px" });
  $$("main section[id]").forEach((s) => spy.observe(s));
  const seen = new IntersectionObserver((entries) => {
    for (const en of entries) if (en.isIntersecting) { en.target.classList.add("in"); seen.unobserve(en.target); }
  }, { rootMargin: "0px 0px -8% 0px", threshold: 0.06 });
  $$(".reveal").forEach((el, i) => { el.style.transitionDelay = `${(i % 4) * 60}ms`; seen.observe(el); });
})();

// ------------------------------------------------------------------ your files: pictures, sounds, fonts
function pick(files, base, exts) {
  const want = base.toLowerCase();
  for (const e of exts) {
    const hit = files.find((f) => f.toLowerCase() === `${want}.${e}`);
    if (hit) return hit;
  }
  return null;
}
function probe(base) {
  return new Promise((done) => {
    let i = 0;
    const next = () => {
      if (i >= IMG_EXTS.length) return done(null);
      const url = `${base}.${IMG_EXTS[i++]}`;
      const im = new Image();
      im.onload = () => done(url);
      im.onerror = next;
      im.src = url;
    };
    next();
  });
}
async function assetList() {
  try {
    const r = await fetch("assets.json", { cache: "no-cache" });
    if (r.ok) { const j = await r.json(); if (Array.isArray(j.files)) return j; }
  } catch { /* opened from your computer, or not built yet */ }
  return null;
}
function tagFor(el, base, found) {
  let t = $(".img-tag", el);
  if (!t) {
    t = document.createElement("span");
    t.className = "img-tag";
    t.setAttribute("aria-hidden", "true");
    el.appendChild(t);
  }
  t.classList.toggle("ok", !!found);
  t.textContent = found ? `✓ ${found}` : `${base}.png`;
  const sm = document.createElement("small");
  sm.textContent = found ? "found" : el.dataset.size || "";
  t.appendChild(sm);
}
const setup = { list: null, spots: 0, filled: 0, missing: [], gallery: 0, heading: "", body: "", live: null };

async function loadPictures(list) {
  const files = list ? list.files : null;
  const els = $$("[data-img]");
  const bases = [...new Set(els.map((el) => el.dataset.img))];
  const found = {};
  await Promise.all(bases.map(async (b) => (found[b] = files ? pick(files, b, IMG_EXTS) : await probe(b))));
  for (const el of els) {
    const url = found[el.dataset.img];
    if (url) {
      const img = document.createElement("img");
      img.className = "photo";
      img.alt = el.dataset.alt || "";
      img.decoding = "async";
      if (!el.closest(".hero, .top")) img.loading = "lazy";
      img.src = encodeURI(url);
      el.prepend(img);
      el.classList.add("has-img");
    }
    tagFor(el, el.dataset.img, url);
  }
  setup.spots = bases.length;
  setup.filled = bases.filter((b) => found[b]).length;
  setup.missing = bases.filter((b) => !found[b] && !/ranks\/|favicon/.test(b));
  // browser tab icon
  const fav = files ? pick(files, "images/favicon", IMG_EXTS) || found["images/logo"] : found["images/logo"];
  let link = $("link[rel~=icon]");
  if (!link) { link = document.createElement("link"); link.rel = "icon"; document.head.appendChild(link); }
  link.href = fav ? encodeURI(fav) : iconURL("crate");
}

// screenshots: every picture in images/gallery (name order); the file name becomes the caption
const shots = [];
async function loadGallery(list) {
  if (list) {
    const natural = new Intl.Collator(undefined, { numeric: true, sensitivity: "base" });
    list.files.filter((f) => /^images\/gallery\/[^/]+\.(png|jpe?g|webp|gif)$/i.test(f)).sort(natural.compare).forEach((f) => shots.push(f));
  } else {
    for (let n = 1; n <= 24; n++) { const u = await probe(`images/gallery/${n}`); if (!u) break; shots.push(u); }
  }
  setup.gallery = shots.length;
  drawGallery();
}
const caption = (f) => {
  const name = decodeURIComponent(f.split("/").pop()).replace(/\.[^.]+$/, "").replace(/[_-]+/g, " ").trim();
  return /^\d+$/.test(name) ? "" : name;
};
function drawGallery() {
  const sec = $("#gallery"), grid = $("#gallery-grid");
  if (!sec || !grid) return;
  const hints = root.classList.contains("hints");
  grid.textContent = "";
  shots.forEach((u, i) => {
    const f = document.createElement("figure");
    f.dataset.shot = String(i);
    f.tabIndex = 0;
    const img = document.createElement("img");
    img.src = encodeURI(u);
    img.alt = caption(u) || `CRIX SMP screenshot ${i + 1}`;
    img.loading = "lazy";
    f.appendChild(img);
    const cap = caption(u);
    if (cap) { const fc = document.createElement("figcaption"); fc.textContent = cap; f.appendChild(fc); }
    if (hints) { const t = document.createElement("span"); t.className = "img-tag ok"; t.textContent = "✓ " + u; f.appendChild(t); }
    f.addEventListener("click", () => openShot(i));
    f.addEventListener("keydown", (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); openShot(i); } });
    grid.appendChild(f);
  });
  if (hints && !shots.length) {
    for (const name of ["Spawn at night.png", "My base.jpg", "any name you like.png"]) {
      const f = document.createElement("figure");
      f.className = "empty";
      const t = document.createElement("span");
      t.className = "img-tag";
      t.textContent = "images/gallery/" + name;
      const sm = document.createElement("small");
      sm.textContent = "any size, any name";
      t.appendChild(sm);
      f.appendChild(t);
      grid.appendChild(f);
    }
  }
  sec.hidden = !shots.length && !hints;
}
let shotAt = 0;
function openShot(i) {
  const lb = $("#lightbox");
  if (!lb || !shots.length) return;
  shotAt = (i + shots.length) % shots.length;
  $("#lb-img").src = encodeURI(shots[shotAt]);
  $("#lb-img").alt = caption(shots[shotAt]) || `Screenshot ${shotAt + 1}`;
  $("#lb-cap").textContent = `${caption(shots[shotAt]) || "Screenshot"} · ${shotAt + 1} / ${shots.length}`;
  lb.hidden = false;
  $("#lb-close").focus();
}
(function lightbox() {
  const lb = $("#lightbox");
  if (!lb) return;
  const close = () => (lb.hidden = true);
  $("#lb-close").addEventListener("click", close);
  $("#lb-prev").addEventListener("click", () => openShot(shotAt - 1));
  $("#lb-next").addEventListener("click", () => openShot(shotAt + 1));
  lb.addEventListener("click", (e) => { if (e.target === lb) close(); });
  document.addEventListener("keydown", (e) => {
    if (lb.hidden) return;
    if (e.key === "Escape") close();
    if (e.key === "ArrowLeft") openShot(shotAt - 1);
    if (e.key === "ArrowRight") openShot(shotAt + 1);
  });
})();

// heading + body fonts: yours from fonts/, or one of the built-in ones (config.js)
const BUILT_IN_FONTS = {
  jersey: ['"Jersey 10"', 1, 400, "Jersey 10"],
  pixelify: ['"Pixelify Sans"', 0.8, 600, "Pixelify Sans"],
  silkscreen: ['"Silkscreen"', 0.72, 400, "Silkscreen"],
  "press-start": ['"Press Start 2P"', 0.5, 400, "Press Start 2P"],
};
async function loadFace(name, url) {
  try {
    const face = new FontFace(name, `url("${encodeURI(url)}")`);
    await face.load();
    document.fonts.add(face);
    return true;
  } catch {
    return false;
  }
}
async function applyFonts(list) {
  const f = CONFIG.fonts || {};
  const want = String(f.heading || "auto").toLowerCase();
  const mine = list && pick(list.files, "fonts/heading", FONT_EXTS);
  let family, scale, weight, label;
  if ((want === "auto" || want === "custom") && mine && (await loadFace("CRIX Heading", mine))) {
    [family, scale, weight, label] = ['"CRIX Heading"', 1, 400, `your font (${mine})`];
  } else {
    [family, scale, weight, label] = BUILT_IN_FONTS[want] || BUILT_IN_FONTS.jersey;
    if (want === "custom" && !mine) label += " (fonts/heading not found)";
  }
  root.style.setProperty("--display-font", `${family}, "Jersey 10", "Silkscreen", "Arial Narrow", Impact, sans-serif`);
  root.style.setProperty("--display-scale", String(scale * (Number(f.headingSize) || 1)));
  root.style.setProperty("--display-weight", String(weight));
  setup.heading = label;
  const bodyWant = String(f.body || "auto").toLowerCase();
  const bodyFile = list && pick(list.files, "fonts/body", FONT_EXTS);
  if (bodyWant !== "archivo" && bodyFile && (await loadFace("CRIX Body", bodyFile))) {
    root.style.setProperty("--body-font", '"CRIX Body", "Archivo", system-ui, sans-serif');
    setup.body = `your font (${bodyFile})`;
  } else setup.body = "Archivo (built in)";
}

// ------------------------------------------------------------------ owner mode: #images / #setup
function hintsWanted() {
  return CONFIG.showImageNames || /(^|[#?&])(images|setup|hints)\b/i.test(location.hash + location.search);
}
function setHints(on) {
  root.classList.toggle("hints", on);
  const btn = $("#hint-toggle");
  if (btn) {
    btn.setAttribute("aria-pressed", String(on));
    btn.textContent = on ? "Picture names: on" : "Picture names: off";
  }
  const guide = $("#setup");
  if (guide) guide.hidden = !on;
  drawGallery();
  if (on) drawSetup();
}
function card(title, big, lines, tone) {
  const c = document.createElement("div");
  c.className = "setup-card panel";
  const h = document.createElement("h4");
  h.textContent = title;
  const b = document.createElement("p");
  b.className = "big " + (tone || "");
  b.textContent = big;
  c.append(h, b);
  for (const l of lines) { const p = document.createElement("p"); p.textContent = l; c.appendChild(p); }
  return c;
}
function drawSetup() {
  const grid = $("#setup-grid");
  if (!grid) return;
  grid.textContent = "";
  grid.appendChild(card("Pictures", `${setup.filled} / ${setup.spots}`, [
    setup.missing.length ? `Still using built-in art: ${setup.missing.slice(0, 5).map((m) => m.replace("images/", "")).join(", ")}${setup.missing.length > 5 ? "…" : ""}` : "Every picture spot has your picture.",
    `Screenshots: ${setup.gallery}`,
  ], setup.filled ? "ok-text" : "warn-text"));
  grid.appendChild(card("Fonts", setup.heading.startsWith("your") ? "Yours" : "Built in", [`Headings: ${setup.heading}`, `Text: ${setup.body}`]));
  const found = Object.keys(Sound.files);
  grid.appendChild(card("Sounds", found.length ? `${found.length} yours` : "Built in", [found.length ? `Using: ${found.join(", ")}` : "Add sounds/click.mp3 and friends to use your own.", Sound.files.music ? "Music button is showing." : "Add sounds/music.mp3 for a music button."]));
  const L = setup.live;
  let big = "Not linked", lines = ["See README: Live stats."], tone = "warn-text";
  if (!CONFIG.liveStats) { big = "Off"; lines = ["liveStats is false in config.js"]; }
  else if (L && L.ok) { big = "Linked"; tone = "ok-text"; lines = [`Last update ${ago(Math.max(0, L.now - L.received))}`, `Pack ${L.pack || "?"}`]; }
  else if (L && L.setup) {
    lines = [`Upstash storage: ${L.setup.storage ? "connected" : "not connected"}`, `CRIX_WEB_KEY: ${L.setup.key ? "set" : "not set"}`, L.reason === "waiting" ? "Waiting for your server to send stats." : L.reason === "storage-error" ? `Storage error: ${L.error || ""}` : ""].filter(Boolean);
  } else if (L === false) lines = ["The /api/stats page didn't answer. It only works on Vercel, not when you open the file on your computer."];
  grid.appendChild(card("Live stats", big, lines, tone));
  grid.appendChild(card("File list", setup.list ? "Found" : "Missing", [setup.list ? `assets.json built ${new Date(setup.list.built).toLocaleString()}` : "assets.json is made by Vercel on every deploy. Without it the site guesses the picture names."]));
}
(function ownerMode() {
  const btn = $("#hint-toggle");
  const on = hintsWanted();
  if (on && btn) btn.hidden = false;
  btn?.addEventListener("click", () => setHints(!root.classList.contains("hints")));
  setHints(on);
  window.addEventListener("hashchange", () => { if (hintsWanted()) { if (btn) btn.hidden = false; setHints(true); } });
})();

// ------------------------------------------------------------------ live stats (see README: Live stats)
const RANK_NAMES = { member: ["Member", "#aab2c0"], vip: ["VIP", "#6f98ff"], vipplus: ["VIP+", "#6f98ff"], mvp: ["MVP", "#ff6b73"], mvpplus: ["MVP+", "#ff6b73"], elite: ["ELITE", "#e2414b"], crixplus: ["CRIX+", "#6f98ff"] };
const ROLE_NAMES = { owner: ["OWNER", "#ff5a5a"], developer: ["DEV", "#5fe3ff"], admin: ["ADMIN", "#ff6b73"], mod: ["MOD", "#d08cff"], helper: ["HELPER", "#ffe36a"], builder: ["BUILDER", "#43c77a"], media: ["MEDIA", "#ff8ad8"] };
const BOARD_FORMAT = { balance: (n) => "$" + compact(n), kills: compact, playtime: (m) => dur(m * 60_000), shards: compact };
let live = null;
let board = "balance";

function showLiveChrome(count, fresh) {
  const txt = fresh ? `${count} online` : "Server offline";
  for (const el of [$("#live-pill"), $("#hero-live")]) {
    if (!el) continue;
    el.hidden = false;
    el.classList.toggle("off", !fresh);
    $("span", el).textContent = txt;
  }
  $$("[data-live-link]").forEach((a) => (a.hidden = false));
  $("#live").hidden = false;
}
function drawBoard() {
  const ol = $("#board");
  if (!ol || !live) return;
  ol.textContent = "";
  const rows = (live.boards && live.boards[board]) || [];
  if (!rows.length) {
    const li = document.createElement("li");
    li.className = "empty";
    li.textContent = "No players on this board yet.";
    ol.appendChild(li);
    return;
  }
  rows.forEach(([name, value], i) => {
    const li = document.createElement("li");
    const pos = document.createElement("span"); pos.className = "pos"; pos.textContent = `#${i + 1}`;
    const nm = document.createElement("span"); nm.className = "name"; nm.textContent = name;
    const val = document.createElement("span"); val.className = "val"; val.textContent = (BOARD_FORMAT[board] || compact)(value);
    li.append(pos, nm, val);
    ol.appendChild(li);
  });
}
function renderLive(d) {
  live = d;
  const age = Math.max(0, (d.now || Date.now()) - (d.received || 0));
  const fresh = age < 10 * 60_000;
  const online = fresh ? d.online || [] : [];
  const count = fresh ? d.count ?? online.length : 0;
  showLiveChrome(count, fresh);
  $("#boards").hidden = false;
  $("#status-card").classList.toggle("off", !fresh);
  $("#live-count").textContent = String(count);
  $("#live-count-label").textContent = count === 1 ? "player online" : "players online";
  const line = $("#live-line");
  line.textContent = "";
  const b = document.createElement("b");
  b.textContent = fresh ? "● Online" : "● Offline";
  line.append(b, fresh ? ` · ${compact(d.total || 0)} players have joined` : ` · last heard from ${ago(age)}`);
  const ev = $("#live-event");
  if (fresh && d.event && d.event.until > (d.now || Date.now())) {
    ev.hidden = false;
    $("#event-name").textContent = d.event.name;
    $("#event-status").textContent = `${d.event.status} · ${dur(d.event.until - (d.now || Date.now())).split(" ")[0]} left`;
  } else ev.hidden = true;
  const ul = $("#live-online");
  ul.textContent = "";
  for (const p of online.slice(0, 40)) {
    const li = document.createElement("li");
    if (p.s && ROLE_NAMES[p.s]) { const r = document.createElement("span"); r.className = "role"; r.style.color = ROLE_NAMES[p.s][1]; r.textContent = ROLE_NAMES[p.s][0]; li.appendChild(r); }
    const rank = RANK_NAMES[p.r];
    if (rank && p.r !== "member") { const r = document.createElement("span"); r.className = "rk"; r.style.color = rank[1]; r.textContent = rank[0]; li.appendChild(r); }
    li.append(p.n);
    ul.appendChild(li);
  }
  if (online.length > 40) { const li = document.createElement("li"); li.className = "more"; li.textContent = `+${online.length - 40} more`; ul.appendChild(li); }
  ul.hidden = !online.length;
  $("#live-updated").textContent = `Updated ${ago(age)}`;
  drawBoard();
}
function renderPing(s) {
  // only a player count (your server's address in config.js, through mcstatus.io)
  live = null;
  const count = s.players?.online ?? 0;
  showLiveChrome(count, !!s.online);
  $("#boards").hidden = true;
  $("#status-card").classList.toggle("off", !s.online);
  $("#live-count").textContent = String(count);
  $("#live-count-label").textContent = `of ${s.players?.max ?? "?"} players online`;
  $("#live-line").textContent = s.online ? `${CONFIG.serverIp}:${CONFIG.serverPort}` : "The server isn't answering right now.";
  $("#live-online").hidden = true;
  $("#live-event").hidden = true;
  $("#live-updated").textContent = "Leaderboards appear here once the server is linked (README: Live stats).";
}
async function pingServer() {
  const ip = String(CONFIG.serverIp || "").trim();
  if (!ip) return null;
  try {
    const r = await fetch(`https://api.mcstatus.io/v2/status/bedrock/${encodeURIComponent(`${ip}:${CONFIG.serverPort}`)}`);
    return r.ok ? await r.json() : null;
  } catch {
    return null;
  }
}
async function refreshLive() {
  if (!CONFIG.liveStats) return;
  let d = null;
  try {
    const r = await fetch("api/stats", { cache: "no-store" });
    if (r.ok && (r.headers.get("content-type") || "").includes("json")) d = await r.json();
  } catch { /* no API (opened from your computer) */ }
  setup.live = d || false;
  if (d && d.ok) renderLive(d);
  else {
    const s = await pingServer();
    if (s) renderPing(s);
    const st = $("#status");
    if (s && st) {
      st.hidden = false;
      st.classList.toggle("on", !!s.online);
      st.classList.toggle("off", !s.online);
      $("span", st).textContent = s.online ? `Online · ${s.players?.online ?? 0} / ${s.players?.max ?? "?"} players` : "Offline right now";
    }
  }
  if (root.classList.contains("hints")) drawSetup();
}
$$(".tabs [role=tab]").forEach((t) =>
  t.addEventListener("click", () => {
    board = t.dataset.board;
    $$(".tabs [role=tab]").forEach((x) => x.setAttribute("aria-selected", String(x === t)));
    Sound.play("tab");
    drawBoard();
  }),
);
setInterval(() => { if (!document.hidden) refreshLive(); }, 30_000);
document.addEventListener("visibilitychange", () => { if (!document.hidden && live) refreshLive(); });

// your own server's address (config.js), with a copy button
(function server() {
  const ip = String(CONFIG.serverIp || "").trim();
  if (!ip) return;
  $("#server").hidden = false;
  $("#server-ip").textContent = ip;
  $("#server-port").textContent = "Port " + CONFIG.serverPort;
  $("#copy-ip").addEventListener("click", (e) => copyText(ip, e.currentTarget, $("#server-ip")));
})();

// ------------------------------------------------------------------ hero backdrop: pixel hills, drifting clouds, twinkling stars
(function terrain() {
  const c = $("#terrain");
  if (!c) return;
  const g = c.getContext("2d");
  let W = 0, H = 0, B = 16, layers = [], sky = null, stars = [], clouds = [], running = false, visible = true, mouse = 0, raf = 0, last = 0;
  const hill = (x, f, ph) => (Math.sin(x * f + ph) + 0.5 * Math.sin(x * f * 2.3 + ph * 1.7) + 0.25 * Math.sin(x * f * 5.1 + ph * 0.3)) / 1.75;
  const LAYERS = [
    { base: 0.62, amp: 0.1, f: 0.006, ph: 1.2, top: "#1b2c5c", fill: "#111d3f", par: 0.25 },
    { base: 0.73, amp: 0.08, f: 0.009, ph: 4.1, top: "#3a1a33", fill: "#1a1027", par: 0.5 },
    { base: 0.85, amp: 0.05, f: 0.014, ph: 2.6, top: "#1a2338", fill: "#0b1019", par: 0.85 },
  ];
  function off(w, h) { const o = document.createElement("canvas"); o.width = w; o.height = h; return o; }
  function build() {
    const r = c.getBoundingClientRect();
    if (!r.width || !r.height) return false;
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    W = r.width; H = r.height; B = W < 640 ? 12 : 16;
    c.width = Math.round(W * dpr); c.height = Math.round(H * dpr);
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.imageSmoothingEnabled = false;
    // sky + moon
    sky = off(W, H);
    const s = sky.getContext("2d");
    const grad = s.createLinearGradient(0, 0, 0, H);
    grad.addColorStop(0, "#060913"); grad.addColorStop(0.5, "#0c1633"); grad.addColorStop(0.74, "#2a1230"); grad.addColorStop(0.86, "#4a1624"); grad.addColorStop(1, "#070a10");
    s.fillStyle = grad; s.fillRect(0, 0, W, H);
    if (W >= 720) {
      const m = Math.round((Math.min(W, H) * 0.075) / B) * B || B * 3;
      const mx = Math.round((W * 0.8) / B) * B, my = Math.round((H * 0.14) / B) * B;
      s.fillStyle = "rgba(214,226,255,.07)"; s.fillRect(mx - B, my - B, m + 2 * B, m + 2 * B);
      s.fillStyle = "#cdd9f6"; s.fillRect(mx, my, m, m);
      s.fillStyle = "#aebde4"; s.fillRect(mx + B, my + B, B, B); s.fillRect(mx + m - 2 * B, my + m - 2 * B, B, B);
    }
    let seed = 20240611;
    const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
    stars = [];
    for (let i = 0; i < Math.round((W * H) / 6500); i++) stars.push({ x: Math.floor((rnd() * W) / 4) * 4, y: Math.floor((rnd() * H * 0.55) / 4) * 4, a: 0.2 + rnd() * 0.6, red: rnd() < 0.12, tw: rnd() < 0.25, ph: rnd() * 6.28 });
    // hills, each on its own layer (a little taller than the screen, for scrolling)
    layers = LAYERS.map((L, li) => {
      const o = off(Math.ceil(W + 64), Math.ceil(H + 80));
      const x2 = o.getContext("2d");
      for (let x = 0; x < o.width; x += B) {
        const h = Math.round(((L.base - L.amp * hill(x, L.f, L.ph)) * H) / B) * B;
        x2.fillStyle = L.fill; x2.fillRect(x, h, B, o.height - h);
        x2.fillStyle = L.top; x2.fillRect(x, h, B, B);
        x2.fillStyle = "rgba(255,255,255,.035)";
        for (let y = h + B; y < o.height; y += B) x2.fillRect(x, y, B, 1);
        x2.fillRect(x, h, 1, o.height - h);
        if (li === 2 && rnd() < 0.07) { x2.fillStyle = rnd() < 0.5 ? "rgba(255,107,115,.55)" : "rgba(111,152,255,.45)"; x2.fillRect(x, h + B * (2 + Math.floor(rnd() * 3)), B, B); }
      }
      return { canvas: o, par: L.par };
    });
    clouds = [];
    for (let i = 0; i < Math.max(3, Math.round(W / 300)); i++) {
      const w = (3 + Math.floor(rnd() * 5)) * B, h = (1 + Math.floor(rnd() * 2)) * B;
      clouds.push({ x: rnd() * W, y: Math.round((H * (0.08 + rnd() * 0.3)) / B) * B, w, h, v: 4 + rnd() * 8 });
    }
    return true;
  }
  let drawn = 0;
  function frame(t) {
    if (running && t - drawn < 32) { raf = requestAnimationFrame(frame); return; }
    drawn = t;
    const dt = Math.min(0.1, (t - (last || t)) / 1000);
    last = t;
    const scroll = Math.min(window.scrollY, H);
    g.drawImage(sky, 0, 0, W, H);
    for (const s of stars) {
      g.globalAlpha = s.tw && !reduceMotion ? s.a * (0.55 + 0.45 * Math.sin(t / 700 + s.ph)) : s.a;
      g.fillStyle = s.red ? "#ff9aa0" : "#d6e2ff";
      g.fillRect(s.x, s.y + scroll * 0.45, 2, 2);
    }
    g.globalAlpha = 1;
    for (const cl of clouds) {
      if (!reduceMotion) cl.x += cl.v * dt;
      if (cl.x > W + 20) cl.x = -cl.w - 20;
      const x = Math.round(cl.x / 2) * 2, y = cl.y + scroll * 0.4;
      g.fillStyle = "rgba(170,190,235,.07)";
      g.fillRect(x, y, cl.w, cl.h);
      g.fillRect(x + B, y - B / 2, cl.w - 2 * B, B / 2);
    }
    for (const L of layers) {
      const dx = -32 + mouse * 24 * L.par;
      const dy = scroll * 0.5 * (1 - L.par);
      g.drawImage(L.canvas, Math.round(dx), Math.round(dy));
    }
    if (running) raf = requestAnimationFrame(frame);
  }
  function start() {
    if (running || reduceMotion) return;
    running = true;
    last = 0;
    raf = requestAnimationFrame(frame);
  }
  function stop() { running = false; cancelAnimationFrame(raf); }
  function redraw() { if (build()) { frame(performance.now()); if (visible && !document.hidden) start(); } }
  let to;
  window.addEventListener("resize", () => { clearTimeout(to); to = setTimeout(() => { stop(); redraw(); }, 150); });
  window.addEventListener("pointermove", (e) => { if (e.pointerType === "mouse") mouse = e.clientX / window.innerWidth - 0.5; }, { passive: true });
  window.addEventListener("scroll", () => { if (!running && visible) frame(performance.now()); }, { passive: true });
  document.addEventListener("visibilitychange", () => (document.hidden ? stop() : visible && start()));
  if ("IntersectionObserver" in window) new IntersectionObserver(([en]) => { visible = en.isIntersecting; visible && !document.hidden ? start() : stop(); }).observe(c);
  redraw();
})();

// ------------------------------------------------------------------ go
(async function boot() {
  const list = await assetList();
  setup.list = list;
  await Promise.all([loadPictures(list), loadGallery(list), applyFonts(list)]);
  Sound.useFiles(list ? list.files : null);
  await refreshLive();
  if (root.classList.contains("hints")) drawSetup();
})();
