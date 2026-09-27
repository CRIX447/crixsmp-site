// CRIX SMP website: copy buttons, hotbar, pictures from images/, hero backdrop.
// Settings live in config.js (edit that file, not this one).
const CONFIG = Object.assign({
  realmCode: "WrxFsoWk5fc",
  discord: "https://discord.gg/VmW6mXzU7z",
  // Your own server's address. Leave serverIp empty ("") to hide the server box.
  serverIp: "",
  serverPort: 19132,
  // true = label every picture with the file name it wants (same as adding #images to the address)
  showImageNames: false,
}, window.CRIX_CONFIG || {});

const PREVIEW = false; // true = show image names, don't load images (used for previews)
const EXTS = ["png", "jpg", "jpeg", "webp"];
const FOLDER = "images/";
const root = document.documentElement;
const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];

// ---------- links + Realm code ----------
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

// ---------- copy buttons ----------
async function copyText(text, btn, selectEl) {
  const old = btn.dataset.label || (btn.dataset.label = btn.textContent);
  try {
    await navigator.clipboard.writeText(text);
    btn.textContent = "Copied!";
  } catch (e) {
    if (selectEl) {
      const r = document.createRange();
      r.selectNodeContents(selectEl);
      const sel = window.getSelection();
      sel.removeAllRanges();
      sel.addRange(r);
    }
    btn.textContent = "Selected: copy it now";
  }
  clearTimeout(btn._t);
  btn._t = setTimeout(() => (btn.textContent = old), 2400);
}
$$("[data-copy]").forEach((b) => b.addEventListener("click", () => copyText(String(CONFIG.realmCode).trim(), b, $("[data-code]"))));

// ---------- your own server (optional) ----------
(function server() {
  const ip = String(CONFIG.serverIp || "").trim();
  if (!ip) return;
  $("#server").hidden = false;
  $("#server-ip").textContent = ip;
  $("#server-port").textContent = "Port " + CONFIG.serverPort;
  $("#copy-ip").addEventListener("click", (e) => copyText(ip, e.currentTarget, $("#server-ip")));
  if (PREVIEW) return;
  // live status from mcstatus.io (hidden if it can't be reached)
  fetch("https://api.mcstatus.io/v2/status/bedrock/" + encodeURIComponent(ip + ":" + CONFIG.serverPort))
    .then((r) => r.json())
    .then((s) => {
      const st = $("#status");
      st.hidden = false;
      st.classList.add(s.online ? "on" : "off");
      $("span", st).textContent = s.online ? `Online · ${s.players?.online ?? 0} / ${s.players?.max ?? "?"} players` : "Offline right now";
    })
    .catch(() => {});
})();

// ---------- hotbar ----------
(function hotbar() {
  const btns = $$(".hotbar button");
  const held = $("#held");
  function pick(i) {
    btns.forEach((b, j) => b.setAttribute("aria-pressed", String(i === j)));
    const b = btns[i];
    held.innerHTML = "";
    const c = document.createElement("span");
    c.className = "held-cmd";
    c.textContent = b.textContent.replace(/^\d/, "");
    held.append(c, b.dataset.what);
  }
  btns.forEach((b, i) => b.addEventListener("click", () => pick(i)));
  document.addEventListener("keydown", (e) => {
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    const t = e.target;
    if (t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))) return;
    if (/^[1-9]$/.test(e.key)) pick(Number(e.key) - 1);
  });
})();

