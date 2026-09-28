// Runs on Vercel before every deploy (see vercel.json). It lists the pictures, sounds and fonts
// you've added, so the website knows exactly which files exist: nothing to set up, and no
// "file not found" errors in visitors' browsers. You never need to run or edit this yourself.
import { readdirSync, statSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const FOLDERS = ["images", "sounds", "fonts"];
const SKIP = /(^|\/)(readme|license|ofl)[^/]*$|\.(md|txt|json|js|html|css)$|(^|\/)\./i;
const files = [];

function walk(dir) {
  let names;
  try {
    names = readdirSync(dir);
  } catch {
    return;
  }
  for (const name of names) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path);
    else {
      const rel = path.split("\\").join("/");
      if (!SKIP.test(rel)) files.push(rel);
    }
  }
}
FOLDERS.forEach(walk);
files.sort();
writeFileSync("assets.json", JSON.stringify({ built: new Date().toISOString(), files }, null, 1));

console.log(`assets.json lists ${files.length} file${files.length === 1 ? "" : "s"}:`);
for (const f of files) console.log("  " + f);
const loud = files.filter((f) => f !== f.toLowerCase());
if (loud.length) console.log("Note: capital letters are fine, the site matches names in any case:", loud.join(", "));
