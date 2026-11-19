// Builds the adaptive launcher icon layers from docs/icon/glass-dark.svg: the same shapes, split into what is behind
// (the tile's gradient and its two soft lights) and what is in front (the glass phone and the bolt), on the 108dp canvas
// where the middle 66dp is always shown. Nothing is redrawn.
const { chromium } = require('playwright'); const fs = require('fs'), path = require('path');
const repo = path.resolve(__dirname, '../..');
const src = fs.readFileSync(repo + '/docs/icon/glass-dark.svg', 'utf8');
const defs = /<defs>[\s\S]*<\/defs>/.exec(src)[0];
const body = /<g clip-path="url\(#cand800034\)">([\s\S]*)<\/g>\s*<rect x="1.5"/.exec(src)[1];
const cut = body.indexOf('<path d="M206 112');           // the phone's shadow: the first shape of the foreground
const back = body.slice(0, cut), front = body.slice(cut);
const VB = '-128 -128 768 768';                            // the launcher shows the middle 72 of 108dp: exactly the 512 units of the original tile
const svg = inner => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${VB}">${defs}${inner}</svg>`;
// Behind, the tile's own gradient runs on past the tile's edge with the same colours at the same places.
const grad = /<linearGradient id="tland800034"[^>]*>([\s\S]*?)<\/linearGradient>/.exec(src)[1];
const bg = svg(`<defs><linearGradient id="wide" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="512" y2="512">${grad}</linearGradient></defs>`
  + back.replace('<rect width="512" height="512" fill="url(#tland800034)"', '<rect x="-128" y="-128" width="768" height="768" fill="url(#wide)"'));
const fg = svg(front);
(async () => {
  const b = await chromium.launch();
  for (const [d, px] of [['mdpi', 108], ['hdpi', 162], ['xhdpi', 216], ['xxhdpi', 324], ['xxxhdpi', 432]]) {
    for (const [name, s] of [['appicon_background', bg], ['appicon_foreground', fg]]) {
      const p = await b.newPage({ viewport: { width: px, height: px }, deviceScaleFactor: 1 });
      await p.setContent(`<body style="margin:0;background:transparent"><img width="${px}" height="${px}" style="display:block" src="data:image/svg+xml;base64,${Buffer.from(s).toString('base64')}">`);
      await p.waitForTimeout(150);
      await p.screenshot({ path: path.join(repo, 'src/FastbootStudio.Mobile/Resources/mipmap-' + d, name + '.png'), omitBackground: true });
      await p.close();
    }
  }
  // preview: circle, squircle and full
  const p = await b.newPage({ viewport: { width: 700, height: 240 } });
  const u = n => 'data:image/png;base64,' + fs.readFileSync(repo + '/src/FastbootStudio.Mobile/Resources/mipmap-xxxhdpi/' + n + '.png').toString('base64');
  const layer = (clip) => `<div style="position:relative;width:216px;height:216px;clip-path:${clip}"><img src="${u('appicon_background')}" width=216 style="position:absolute"><img src="${u('appicon_foreground')}" width=216 style="position:absolute"></div>`;
  await p.setContent(`<body style="margin:8px;background:#c33;display:flex;gap:12px">${layer('circle(33.3% at 50% 50%)')}${layer('inset(16.7% round 22%)')}${layer('none')}`);
  await p.waitForTimeout(200); await p.screenshot({ path: 'adaptive-preview.png' }); await b.close();
})();