// ---------- pictures from the images/ folder ----------
function tagFor(el, name, found) {
  let t = $(".img-tag", el);
  if (!t) {
    t = document.createElement("span");
    t.className = "img-tag";
    t.setAttribute("aria-hidden", "true");
    el.appendChild(t);
  }
  t.classList.toggle("ok", !!found);
  t.innerHTML = "";
  t.append(found ? "✓ " + found.replace(FOLDER, "images/") : "images/" + name + ".png");
  const sm = document.createElement("small");
  sm.textContent = found ? "found" : (el.dataset.size || "");
  t.appendChild(sm);
}
function probe(name) {
  return new Promise((done) => {
    let i = 0;
    const next = () => {
      if (i >= EXTS.length) return done(null);
      const url = FOLDER + name + "." + EXTS[i++];
      const im = new Image();
      im.onload = () => done(url);
      im.onerror = next;
      im.src = url;
    };
    next();
  });
}
const found = {};
async function loadPics() {
  const els = $$("[data-img]");
  const names = [...new Set(els.map((el) => el.dataset.img))];
  await Promise.all(names.map(async (n) => (found[n] = PREVIEW ? null : await probe(n))));
  for (const el of els) {
    const url = found[el.dataset.img];
    if (url) {
      const img = document.createElement("img");
      img.alt = el.dataset.alt || "";
      img.decoding = "async";
      img.src = url;
      el.prepend(img);
      el.classList.add("has-img");
    } else el.classList.add("no-img");
    tagFor(el, el.dataset.img, url);
  }
  if (!PREVIEW) favicon();
}
function favicon() {
  const pixel = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" shape-rendering="crispEdges"><rect width="16" height="16" fill="#080b11"/><path fill="#3d78ff" d="M1 3h6v2H3v6h4v2H1z"/><path fill="#ff6b73" d="M9 3h2v2H9zM13 3h2v2h-2zM10 5h4v2h-4zM11 7h2v2h-2zM10 9h4v2h-4zM9 11h2v2H9zM13 11h2v2h-2z"/></svg>';
  let link = $("link[rel~=icon]");
  if (!link) {
    link = document.createElement("link");
    link.rel = "icon";
    document.head.appendChild(link);
  }
  probe("favicon").then((u) => {
    if (u) return (link.href = u);
    if (found.logo) return (link.href = found.logo);
    link.href = "data:image/svg+xml," + encodeURIComponent(pixel);
  });
}

// ---------- gallery: gallery-1, gallery-2, ... (stops at the first missing number) ----------
const shots = [];
async function loadGallery() {
  if (!PREVIEW) {
    for (let n = 1; n <= 24; n++) {
      const u = await probe("gallery-" + n);
      if (!u) break;
      shots.push(u);
    }
  }
  drawGallery();
}
function drawGallery() {
  const sec = $("#gallery"), grid = $("#gallery-grid");
  const hints = root.classList.contains("hints");
  grid.textContent = "";
  shots.forEach((u, i) => {
    const f = document.createElement("figure");
    const img = document.createElement("img");
    img.src = u;
    img.alt = "CRIX SMP screenshot " + (i + 1);
    img.loading = "lazy";
    f.appendChild(img);
    if (hints) {
      const t = document.createElement("span");
      t.className = "img-tag ok";
      t.textContent = "✓ " + u;
      f.appendChild(t);
    }
    grid.appendChild(f);
  });
  if (hints) {
    const next = shots.length + 1;
    for (let n = next; n < next + (shots.length ? 1 : 3); n++) {
      const f = document.createElement("figure");
      f.className = "empty";
      const t = document.createElement("span");
      t.className = "img-tag";
      t.textContent = "images/gallery-" + n + ".png";
      const sm = document.createElement("small");
      sm.textContent = n === next + 2 || (shots.length && n === next) ? "…and so on, up to gallery-24" : "any size";
      t.appendChild(sm);
      f.appendChild(t);
      grid.appendChild(f);
    }
  }
  sec.hidden = !shots.length && !hints;
}

