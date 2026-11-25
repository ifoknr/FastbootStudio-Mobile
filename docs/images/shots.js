// The README pictures (docs/images): 390x844 at 2x, dark, English and Arabic, from the desktop preview (demo.js).
const { chromium } = require('playwright'); const path = require('path');
const repo = path.resolve(__dirname, '../..'), UI = 'file://' + repo + '/src/FastbootStudio.Mobile/ui/index.html';
const click = s => async p => { await p.click(s); await p.waitForTimeout(600); };
const shots = {
  device: ['', []],
  flash: ['', [click('nav [data-v=flash]'), click('[data-act=pickBackup]'), click('[data-act=useBackup][data-v="23078PND5G_a1b2c3d4_20261009-091233"]')]],
  kernel: ['&pick=Kernel-sweet-v2.1-AK3.zip', [click('nav [data-v=flash]'), click('[data-act=pickFile]')]],
  backups: ['', [click('nav [data-v=backup]'), click('[data-act=bkOpen][data-v="23078PND5G_a1b2c3d4_20261015-221804"]')]],
  otg: ['&otg=1', [click('nav [data-v=otg]'), click('[data-act=otgConnect]'), p => p.waitForTimeout(500)]],
};
(async () => { const b = await chromium.launch();
  for (const lang of ['en', 'ar']) for (const [name, [q, steps]] of Object.entries(shots)) {
    const c = await b.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 }); const p = await c.newPage();
    await p.goto(`${UI}?lang=${lang}&theme=dark${q}`); await p.waitForTimeout(900);
    for (const s of steps) await s(p);
    await p.screenshot({ path: path.join(repo, 'docs/images', `${lang}-${name}.png`) }); await c.close(); }
  await b.close(); })();
