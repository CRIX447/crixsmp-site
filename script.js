/* ============================================================
   CRIX S.M.P — site script
   Edit the settings block, everything else follows.
   ============================================================ */

const IP = 'play.crixsmp.net';
const BEDROCK_PORT = '19132';
const DISCORD = 'https//:discord.gg/KufFPEptTG';

/* Title-screen splash lines. One is picked at random on every load.
   Add, remove, rewrite — keep them short so they don't wrap. */
const SPLASHES = [
  'Java and Bedrock!',
  'Same world, either edition!',
  'Bring a shovel!',
  'No whitelist!',
  'Now with more chunks!',
  'Watch out for creepers!',
  'Built by the community!',
  '100% survival!'
];

const REFRESH_MS = 45000;      // how often the status panel re-checks
const VOLUME = 0.35;           // UI sound volume, 0 to 1

/* ---------- fill in IP / port / Discord ---------- */
document.querySelectorAll('[data-ip]').forEach(el => (el.textContent = IP));
document.querySelectorAll('[data-port]').forEach(el => (el.textContent = BEDROCK_PORT));
document.querySelectorAll('[data-discord]').forEach(el => (el.href = DISCORD));

/* ---------- UI sounds ----------
   Drop your own files into sounds/ as click.mp3 and hover.mp3.
   If they aren't there, the site stays silent and nothing breaks. */
let muted = localStorage.getItem('crix-muted') === '1';

let soundsAvailable = true;

const sounds = {
  click: new Audio('/sounds/click.mp3'),
  hover: new Audio('/sounds/hover.mp3')
};
Object.values(sounds).forEach(a => {
  a.volume = VOLUME;
  a.preload = 'none';
  // if the file isn't there, stop trying rather than 404 on every click
  a.addEventListener('error', () => { soundsAvailable = false; });
});

function play(name) {
  if (muted || !soundsAvailable || !sounds[name]) return;
  const s = sounds[name].cloneNode();
  s.volume = VOLUME;
  s.play().catch(() => {}); // no file yet, or the browser blocked it
}

const soundBtn = document.getElementById('sound');
function paintSound() {
  if (!soundBtn) return;
  soundBtn.textContent = muted ? 'OFF' : 'ON';
  soundBtn.setAttribute('aria-label', muted ? 'Turn UI sounds on' : 'Turn UI sounds off');
  soundBtn.setAttribute('aria-pressed', String(!muted));
}
paintSound();

if (soundBtn) {
  soundBtn.addEventListener('click', () => {
    muted = !muted;
    localStorage.setItem('crix-muted', muted ? '1' : '0');
    paintSound();
    play('click');
  });
}

// every button and nav link clicks; hovering a button ticks
document.querySelectorAll('.btn, nav a, button, summary').forEach(el => {
  el.addEventListener('click', () => play('click'));
});
document.querySelectorAll('.btn, nav a').forEach(el => {
  el.addEventListener('mouseenter', () => play('hover'));
});

/* ---------- splash text ---------- */
const splash = document.getElementById('splash');
if (splash) splash.textContent = SPLASHES[Math.floor(Math.random() * SPLASHES.length)];

/* ---------- mobile menu ---------- */
const menuBtn = document.getElementById('menu');
const nav = document.querySelector('nav');
if (menuBtn) {
  menuBtn.addEventListener('click', () => {
    const open = nav.classList.toggle('open');
    menuBtn.setAttribute('aria-expanded', String(open));
    menuBtn.textContent = open ? '✕' : '☰';
  });
}

/* ---------- copy buttons ----------
   <button data-copy="ip"> or data-copy="port" ---------- */
function toast(msg) {
  let t = document.getElementById('toast');
  if (!t) {
    t = document.createElement('div');
    t.id = 'toast';
    t.setAttribute('role', 'status');
    document.body.appendChild(t);
  }
  t.textContent = msg;
  t.classList.add('show');
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => t.classList.remove('show'), 1800);
}