// ---------- image-name labels (#images) ----------
(function hints() {
  const btn = $("#hint-toggle");
  const asked = /(^|[#?&])(images|hints)\b/.test(location.hash + location.search);
  const start = PREVIEW || CONFIG.showImageNames || asked;
  function set(on) {
    root.classList.toggle("hints", on);
    $("#image-guide").hidden = !on;
    btn.setAttribute("aria-pressed", String(on));
    btn.textContent = on ? "Image names: on" : "Image names: off";
    drawGallery();
  }
  if (start) btn.hidden = false;
  btn.addEventListener("click", () => set(!root.classList.contains("hints")));
  set(start);
  window.addEventListener("hashchange", () => {
    if (/images|hints/.test(location.hash)) { btn.hidden = false; set(true); }
  });
})();

// ---------- hero backdrop: block hills under a night sky (hidden once images/hero is added) ----------
(function terrain() {
  const c = $("#terrain");
  if (!c) return;
  function draw() {
    const r = c.getBoundingClientRect();
    if (!r.width || !r.height) return;
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    c.width = Math.round(r.width * dpr);
    c.height = Math.round(r.height * dpr);
    const g = c.getContext("2d");
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    const W = r.width, H = r.height, B = W < 640 ? 12 : 16;
    const sky = g.createLinearGradient(0, 0, 0, H);
    sky.addColorStop(0, "#060913");
    sky.addColorStop(0.5, "#0c1633");
    sky.addColorStop(0.74, "#2a1230");
    sky.addColorStop(0.86, "#4a1624");
    sky.addColorStop(1, "#080b11");
    g.fillStyle = sky;
    g.fillRect(0, 0, W, H);
    let s = 20240611;
    const rnd = () => (s = (s * 16807) % 2147483647) / 2147483647;
    const stars = Math.round((W * H) / 7000);
    for (let i = 0; i < stars; i++) {
      g.globalAlpha = 0.18 + rnd() * 0.55;
      g.fillStyle = rnd() < 0.12 ? "#ff9aa0" : "#d6e2ff";
      const x = Math.floor((rnd() * W) / 4) * 4, y = Math.floor((rnd() * H * 0.55) / 4) * 4;
      g.fillRect(x, y, 2, 2);
    }
    g.globalAlpha = 1;
    // a square moon, right side (wide screens only: on phones it would sit on the title)
    const m = W < 720 ? 0 : Math.round(Math.min(W, H) * 0.075 / B) * B || B * 3;
    const mx = Math.round(W * 0.8 / B) * B, my = Math.round(H * 0.16 / B) * B;
    if (m) {
      g.fillStyle = "rgba(214,226,255,.08)";
      g.fillRect(mx - B, my - B, m + 2 * B, m + 2 * B);
      g.fillStyle = "#cdd9f6";
      g.fillRect(mx, my, m, m);
      g.fillStyle = "#aebde4";
      g.fillRect(mx + B, my + B, B, B);
      g.fillRect(mx + m - 2 * B, my + m - 2 * B, B, B);
    }
    const hill = (x, f, ph) => (Math.sin(x * f + ph) + 0.5 * Math.sin(x * f * 2.3 + ph * 1.7) + 0.25 * Math.sin(x * f * 5.1 + ph * 0.3)) / 1.75;
    const layers = [
      { base: 0.62, amp: 0.1, f: 0.006, ph: 1.2, top: "#1b2c5c", fill: "#111d3f" },
      { base: 0.73, amp: 0.08, f: 0.009, ph: 4.1, top: "#3a1a33", fill: "#1a1027" },
      { base: 0.85, amp: 0.05, f: 0.014, ph: 2.6, top: "#1a2338", fill: "#0b1019" },
    ];
    for (const L of layers) {
      for (let x = 0; x < W; x += B) {
        const h = Math.round(((L.base - L.amp * hill(x, L.f, L.ph)) * H) / B) * B;
        g.fillStyle = L.fill;
        g.fillRect(x, h, B, H - h);
        g.fillStyle = L.top;
        g.fillRect(x, h, B, B);
        g.fillStyle = "rgba(255,255,255,.035)";
        for (let y = h + B; y < H; y += B) g.fillRect(x, y, B, 1);
        g.fillRect(x, h, 1, H - h);
      }
    }
    // a few red windows / lava glints in the nearest hills
    let t = 7;
    const r2 = () => (t = (t * 48271) % 2147483647) / 2147483647;
    for (let i = 0; i < Math.round(W / 140); i++) {
      const x = Math.floor((r2() * W) / B) * B;
      const L = layers[2];
      const h = Math.round(((L.base - L.amp * hill(x, L.f, L.ph)) * H) / B) * B;
      g.fillStyle = r2() < 0.5 ? "rgba(255,107,115,.55)" : "rgba(111,152,255,.45)";
      g.fillRect(x, h + B * (2 + Math.floor(r2() * 3)), B, B);
    }
  }
  let to;
  const redraw = () => { clearTimeout(to); to = setTimeout(draw, 120); };
  draw();
  window.addEventListener("resize", redraw);
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(draw);
})();

loadPics();
loadGallery();
