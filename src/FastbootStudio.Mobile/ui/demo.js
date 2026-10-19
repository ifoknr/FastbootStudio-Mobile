'use strict';
// A pretend phone for previews and screenshots in a desktop browser. Not packed into the app.
// ?lang=ar|en&theme=dark|light&root=0&dev=1&otg=1

(function () {
  const q = new URLSearchParams(location.search);
  const settings = { theme: q.get('theme') || 'dark', lang: q.get('lang') || 'ar', dev: q.get('dev') === '1', backupFirst: true, readBack: true, minBattery: 50, autoUpdate: true };
  const root = q.get('root') !== '0';
  const sha = (s) => { let h = 0x811c9dc5; for (const c of s) h = Math.imul(h ^ c.charCodeAt(0), 16777619) >>> 0; return (h.toString(16).padStart(8, '0').repeat(8)); };
  const phone = {
    root, rootManager: root ? 'KernelSU 1.0.5' : null, selinux: root ? 'Enforcing' : null,
    model: '23078PND5G', name: 'Xiaomi 13T Pro', brand: 'Xiaomi', codename: 'corot', product: 'corot_global', serial: root ? 'a1b2c3d4' : null,
    android: '14', sdk: '34', patch: '2026-09-05', slot: '_a', ab: true, dynamic: true, platform: 'mt6985', abi: 'arm64-v8a',
    vendor: 'MediaTek', kernel: '6.1.75-android14-11-g4e8c1f2a9b10', kmi: 'android14-6.1', avb: 'orange', unlocked: true, battery: 82,
    targets: ['boot', 'init_boot', 'vendor_boot', 'dtbo', 'vbmeta', 'vbmeta_system'],
  };
  const files = (names) => names.map(n => ({ name: n + '.img', sha256: sha(n), partition: n }));
  const backups = [
    { name: '23078PND5G_a1b2c3d4_20261015-221804', serial: 'a1b2c3d4', model: '23078PND5G', set: 'boot', mode: 'companion', kb: 122880, mine: true, files: files(['boot_a', 'dtbo_a', 'init_boot_a', 'vbmeta_a', 'vbmeta_system_a', 'vendor_boot_a']) },
    { name: '23078PND5G_a1b2c3d4_20261009-091233', serial: 'a1b2c3d4', model: '23078PND5G', set: 'before-flash', kb: 8192, mine: true, files: files(['init_boot_a']) },
    { name: '23078PND5G_a1b2c3d4_20260921-184012', serial: 'a1b2c3d4', model: '23078PND5G', set: 'boot', kb: 114688, mine: true, files: files(['boot_a', 'dtbo_a', 'init_boot_a', 'vbmeta_a', 'vendor_boot_a']) },
    { name: 'Redmi_Note_12_9f8e7d6c_20260830-120501', serial: '9f8e7d6c', model: 'Redmi Note 12', set: 'boot', kb: 98304, mine: false, files: files(['boot_a', 'vendor_boot_a']) },
  ];
  const picks = (q.get('pick') || 'magisk_patched-28100_Xk2.img').split(',');
  let pickIndex = 0;
  const logs = [];
  const log = l => logs.push(new Date().toTimeString().slice(0, 8) + ' ' + l);
  let pending = null, otgPhone = null;
  const otgDevice = { id: '/dev/bus/usb/001/002', name: 'Android', maker: 'Xiaomi', vid: '18d1', pid: 'd00d', permitted: false, open: false };

  const item = (level, code, values, override) => ({ level, original: override && level === 'warn' ? 'block' : level, override: !!override, code, values: values || [] });
  const result = (items) => {
    const order = ['ok', 'notice', 'warn', 'block'];
    const verdict = items.reduce((w, i) => order.indexOf(i.level) > order.indexOf(w) ? i.level : w, 'ok');
    return { verdict, canFlash: verdict !== 'block', sha256: sha(JSON.stringify(items)), items };
  };
  const keep = (kind, partition, check, typed, extra) => {
    pending = { id: Math.random().toString(16).slice(2), kind, partition, check };
    return Object.assign({ checkId: pending.id, partition, typed, check }, extra || {});
  };

  function imageCheck(a, target) {
    const part = a.base + (target.slot ? (a.other ? (target.slot === '_a' ? '_b' : '_a') : target.slot) : '');
    const items = [];
    if (a.source === 'backup' || a.source === 'folder') {
      const b = backups.find(x => x.name === a.name) || { serial: target.serial, model: '23078PND5G' };
      const from = (a.file || '').replace(/\.img$/, '');
      if (b.serial !== target.serial) items.push(item('block', 'OtherPhone', [b.model + ' · ' + b.serial, (target.model || target.product) + ' · ' + target.serial]));
      else items.push(item('ok', 'SamePhone', [(target.model || target.product) + ' · ' + target.serial]));
      items.push(from.replace(/_[ab]$/, '') === a.base ? item('ok', 'SamePartition', [from, part]) : item('block', 'OtherPartition', [from, part]));
      if ((a.name || '').includes('20260921')) items.push(item('block', 'HashMismatch', [sha('x'), sha('y')]));
      else items.push(item('ok', 'HashMatches', [sha(a.file || '')]));
      if (a.base === 'boot' && (a.name || '').includes('20261015')) items.push(item('notice', 'KernelVersionDiffers', ['6.1.162', '6.1.75']));
      if (a.base === 'init_boot' || a.base === 'boot') items.push(item('ok', 'BootCheck', ['KernelSummary', '6.1.75-android14-11', 'android14-6.1']));
    } else {
      if (a.expected) items.push(a.expected.length === 64 ? item('ok', 'ExpectedHashMatches', [a.expected]) : item('block', 'ExpectedHashMismatch', [a.expected, sha('z')]));
      items.push(item('warn', 'NoFingerprint', [a.name || 'file.img']));
      items.push(item('ok', 'BootCheck', ['RootSummary', 'Magisk']));
    }
    return { part, items };
  }

  const handlers = {
    'init': () => ({ settings, version: '1.0.0', systemDark: true, backupRoot: '/sdcard/FastbootStudio/Backups' }),
    'settings.set': a => { if (a.key in settings) settings[a.key] = a.value; return Object.assign({}, settings); },
    'ui.bars': () => null,
    'device.refresh': () => phone,
    'pick': () => { const name = picks[pickIndex++ % picks.length]; return { token: 't' + pickIndex, name, size: name.endsWith('.zip') ? 32925696 : 8388608, zip: name.endsWith('.zip') }; },
    'pickFolder': () => ({ token: 'f1', name: 'Redmi_Note_12_9f8e7d6c_20260830-120501', serial: '9f8e7d6c', model: 'Redmi Note 12', date: '2026-08-30', hasSums: true,
      files: [{ name: 'boot_a.img', partition: 'boot_a', listed: true }, { name: 'vendor_boot_a.img', partition: 'vendor_boot_a', listed: true }] }),
    'flash.check': a => {
      if (!root) throw 'no-root';
      const name = a.source === 'file' ? picks[(pickIndex - 1 + picks.length) % picks.length] : null;
      const r = imageCheck(Object.assign({ name: a.name }, a, name ? { name } : {}), phone);
      return keep('image', r.part, result(r.items), a.base);
    },
    'flash.run': async a => {
      const p = pending; pending = null;
      if (!p || p.id !== a.checkId) throw 'check-expired';
      const ev = (l, ms) => new Promise(r => setTimeout(() => { window.fbsEvent('progress', { line: l }); r(); }, ms));
      await ev('check ok ' + p.partition, 300);
      await ev('backup ' + p.partition, 400);
      await ev('backup ok /sdcard/FastbootStudio/Backups/23078PND5G_a1b2c3d4_20261018-101544', 900);
      await ev('write ' + p.partition + ' 8388608 bytes', 300);
      if (q.get('hold') === 'write') await new Promise(() => {});
      await ev('verify ok ' + p.check.sha256, 1200);
      await ev('done ' + p.partition, 200);
      return { partition: p.partition, written: true, verified: true, backup: '23078PND5G_a1b2c3d4_20261018-101544', sha256: p.check.sha256 };
    },
    'kernel.check': a => {
      const name = picks[(pickIndex - 1 + picks.length) % picks.length];
      const other = /sweet/.test(name);
      const items = [];
      if (a.expected) items.push(item('ok', 'ExpectedHashMatches', [a.expected]));
      items.push(other ? item(settings.dev ? 'warn' : 'block', 'DeviceNotListed', ['sweet, sweetin', 'corot'], true) : item('ok', 'DeviceListed', ['corot, corotin', 'corot']));
      items.push(item('ok', 'AndroidSupported', ['14', '13 - 15']));
      if (/5\.10/.test(name)) items.push(item(settings.dev ? 'warn' : 'block', 'KernelSeriesDiffers', ['5.10.107', '6.1.75', 'image'], true));
      else items.push(item('notice', 'KernelVersionDiffers', ['6.1.162', '6.1.75']));
      if (!a.expected) items.push(item('notice', 'NoFingerprint', [name]));
      return keep('kernel', 'boot', result(items), 'boot', { kernel: other ? 'Kernel sweet v2.1 by dev' : 'Kernel corot v4.2 by dev', devices: other ? ['sweet', 'sweetin'] : ['corot', 'corotin'],
        deviceCheck: true, block: 'auto', versions: '13 - 15', sha256: sha(name), kernelVersion: /5\.10/.test(name) ? '5.10.107' : '6.1.162', kernelKmi: /5\.10/.test(name) ? 'android13-5.10' : 'android14-6.1', kernelSource: 'image' });
    },
    'kernel.run': async a => {
      const p = pending; pending = null;
      if (!p || p.id !== a.checkId) throw 'check-expired';
      const lines = ['backup boot_a', 'backup init_boot_a', 'backup ok /sdcard/FastbootStudio/Backups/23078PND5G_a1b2c3d4_20261018-102101', 'install Kernel-corot-v4.2-AK3.zip',
        'AnyKernel3 by osm0sis @ xda-developers', 'Kernel corot v4.2 by dev', 'Checking device...', 'corot', 'Installing...', 'Done!', 'done boot_a changed'];
      for (const l of lines) await new Promise(r => setTimeout(() => { window.fbsEvent('progress', { line: l }); r(); }, 250));
      return { installed: true, changed: true, backup: '23078PND5G_a1b2c3d4_20261018-102101' };
    },
    'companion.find': () => root ? { installed: true, version: 'v2.5.1', enabled: true, usable: true } : { installed: false },
    'companion.backup': async a => {
      const parts = a.set === 'critical' ? ['abl_a', 'xbl_a', 'modem_a', 'modemst1', 'modemst2', 'persist', 'devinfo', 'frp'] : ['boot_a', 'init_boot_a', 'vendor_boot_a', 'dtbo_a', 'vbmeta_a'];
      window.fbsEvent('progress', { line: 'plan ' + parts.length });
      for (const p of parts) await new Promise(r => setTimeout(() => { window.fbsEvent('progress', { line: 'backup ' + p }); r(); }, 200));
      if (q.get('hold') === 'comp') await new Promise(() => {});
      const name = '23078PND5G_a1b2c3d4_20261019-090000';
      backups.unshift({ name, serial: 'a1b2c3d4', model: '23078PND5G', set: a.set, mode: 'companion', kb: 40960, mine: true, files: files(parts) });
      window.fbsEvent('progress', { line: 'backup ok /sdcard/FastbootStudio/Backups/' + name });
      return { name };
    },
    'companion.bundle': () => ({ file: '/sdcard/FastbootStudio/Reports/companion-report_20261019-090500.tar.gz' }),
    'update.check': a => (!a.force && !settings.autoUpdate) ? { skipped: true }
      : { installed: '1.0.0', latest: q.get('upd') || '1.0.0', newer: !!q.get('upd'), page: 'https://github.com/ifoknr/FastbootStudio-Mobile/releases/tag/v' + (q.get('upd') || '1.0.0') },
    'backup.list': () => root ? backups : [],
    'backup.create': async () => {
      for (const p of ['boot_a', 'init_boot_a', 'vendor_boot_a', 'dtbo_a', 'vbmeta_a', 'vbmeta_system_a']) await new Promise(r => setTimeout(() => { window.fbsEvent('progress', { line: 'backup ' + p }); r(); }, 250));
      window.fbsEvent('progress', { line: 'backup ok /sdcard/FastbootStudio/Backups/23078PND5G_a1b2c3d4_20261018-103000' });
      return { name: '23078PND5G_a1b2c3d4_20261018-103000' };
    },
    'backup.verify': a => new Promise(r => setTimeout(() => r({ failed: a.name.includes('20260921') ? ['boot_a.img'] : [] }), 500)),
    'backup.delete': a => { const i = backups.findIndex(b => b.name === a.name); if (i >= 0) backups.splice(i, 1); return null; },
    'reboot': () => null,
    'logs': () => logs.concat(['10:15:41 $ id -u', '10:15:44 check init_boot_a.img -> init_boot_a: Ok', '10:15:44   Ok SamePhone 23078PND5G · a1b2c3d4', '10:15:44   Ok HashMatches 7f3a…c91e',
      '10:15:45 $ dd if=/data/user/0/…/init_boot_a.img of=/dev/block/by-name/init_boot_a bs=4194304 conv=notrunc,fsync && sync', '10:15:47 verify ok 7f3a…c91e', '10:15:47 done init_boot_a',
      '10:21:03 check Kernel-sweet-v2.1-AK3.zip -> boot: Block', '10:21:03   Block DeviceNotListed sweet, sweetin | corot']),
    'copy': () => null,
    'open': () => null,
    'otg.scan': () => q.get('otg') === '1' ? [Object.assign({}, otgDevice, { open: !!otgPhone })] : [],
    'otg.connect': () => new Promise(r => setTimeout(() => {
      otgPhone = { serial: '9f8e7d6c', product: 'sweet', unlocked: 'yes', userspace: false, bootloader: 'unknown', maxDownload: 268435456, slot: '_a', targets: ['boot', 'vendor_boot', 'dtbo', 'vbmeta', 'recovery'] };
      r(otgPhone);
    }, 600)),
    'otg.check': a => {
      const r = imageCheck(Object.assign({}, a, a.source === 'folder' ? { name: 'Redmi_Note_12_9f8e7d6c_20260830-120501' } : { name: picks[(pickIndex - 1 + picks.length) % picks.length] }), otgPhone);
      return keep('otg', r.part, result(r.items), a.base, { tooBigForDownload: false, size: 67108864 });
    },
    'otg.flash': async a => {
      const p = pending; pending = null;
      if (!p || p.id !== a.checkId) throw 'check-expired';
      for (let s = 0; s <= 64; s += 8) await new Promise(r => setTimeout(() => { window.fbsEvent('otgProgress', { sent: s * 1048576, total: 67108864 }); r(); }, 150));
      if (q.get('hold') === 'otg') await new Promise(() => {});
      return { partition: p.partition, sha256: p.check.sha256 };
    },
    'otg.reboot': () => { otgPhone = null; return null; },
    'otg.close': () => { otgPhone = null; return null; },
  };

  window.fbsNative = {
    call(id, method, json) {
      const a = JSON.parse(json || '{}');
      log(method);
      Promise.resolve().then(() => handlers[method] ? handlers[method](a) : Promise.reject('unknown-method'))
        .then(data => setTimeout(() => window.fbsReply(id, { ok: true, data: data === undefined ? null : data }), 120))
        .catch(e => setTimeout(() => window.fbsReply(id, { ok: false, error: String(e) }), 120));
    },
  };
})();