async function copy(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const f = document.createElement('textarea');
    f.value = text;
    f.style.cssText = 'position:fixed;top:-1000px';
    document.body.appendChild(f);
    f.select();
    const ok = document.execCommand('copy');
    f.remove();
    return ok;
  }
}

document.querySelectorAll('[data-copy]').forEach(btn => {
  const label = btn.textContent;
  btn.addEventListener('click', async () => {
    const value = btn.dataset.copy === 'port' ? BEDROCK_PORT : IP;
    const ok = await copy(value);
    btn.classList.add('done');
    btn.textContent = ok ? 'Copied!' : value;
    toast(ok ? 'IP copied!' : 'Copy this: ' + value);
    setTimeout(() => {
      btn.classList.remove('done');
      btn.textContent = label;
    }, 1800);
  });
});

/* ---------- live server status ----------
   Real data from api.mcsrvstat.us. Free, public, no API key, so there's
   nothing secret sitting in this file. Java and Bedrock are checked
   separately because they're different protocols on different ports. */
const status = document.getElementById('status');

if (status) {
  const el = id => document.getElementById(id);
  let checkedAt = 0;

  async function ask(url) {
    const r = await fetch(url, { cache: 'no-store' });
    if (!r.ok) throw new Error(r.status);
    return r.json();
  }

  function ago(ms) {
    const s = Math.round((Date.now() - ms) / 1000);
    if (s < 5) return 'just now';
    if (s < 60) return s + ' seconds ago';
    const m = Math.floor(s / 60);
    return m === 1 ? '1 minute ago' : m + ' minutes ago';
  }

  function tag(node, up, name) {
    node.className = 'tag ' + (up ? 'on' : 'off');
    node.innerHTML = `<em></em>${name}: ${up ? 'Online' : 'Offline'}`;
  }

  async function check() {
    status.classList.add('checking');

    const [j, b] = await Promise.allSettled([
      ask(`https://api.mcsrvstat.us/3/${IP}`),
      ask(`https://api.mcsrvstat.us/bedrock/3/${IP}:${BEDROCK_PORT}`)
    ]);

    const java = j.status === 'fulfilled' ? j.value : null;
    const bedrock = b.status === 'fulfilled' ? b.value : null;
    const javaUp = !!(java && java.online);
    const bedrockUp = !!(bedrock && bedrock.online);
    const online = javaUp || bedrockUp;

    el('state').className = 'tag ' + (online ? 'on' : 'off');
    el('state').innerHTML = `<em></em>${online ? 'Online' : 'Server offline'}`;
    tag(el('tagJava'), javaUp, 'Java');
    tag(el('tagBedrock'), bedrockUp, 'Bedrock');

    if (online) {
      const s = javaUp ? java : bedrock;
      const now = (s.players && s.players.online) || 0;
      const max = (s.players && s.players.max) || 0;
      el('players').innerHTML = `${now}<i>/</i><u>${max}</u>`;
      el('fill').style.width = max ? Math.min(100, (now / max) * 100) + '%' : '0%';
      el('motd').textContent =
        (s.motd && s.motd.clean && s.motd.clean.filter(Boolean).join(' ')) || IP;
      el('err').hidden = true;
    } else {
      // never leave an old count on screen pretending to be current
      el('players').innerHTML = '<u>—</u>';
      el('fill').style.width = '0%';
      el('motd').textContent = 'Not responding right now.';
      el('err').hidden = false;
      el('err').textContent = (!java && !bedrock)
        ? "Couldn't reach the status service, so the live count is unavailable. The server itself may still be up. Try connecting, or ask in Discord."
        : 'The server did not answer. It may be restarting or down for maintenance. Check Discord for updates.';
    }

    checkedAt = Date.now();
    el('stamp').textContent = 'just now';
    status.classList.remove('checking');
  }

  el('recheck').addEventListener('click', check);
  setInterval(() => checkedAt && (el('stamp').textContent = ago(checkedAt)), 1000);
  setInterval(check, REFRESH_MS);
  check();
}
