'use strict';
// Fastboot Studio Mobile: the pages. The phone is reached only through window.fbsNative
// (the app's bridge); every check is decided there, the page only shows it and passes on
// what the person pressed or typed.

// ---------------------------------------------------------------- bridge

const calls = new Map();
let seq = 0;
function call(method, args) {
  return new Promise((resolve, reject) => {
    const id = 'c' + (++seq);
    calls.set(id, { resolve, reject });
    try { window.fbsNative.call(id, method, JSON.stringify(args || {})); }
    catch (e) { calls.delete(id); reject(e); }
  });
}
window.fbsReply = (id, r) => {
  const c = calls.get(id);
  if (!c) return;
  calls.delete(id);
  if (r && r.ok) c.resolve(r.data); else c.reject(new Error(r ? r.error : 'internal'));
};
const listeners = {};
window.fbsEvent = (name, data) => (listeners[name] || []).forEach(f => { try { f(data); } catch (e) { console.error(e); } });
const on = (name, f) => (listeners[name] = listeners[name] || []).push(f);

// ---------------------------------------------------------------- icons

const ic = {
  device: '<rect x="7" y="2.5" width="10" height="19" rx="2"/><path d="M11 18.5h2"/>',
  flash: '<path d="M13 2.5 5 13.5h6l-1 8 8-11h-6z"/>',
  backup: '<path d="M12 3v12M7 10l5 5 5-5M4 19.5h16"/>',
  otg: '<path d="M8 3v5M16 3v5M6 8h12v4a6 6 0 0 1-12 0zM12 18v3"/>',
  more: '<circle cx="5" cy="12" r="1.3"/><circle cx="12" cy="12" r="1.3"/><circle cx="19" cy="12" r="1.3"/>',
  file: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/>',
  zip: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5M11 6h1M11 9h1M11 12h1M10.5 15h2v3h-2z"/>',
  folder: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
  warn: '<path d="M12 3 2 20h20z"/><path d="M12 10v4M12 17h.01"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>',
  shield: '<path d="M12 3 4 6v6c0 5 3.5 8 8 9 4.5-1 8-4 8-9V6z"/><path d="m8.5 12 2.5 2.5 4.5-5"/>',
  gear: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
  log: '<path d="M4 6h16M4 10h16M4 14h10M4 18h7"/>',
  about: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>',
  tg: '<path d="m21 4-18 7 6 2 2 6 3-4 5 4z"/><path d="m9 13 7-5"/>',
  gh: '<path d="M9 19c-4 1.5-4-2-6-2.5M15 21v-3.5c0-1 .1-1.4-.5-2 2.8-.3 5.5-1.4 5.5-6a4.6 4.6 0 0 0-1.3-3.2 4.2 4.2 0 0 0-.1-3.2s-1.1-.3-3.5 1.3a12 12 0 0 0-6.2 0C6.5 2.8 5.4 3.1 5.4 3.1a4.2 4.2 0 0 0-.1 3.2A4.6 4.6 0 0 0 4 9.5c0 4.6 2.7 5.7 5.5 6-.6.6-.6 1.2-.5 2V21"/>',
  pc: '<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/>',
  restore: '<path d="M3 12a9 9 0 1 0 3-6.7L3 8"/><path d="M3 3v5h5"/>',
  lock: '<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
  refresh: '<path d="M21 12a9 9 0 1 1-3-6.7L21 8"/><path d="M21 3v5h-5"/>',
  power: '<path d="M12 3v9"/><path d="M6.3 7.5a8 8 0 1 0 11.4 0"/>',
};
const svg = k => `<svg viewBox="0 0 24 24">${ic[k]}</svg>`;

// ---------------------------------------------------------------- state

const ALLOWED = ['boot', 'init_boot', 'vendor_boot', 'dtbo', 'vbmeta', 'vbmeta_system', 'recovery'];
const st = {
  ready: false, s: { theme: 'auto', lang: 'ar', dev: false, backupFirst: true, readBack: true, minBattery: 50 },
  systemDark: true, version: '1.0.0', backupRoot: '/sdcard/FastbootStudio/Backups',
  tab: 'device', page: null,
  phone: null, phoneErr: null, refreshing: false,
  backups: null, bk: {}, bkOpen: null,
  // The Companion module, looked for only with root: {installed, version, enabled, usable}.
  comp: null,
  // This phone's flash page: src is {type:'backup', name, file} or {type:'file', token, name, size}.
  f: { src: null, base: null, other: false, expected: '', check: null, checking: false, err: null },
  // An AnyKernel3 zip picked on the flash page.
  k: null,
  o: { devices: [], phone: null, id: null, connecting: false, err: null, src: null, folder: null, base: null, other: false, expected: '', check: null, checking: false },
  run: null,
  sheet: null,
  logs: [],
  toast: null,
};

// ---------------------------------------------------------------- small parts

const head = (title, sub, chip, back) => `<header>${back ? `<button class="back" data-act="back">${A ? '›' : '‹'}</button>` : '<img src="icon.png" alt="">'}
<div class="t"><h1>${title}</h1><p>${sub || ''}</p></div>${chip || ''}</header>`;
const rows = r => `<div class="rows">${r.filter(Boolean).map(([k, v, ar]) => `<div class="k">${k}</div><div class="v${ar ? ' ar' : ''}">${v}</div>`).join('')}</div>`;
const fileCard = (icon, name, meta, side, act) => `<button class="file"${act ? ` data-act="${act}"` : ''}><div class="ico">${svg(icon)}</div><div style="flex:1;min-width:0"><div class="n">${esc(name)}</div><div class="m">${meta}</div></div>${side || ''}</button>`;
const vrow = (kind, mark, text, small, extra) => `<div class="vrow"><span class="i ${kind}">${mark}</span><div>${text}${small ? `<small>${small}</small>` : ''}${extra || ''}</div></div>`;
const chip = (kind, text) => `<span class="chip ${kind || ''}">${text}</span>`;
const LEVEL = { ok: ['ok', '✓'], notice: ['info', '!'], warn: ['warn', '!'], block: ['bad', '✗'] };
const navItems = () => [['device', t('الجهاز', 'Device')], ['flash', t('التفليش', 'Flash')], ['backup', t('النسخ', 'Backups')], ['otg', 'OTG'], ['more', t('المزيد', 'More')]];
const nav = () => `<nav>${navItems().map(([k, l]) => `<button class="${k === st.tab ? 'on' : ''}" data-act="tab" data-v="${k}"><span class="pill">${svg(k)}</span>${l}</button>`).join('')}</nav>`;
const sheet = (color, body) => `<div class="scrim" data-act="sheetClose"></div><div class="sheet" style="border-top-color:var(--${color})"><div class="grab"></div>${body}</div>`;
const spinner = text => `<div class="banner plain"><span class="spin"></span><div class="body note" style="color:var(--text)">${text}</div></div>`;
const errBanner = code => `<div class="banner bad">${svg('warn')}<div class="body"><div class="big" style="font-size:14px">${errText(code)}</div></div></div>`;
const backupDate = name => {
  const m = /(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2})\d{2}$/.exec(name || '');
  if (!m) return '';
  const d = new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5]);
  return d.toLocaleDateString(A ? 'ar-u-nu-latn' : 'en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
};
const isToday = name => {
  const n = new Date(), p = x => String(x).padStart(2, '0');
  return (name || '').includes(`_${n.getFullYear()}${p(n.getMonth() + 1)}${p(n.getDate())}-`);
};
const setLabel = s => ({ boot: t('إقلاع', 'boot'), critical: t('حرجة', 'critical'), 'before-flash': t('قبل التفليش', 'before a flash'), 'before-kernel': t('قبل كيرنل', 'before a kernel') })[s] || esc(s || '');
const imagesCount = n => A ? (n === 1 ? 'صورة وحدة' : n === 2 ? 'صورتين' : n <= 10 ? `${n} صور` : `${n} صورة`) : `${n} image${n === 1 ? '' : 's'}`;
const slotName = (p, other) => {
  if (!p || !p.slot) return '';
  return other ? (p.slot === '_a' ? '_b' : '_a') : p.slot;
};

// The banner a check gets on the flash pages, and the rows of its items.
function checkRows(check, ctx, onlyIssues) {
  return check.items.filter(i => !onlyIssues || i.level !== 'ok').map(i => {
    const [kind, mark] = LEVEL[i.level] || LEVEL.block;
    const [text, small, extra] = checkText(i, ctx);
    let dev = '';
    if (i.override && i.original === 'block' && i.level === 'warn') dev = `<small class="dev">${t('عادةً ممنوع؛ وضع المطور يسمح فيه بعد ما تكتب الاسم', 'Normally refused; developer mode allows it once you type the name')}</small>`;
    else if (i.override && i.level === 'block') dev = `<small>${t('وضع المطور (من الإعدادات) يقدر يسمح فيه', 'Developer mode (Settings) can allow it')}</small>`;
    return vrow(kind, mark, i.level === 'block' ? `<b class="bad">${text}</b>` : text, small, (extra || '') + dev);
  }).join('');
}
function checkBanner(check, ctx) {
  const v = check.verdict;
  const kind = { ok: 'ok', notice: 'info', warn: 'warn', block: 'bad' }[v] || 'bad';
  const icon = { ok: 'shield', notice: 'info', warn: 'warn', block: 'warn' }[v] || 'warn';
  const title = {
    ok: ctx === 'kernel' ? t('يناسب جوالك ✓', 'Fits your phone ✓') : t('الملف مطابق ✓', 'The file checks out ✓'),
    notice: t('سليم، مع تنبيه خفيف', 'Fine, with something worth knowing'),
    warn: t('انتبه: يحتاج تأكيد منك', 'Careful: this needs your confirmation'),
    block: t('ممنوع: ما ينفلش', 'Refused: this will not be flashed'),
  }[v];
  return `<div class="banner ${kind}">${svg(icon)}<div class="body"><div class="big">${title}</div>${checkRows(check, ctx)}</div></div>`;
}

// A refused file: the reasons, and whether developer mode could ever allow it, on a sheet.
const whyButton = ctx => `<button class="btn danger" data-act="why" data-v="${ctx}">${svg('lock')}<span>${t('ليش ممنوع؟', 'Why is it refused?')}</span></button>`;

// ---------------------------------------------------------------- pages

const P = {};

P.device = () => {
  const p = st.phone;
  if (!p) return head(t('الجهاز', 'Device'), t('يقرا الجوال…', 'Reading the phone…')) + `<main>${st.phoneErr ? errBanner(st.phoneErr) : spinner(t('يقرا معلومات الجهاز…', 'Reading the device…'))}</main>`;
  const unlocked = p.unlocked === true ? (p.bootHidden ? t('مفتوح (مخفي بإضافة)', 'Unlocked (hidden by a module)') : t('مفتوح', 'Unlocked')) : p.unlocked === false ? t('مقفل', 'Locked') : t('غير معروف', 'Unknown');
  const avbKind = { green: 'ok', yellow: 'warn', orange: 'warn', red: 'bad' }[p.avb] || '';
  const mine = (st.backups || []).filter(b => b.mine);
  const today = mine.some(b => isToday(b.name));
  const battOk = p.battery < 0 || p.battery >= st.s.minBattery;
  const sub = t('هذا الجوال، وهو شغّال على أندرويد', 'This phone, running Android');
  const top = head(t('الجهاز', 'Device'), sub, p.root ? chip('ok', esc(p.rootManager ? p.rootManager.split(' ')[0] : t('روت', 'Root'))) : chip('bad', t('بدون روت', 'No root')));
  const device = `<div class="card"><h2>${t('الجهاز', 'Device')} ${p.vendor ? chip('', esc(p.vendor)) : ''}</h2>
    ${rows([[t('الموديل', 'Model'), esc(p.name && p.name !== p.model ? `${p.name} (${p.model})` : p.model)], [t('الاسم الرمزي', 'Codename'), esc(p.codename)], p.platform ? [t('المعالج', 'Chip'), esc([p.platform, p.abi].filter(Boolean).join(' · '))] : null,
      [t('أندرويد', 'Android'), esc(`${p.android || '?'} (SDK ${p.sdk || '?'})`)], p.patch ? [t('التصحيح الأمني', 'Security patch'), esc(p.patch)] : null,
      [t('السلوت', 'Slot'), esc([p.slot || t('بدون', 'none'), p.ab ? 'A/B' : null, p.dynamic ? 'dynamic' : null].filter(Boolean).join(' · ')), !p.slot && A],
      p.serial ? [t('الرقم', 'Serial'), esc(p.serial)] : null])}</div>`;
  if (!p.root)
    return top + `<main>${device}
      <div class="banner warn">${svg('lock')}<div class="body"><div class="big">${t('التفليش على هذا الجوال يحتاج روت', 'Flashing this phone needs root')}</div>
      <div class="note">${t('ما لقينا صلاحية الروت. إذا عندك روت، وافق على طلب الصلاحية من مدير الروت وحدّث. وقائمة OTG تشتغل بدون روت عشان تنقذ جوال ثاني.', 'No root access was found. If the phone is rooted, allow the app in your root manager and refresh. The OTG menu works without root to rescue another phone.')}</div></div></div>
      <div class="card"><h2>${t('وش يشتغل', 'What works')}</h2>
        <div class="check"><span class="dot ok"></span><span>${t('معلومات الجهاز', 'Device details')}</span></div>
        <div class="check"><span class="dot ok"></span><span>${t('إنقاذ جوال ثاني بالـ OTG', 'Rescue another phone over OTG')}</span></div>
        <div class="check"><span class="dot bad"></span><span>${t('التفليش والنسخ على هذا الجوال', 'Flashing and backups on this phone')}</span></div></div>
      <button class="btn" data-act="refresh">${svg('refresh')}<span>${t('تحديث', 'Refresh')}</span></button></main>` + nav();
  return top + `<main>${device}
    <div class="card"><h2>${t('الكيرنل والحماية', 'Kernel and security')} ${p.avb ? chip(avbKind, 'AVB ' + esc(p.avb)) : ''}</h2>
    ${rows([[t('الكيرنل', 'Kernel'), esc(p.kernel)], p.kmi ? ['KMI', esc(p.kmi)] : null, [t('البوت لودر', 'Bootloader'), unlocked, A],
      p.selinux ? ['SELinux', esc(p.selinux)] : null, [t('الروت', 'Root'), esc(p.rootManager || t('متاح', 'Granted')), !p.rootManager && A]])}</div>
    <div class="card"><h2>${t('جاهزية التفليش', 'Ready to flash')}</h2>
      <div class="check"><span class="dot ${p.unlocked === false ? 'bad' : p.unlocked ? 'ok' : 'info'}"></span><span>${p.unlocked === false ? t('البوت لودر مقفل: الصور المعدّلة ما تقلع', 'Bootloader locked: modified images will not boot') : p.unlocked ? (p.bootHidden ? t('البوت لودر مفتوح، وإضافة عندك تخفيه عن التطبيقات (عادي مع Play Integrity)', 'Bootloader unlocked; a module hides it from apps (normal with Play Integrity)') : t('البوت لودر مفتوح: الصور المعدّلة تقلع', 'Bootloader unlocked: modified images boot')) : t('ما قدرنا نعرف حالة البوت لودر', 'The bootloader state is unknown')}</span></div>
      <div class="check"><span class="dot ok"></span><span>${t('صلاحية الروت متاحة للتطبيق', 'Root access granted to the app')}</span></div>
      <div class="check"><span class="dot ${battOk ? 'ok' : 'bad'}"></span>${p.battery < 0 ? t('ما قدرنا نقرا البطارية', 'Battery level unknown') : battOk
        ? t(`البطارية ${L(p.battery + '%')}: فوق الحد الأدنى ${L(st.s.minBattery + '%')}`, `Battery ${p.battery}%: above the ${st.s.minBattery}% minimum`)
        : t(`البطارية ${L(p.battery + '%')}: أقل من ${L(st.s.minBattery + '%')}، اشحن قبل التفليش`, `Battery ${p.battery}%: below ${st.s.minBattery}%, charge before flashing`)}</div>
      ${st.backups ? `<div class="check"><span class="dot ${today ? 'ok' : 'warn'}"></span><span>${today ? t('فيه نسخة احتياطية من اليوم', 'There is a backup from today') : t('ما فيه نسخة احتياطية من اليوم', 'No backup from today')}</span></div>` : ''}
    </div>
    ${companionCard()}
    <button class="btn" data-act="refresh" ${st.refreshing ? 'disabled' : ''}>${svg('refresh')}<span>${t('تحديث', 'Refresh')}</span></button>
  </main>` + nav();
};

// Shown only when the module is installed (and so only with root).
function companionCard() {
  const c = st.comp;
  if (!c || !c.installed) return '';
  if (!c.usable)
    return `<div class="card"><h2>Fastboot Studio Companion ${chip('warn', t('موقفة', 'Disabled'))}</h2>
      <div class="note">${t('الإضافة مركّبة بس موقفة. فعّلها من برنامج الروت وأعد التشغيل، وبعدها تقدر تستخدمها من هنا.', 'The module is installed but disabled. Turn it on in your root manager and restart to use it from here.')}</div></div>`;
  return `<div class="card"><h2>Fastboot Studio Companion ${chip('ok', '✓ ' + esc(c.version || ''))}</h2>
    <div class="note">${t('الإضافة متصلة بالتطبيق: نسخها تطلع في صفحة النسخ، وتتحقق منها وتفلّشها من التطبيق. وتقدر تاخذ منها نسخة حرجة (البوت لودر والمودم و persist) ما ياخذها التطبيق لحاله.', 'The module works with the app: its backups show on the Backups page, where you check and flash them. It also takes a critical backup (bootloader chain, modem, persist) the app does not take by itself.')}</div>
    <div class="row2" style="margin-top:10px"><button class="btn sm" data-act="compBackup" data-v="critical"><span>${t('نسخة حرجة', 'Critical backup')}</span></button><button class="btn sm" data-act="compBundle"><span>${t('تقرير للدعم', 'Support report')}</span></button></div></div>`;
}

const noRoot = () => `<div class="banner warn">${svg('lock')}<div class="body"><div class="big">${t('يحتاج روت', 'Needs root')}</div><div class="note">${t('التفليش والنسخ على هذا الجوال يحتاج صلاحية الروت. للجوال الثاني استخدم قائمة OTG.', 'Flashing and backups on this phone need root. For another phone use the OTG menu.')}</div></div></div>`;

P.flash = () => {
  if (st.run && st.run.where === 'flash') return P.running();
  if (st.k) return P.kernel();
  const p = st.phone, f = st.f;
  const slot = slotName(p, f.other);
  const sub = t('اكتب صورة على قسم في هذا الجوال', 'Write an image to a partition on this phone');
  const top = head(t('التفليش', 'Flash'), sub, p && p.slot ? chip('ok', LE(p.slot)) : '');
  if (!p || !p.root) return top + `<main>${p ? noRoot() : spinner(t('يقرا الجوال…', 'Reading the phone…'))}</main>` + nav();
  let src = '';
  if (f.src) {
    const meta = f.src.type === 'backup'
      ? t(`من نسخة ${esc(backupDate(f.src.name))}`, `From the ${esc(backupDate(f.src.name))} backup`)
      : t(`ملف · ${LE(mib(f.src.size))}`, `A file · ${esc(mib(f.src.size))}`);
    src = fileCard('file', f.src.file || f.src.name, meta, chip('', t('تغيير', 'Change')), 'pickSource');
  }
  const targets = p.targets || [];
  const flashable = f.check && f.check.check.canFlash;
  return top + `<main>
    <div><div class="label">${t('١. الملف', '1. The file')}</div>${src}
      <div class="row2" style="margin-top:${src ? 8 : 0}px"><button class="btn sm" data-act="pickBackup"><span>${t('من النسخ', 'From backups')}</span></button><button class="btn sm" data-act="pickFile"><span>${t('ملف IMG أو ZIP', 'IMG or ZIP file')}</span></button></div></div>
    <div><div class="label">${t('٢. القسم', '2. Partition')}</div>
      <div class="parts">${ALLOWED.map(b => `<button class="${f.base === b ? 'on' : ''}" data-act="base" data-v="${b}" ${targets.includes(b) ? '' : 'disabled'}>${b}</button>`).join('')}</div></div>
    ${p.ab ? `<div><div class="label">${t('٣. السلوت', '3. Slot')}</div>
      <div class="seg"><button class="${f.other ? '' : 'on'}" data-act="slot" data-v="0"><b>${t('الحالي', 'Current')} ${LE(p.slot)}</b>${t('يبان بعد إعادة التشغيل', 'Takes effect on restart')}</button><button class="${f.other ? 'on' : ''}" data-act="slot" data-v="1"><b>${t('الثاني', 'Other')} ${LE(slotName(p, true))}</b>${t('للمتقدمين', 'Advanced')}</button></div></div>` : ''}
    ${f.src && f.src.type === 'file' ? `<div><div class="label">${t('بصمة المطور SHA-256 (اختياري)', 'Developer’s SHA-256 (optional)')}</div><input class="input" data-in="fexp" value="${esc(f.expected)}" placeholder="${t('الصق البصمة اللي نشرها المطور', 'Paste the hash the developer published')}" spellcheck="false" autocomplete="off"></div>` : ''}
    ${f.checking ? spinner(t('يتحقق من الملف…', 'Checking the file…')) : f.err ? errBanner(f.err) : f.check ? checkBanner(f.check.check, 'image') : !f.src ? `<div class="note">${t('اختر الملف والقسم، والتطبيق يتحقق منه قبل أي كتابة.', 'Pick the file and the partition; the app checks it before anything is written.')}</div>` : ''}
    ${f.check && !flashable ? whyButton('image') : ''}<button class="btn primary" data-act="flash" ${flashable ? '' : 'disabled'}><span>${f.base ? t(`فلّش ${LE(f.base)}${slot ? ' على ' + LE(slot) : ''}`, `Flash ${esc(f.base)}${slot ? ' to ' + esc(slot) : ''}`) : t('فلّش', 'Flash')}</span></button>
  </main>` + nav();
};

P.kernel = () => {
  const k = st.k, c = k.check;
  const chipTop = st.s.dev ? chip('warn', t('وضع المطور', 'Developer mode')) : chip('ok', 'AK3');
  const top = head(t('تفليش كيرنل', 'Flash a kernel'), t(`ملف ${L('AnyKernel3 zip')}`, 'An AnyKernel3 zip'), chipTop);
  const info = c ? `<div class="card"><h2>${t('محتوى الملف', 'Inside the zip')} ${c.check.items.some(i => i.code === 'NotAnyKernel' || i.code === 'UnsafeZipEntry') ? chip('bad', '✗') : chip('ok', 'AnyKernel3')}</h2>
    ${rows([c.kernel ? [t('الكيرنل', 'Kernel'), esc(c.kernel)] : null, c.kernelVersion ? [t('الإصدار', 'Version'), esc(c.kernelVersion + (c.kernelKmi ? ' · ' + c.kernelKmi : ''))] : null, c.devices && c.devices.length ? [t('الأجهزة', 'Devices'), esc(c.devices.join(', '))] : null,
      [t('القسم', 'Partition'), esc(c.block && c.block !== 'auto' ? c.block : 'boot (auto)')], c.versions ? [t('أندرويد', 'Android'), esc(c.versions)] : null,
      [t('فحص الجهاز', 'Device check'), c.deviceCheck ? t('مفعّل', 'On') : t('مقفل', 'Off'), A], ['SHA-256', esc(short(c.sha256))]])}</div>` : '';
  return top + `<main>
    ${fileCard('zip', k.file.name, t(`${LE(mib(k.file.size))} · ملف ZIP`, `${esc(mib(k.file.size))} · a zip`), chip('', t('تغيير', 'Change')), 'pickFile')}
    ${info}
    <div><div class="label">${t('بصمة المطور SHA-256 (اختياري)', 'Developer’s SHA-256 (optional)')}</div><input class="input" data-in="kexp" value="${esc(k.expected)}" placeholder="${t('الصق البصمة اللي نشرها المطور', 'Paste the hash the developer published')}" spellcheck="false" autocomplete="off"></div>
    ${k.checking ? spinner(t('يفحص الملف…', 'Checking the zip…')) : k.err ? errBanner(k.err) : c ? checkBanner(c.check, 'kernel') : ''}
    ${c && !c.check.canFlash ? whyButton('kernel') : ''}<button class="btn primary" data-act="flashKernel" ${c && c.check.canFlash ? '' : 'disabled'}><span>${t('فلّش الكيرنل', 'Flash the kernel')}</span></button>
    <button class="btn sm" data-act="kernelClose"><span>${t('رجوع لتفليش الصور', 'Back to flashing images')}</span></button>
    <div class="note">${t('التطبيق ياخذ نسخة من boot و init_boot و vendor_boot و dtbo قبل التثبيت (إذا مفعّلة في الإعدادات).', 'The app backs up boot, init_boot, vendor_boot and dtbo before installing (when enabled in Settings).')}</div>
  </main>` + nav();
};

// Progress and result of a flash, a kernel, an OTG flash or a backup.
P.running = () => {
  const r = st.run;
  const has = prefix => r.lines.some(l => l.startsWith(prefix));
  let steps = [];
  if (r.kind === 'image') {
    steps.push([t('تحقق من الملف', 'Check the file'), 'SHA-256', has('check ok')]);
    if (r.backupFirst) steps.push([t(`نسخة احتياطية من ${LE(r.partition)} الحالي`, `Back up the current ${esc(r.partition)}`), r.backup ? LE('Backups/…' + r.backup.slice(-16)) : '', has('backup ok')]);
    steps.push([t('الكتابة على القسم', 'Write the partition'), LE(mib(r.size)), has('verify ok') || has('done ')]);
    if (r.readBack) steps.push([t('قراءة القسم ومقارنته بالملف', 'Read it back and compare'), 'SHA-256', has('verify ok')]);
  } else if (r.kind === 'kernel') {
    if (r.backupFirst) steps.push([t('نسخة احتياطية من أقسام الإقلاع', 'Back up the boot partitions'), r.backup ? LE('Backups/…' + r.backup.slice(-16)) : '', has('backup ok')]);
    steps.push([t('تثبيت AnyKernel3', 'Run AnyKernel3'), '', has('done')]);
  } else if (r.kind === 'otg') {
    steps.push([t('تحقق من الملف', 'Check the file'), 'SHA-256', true]);
    steps.push([t(`إرسال ${LE(r.partition)}`, `Send ${esc(r.partition)}`), r.total ? L(`${esc(mib(r.sent))} / ${esc(mib(r.total))}`) : '', r.total > 0 && r.sent >= r.total]);
    steps.push([t('الكتابة (من البوت لودر)', 'Write (by the bootloader)'), '', r.state === 'done']);
  } else if (r.kind === 'backup') {
    const copied = r.lines.filter(l => l.startsWith('backup ') && !l.startsWith('backup ok')).map(l => l.slice(7));
    const plan = r.lines.find(l => l.startsWith('plan '));
    const failedParts = r.lines.filter(l => l.startsWith('failed ')).map(l => l.slice(7));
    steps.push([t('نسخ الأقسام', 'Copy the partitions') + (plan ? ' ' + L(`${copied.length} / ${esc(plan.slice(5))}`) : ''),
      LE(copied.slice(-6).join(', ')) + (failedParts.length ? `<br><span style="color:var(--warn)">${t('ما انقرت:', 'Could not copy:')} ${LE(failedParts.join(', '))}</span>` : ''), has('backup ok')]);
  }
  if (r.state !== 'running') steps = steps.map(s => [s[0], s[1], s[2] || r.state === 'done']);
  steps.push([t('تم', 'Done'), '', r.state === 'done']);
  let marked = false;
  const stepHtml = steps.map(([label, small, done], i) => {
    let b = 'wait', mark = i + 1, side = '';
    if (done) { b = 'ok'; mark = '✓'; side = chip('ok', '✓'); }
    else if (!marked) {
      marked = true;
      if (r.state === 'running') { b = 'run'; mark = ''; if (r.kind === 'otg' && r.total && r.sent < r.total) side = chip('', L(Math.floor(r.sent * 100 / r.total) + '%')); }
      else if (r.state === 'failed') { b = 'bad'; mark = '✗'; side = chip('bad', '✗'); }
    }
    return `<div class="step"><span class="b ${b}">${mark}</span><div>${label}${small ? `<small>${small}</small>` : ''}</div>${side}</div>`;
  }).join('');
  const total = steps.length, done = steps.filter(s => s[2]).length;
  const title = { image: t('التفليش', 'Flash'), kernel: t('تفليش كيرنل', 'Flash a kernel'), otg: t('إنقاذ OTG', 'OTG rescue'), backup: t('النسخ الاحتياطية', 'Backups') }[r.kind];
  const sub = r.state === 'running' ? (r.kind === 'backup' ? t('ياخذ نسخة', 'Backing up') : t(`يكتب ${LE(r.partition || 'boot')}`, `Writing ${esc(r.partition || 'boot')}`)) : r.state === 'done' ? t('تم', 'Done') : t('ما كمل', 'Stopped');
  const pct = r.kind === 'otg' && r.total && r.state === 'running' ? Math.floor(r.sent * 100 / r.total) : Math.round(done * 100 / total);
  const progress = `<div class="progress${r.state === 'running' && r.kind !== 'otg' ? ' busy' : ''}" style="margin-top:10px"><div style="width:${pct}%"></div></div>`;
  const log = r.kind === 'kernel' && r.lines.length ? `<div class="card"><h2>${t('مخرجات AnyKernel3', 'AnyKernel3 output')}</h2><div class="log">${r.lines.map(esc).join('\n')}</div></div>` : '';
  let body;
  if (r.state === 'running') {
    body = `<div class="card"><h2>${t('الخطوات', 'Steps')} ${chip('ok', L(`${done} / ${total}`))}</h2><div class="steps">${stepHtml}</div>${progress}</div>
      <div class="banner warn">${svg('warn')}<div class="body"><div class="big" style="font-size:14px">${r.kind === 'otg' ? t('لا تفصل الكيبل ولا تقفل التطبيق', 'Do not unplug the cable or close the app') : t('لا تطفي الجوال ولا تقفل التطبيق', 'Do not turn off the phone or close the app')}</div></div></div>${log}`;
  } else if (r.state === 'done') body = doneBody(r) + log;
  else body = failBody(r, stepHtml) + log;
  return head(title, sub, r.partition && r.kind !== 'backup' ? chip(r.state === 'failed' ? 'bad' : 'ok', LE(r.partition)) : '') + `<main>${body}</main>` + nav();
};

function doneBody(r) {
  if (r.kind === 'backup')
    return `<div class="card"><div class="big-ok"><div class="c">✓</div><h3>${t('انحفظت النسخة', 'Backup saved')}</h3><div class="note">${t('كل صورة ومعها بصمتها في SHA256SUMS.', 'Every image with its SHA-256 in SHA256SUMS.')}</div></div>
      ${rows([[t('المجلد', 'Folder'), esc(r.name || '')]])}</div><button class="btn primary" data-act="runClose"><span>${t('تم', 'Done')}</span></button>`;
  if (r.kind === 'otg')
    return `<div class="card"><div class="big-ok"><div class="c">✓</div><h3>${t(`انكتب ${LE(r.partition)} على الجوال الموصول`, `${esc(r.partition)} written to the connected phone`)}</h3>
      <div class="note">${t('البوت لودر رد بـ OKAY. وضع fastboot ما يسمح بقراءة القسم، فالتحقق كان من الملف قبل الإرسال.', 'The bootloader answered OKAY. Fastboot cannot read a partition back, so the file was verified before sending.')}</div></div>
      ${rows([[t('القسم', 'Partition'), esc(r.partition)], ['SHA-256', esc(short(r.sha256)) + ' ✓'], [t('الجوال', 'Phone'), esc(r.serial || '')]])}</div>
      <div class="row2"><button class="btn primary" data-act="otgReboot" data-v=""><span>${t('تشغيل الجوال', 'Start the phone')}</span></button><button class="btn" data-act="runClose"><span>${t('تفليش ثاني', 'Flash another')}</span></button></div>`;
  if (r.kind === 'kernel') {
    const changed = r.outcome && r.outcome.changed;
    return `<div class="card"><div class="big-ok"><div class="c${changed ? '' : ' warn'}">${changed ? '✓' : '!'}</div><h3>${changed ? t('انثبّت الكيرنل', 'The kernel is installed') : t('AnyKernel3 خلص، بس القسم ما تغيّر', 'AnyKernel3 finished, but the partition did not change')}</h3>
      <div class="note">${changed ? t('القسم تغيّر بعد التثبيت. شغّل الجوال من جديد عشان يشتغل الكيرنل.', 'The partition changed. Restart the phone to run the new kernel.') : t('شوف مخرجات AnyKernel3 تحت: ممكن رفض الملف.', 'See the AnyKernel3 output below: it may have refused the zip.')}</div></div>
      ${r.backup ? rows([[t('النسخة القديمة', 'Previous copy'), esc('Backups/…' + r.backup.slice(-16))]]) : ''}</div>
      <button class="btn primary" data-act="reboot" data-v=""><span>${t('إعادة التشغيل الحين', 'Restart now')}</span></button>
      ${r.backup ? `<button class="btn" data-act="restoreFrom" data-v="${esc(r.backup)}">${svg('restore')}<span>${t('رجّع النسخة القديمة', 'Put the previous copy back')}</span></button>` : ''}
      <button class="btn sm" data-act="runClose"><span>${t('رجوع', 'Back')}</span></button>`;
  }
  const verified = r.outcome && r.outcome.verified;
  return `<div class="card"><div class="big-ok"><div class="c">✓</div><h3>${verified ? t(`انكتب ${LE(r.partition)} وتطابق`, `${esc(r.partition)} written and verified`) : t(`انكتب ${LE(r.partition)}`, `${esc(r.partition)} written`)}</h3>
    <div class="note">${verified ? t('قرينا القسم بعد الكتابة وبصمته نفس بصمة الملف.', 'The partition was read back and its SHA-256 equals the file’s.') : t('القراءة بعد الكتابة مقفلة في الإعدادات.', 'Reading back is off in Settings.')}</div></div>
    ${rows([[t('القسم', 'Partition'), esc(r.partition)], ['SHA-256', esc(short(r.sha256)) + (verified ? ' ✓' : '')], r.backup ? [t('النسخة القديمة', 'Previous copy'), esc('Backups/…' + r.backup.slice(-16))] : null])}</div>
    <button class="btn primary" data-act="reboot" data-v=""><span>${t('إعادة التشغيل الحين', 'Restart now')}</span></button>
    ${r.backup ? `<button class="btn" data-act="restoreFrom" data-v="${esc(r.backup)}" data-file="${esc(r.partition)}.img">${svg('restore')}<span>${t('رجّع النسخة القديمة', 'Put the previous copy back')}</span></button>` : ''}
    <div class="note">${t('إذا الجوال ما قلع: رجّع النسخة القديمة من برنامج الويندوز، أو من جوال ثاني بقائمة OTG.', 'If the phone does not start: put the previous copy back from the Windows app, or from another phone with the OTG menu.')}</div>
    <button class="btn sm" data-act="runClose"><span>${t('رجوع', 'Back')}</span></button>`;
}

function failBody(r, stepHtml) {
  const written = r.outcome && r.outcome.written;
  const note = r.kind === 'otg' ? t('الجوال الموصول ما زال في fastboot. تقدر تحاول مرة ثانية.', 'The connected phone is still in fastboot; you can try again.')
    : written ? t('القسم انكتب لكن ما تطابق. لا تعيد التشغيل: رجّع النسخة القديمة الحين.', 'The partition was written but does not match. Do not restart: put the previous copy back now.')
    : r.kind === 'backup' ? t('ما انحفظ شي ناقص.', 'Nothing half-written was kept.')
    : t('ما انكتب شي على القسم، والجوال مثل ما كان.', 'Nothing was written; the phone is as it was.');
  return `<div class="banner bad">${svg('warn')}<div class="body"><div class="big">${errText(r.error)}</div><div class="note">${note}</div></div></div>
    <div class="card"><div class="steps">${stepHtml}</div></div>
    ${written && r.backup ? `<button class="btn primary" data-act="restoreFrom" data-v="${esc(r.backup)}" data-file="${esc(r.partition)}.img">${svg('restore')}<span>${t('رجّع النسخة القديمة', 'Put the previous copy back')}</span></button>` : ''}
    <button class="btn" data-act="runClose"><span>${t('رجوع', 'Back')}</span></button>`;
}

P.backup = () => {
  if (st.run && st.run.where === 'backup') return P.running();
  const p = st.phone;
  const list = st.backups;
  const top = head(t('النسخ الاحتياطية', 'Backups'), LE(st.backupRoot), list ? chip('ok', A ? `${list.length} ${list.length === 1 ? 'نسخة' : 'نسخ'}` : `${list.length} set${list.length === 1 ? '' : 's'}`) : '');
  if (!p || !p.root) return top + `<main>${p ? noRoot() : spinner(t('يقرا الجوال…', 'Reading the phone…'))}</main>` + nav();
  let items;
  if (!list) items = spinner(t('يقرا النسخ…', 'Reading the backups…'));
  else if (!list.length) items = `<div class="card"><div class="empty"><div class="c">${svg('backup')}</div><h3>${t('ما فيه نسخ للحين', 'No backups yet')}</h3><div class="note">${t('خذ نسخة قبل أي تفليش. نفس ترتيب برنامج الويندوز والإضافة.', 'Take one before any flash. The same layout as the Windows app and the Companion.')}</div></div></div>`;
  else items = `<div class="card">${list.map(b => {
    const s = st.bk[b.name];
    const side = !b.mine ? chip('warn', t('جوال ثاني', 'Other phone'))
      : s === 'run' ? '<span class="spin"></span>' : s === 'ok' ? chip('ok', '✓ ' + t('مطابقة', 'Verified'))
      : s && s.failed ? chip('bad', '✗ ' + (A ? `${s.failed.length} ${s.failed.length === 1 ? 'ملف متغيّر' : 'ملفات متغيّرة'}` : `${s.failed.length} changed`)) : chip('', t('ما تحققت', 'Not checked'));
    const meta = b.mine ? `${imagesCount(b.files.length)} · ${LE(mib(b.kb * 1024))} · ${setLabel(b.set)}${b.mode === 'companion' ? ' · ' + t('من الإضافة', 'Companion') : ''}` : t(`${esc(b.model || '?')} · ما تنفلش هنا`, `${esc(b.model || '?')} · not flashable here`);
    return `<button class="bk${st.bkOpen === b.name ? ' open' : ''}" data-act="bkOpen" data-v="${esc(b.name)}"><div class="n">${esc(b.name)}</div><div class="s">${side}</div><div class="m">${meta}</div></button>`;
  }).join('')}</div>`;
  const open = list && list.find(b => b.name === st.bkOpen);
  let detail = '';
  if (open) {
    const s = st.bk[open.name];
    const failed = s && s.failed ? s.failed : [];
    detail = `<div class="card"><h2>${t('محتوى النسخة', 'Inside the backup')} ${chip('', esc(backupDate(open.name)))}</h2>
      ${rows(open.files.map(f => [LE(f.name.replace(/\.img$/, '')), esc(short(f.sha256)) + (failed.includes(f.name) ? ' ✗' : s === 'ok' ? ' ✓' : '')]))}
      ${open.mine ? '' : `<div class="note" style="margin-top:8px">${t(`النسخة لـ ${esc(open.model || '?')} · ${esc(open.serial || '?')}: تنفلش على جوالها بس (من OTG أو الكمبيوتر).`, `This backup is ${esc(open.model || '?')} · ${esc(open.serial || '?')}’s: it can only go back to that phone (OTG or a computer).`)}</div>`}
      <div class="${open.mine ? 'row3' : 'row2'}" style="margin-top:10px">${open.mine ? `<button class="btn sm" data-act="bkVerify" data-v="${esc(open.name)}"><span>${t('تحقق', 'Verify')}</span></button><button class="btn sm" data-act="bkRestore" data-v="${esc(open.name)}"><span>${t('استرجاع', 'Restore')}</span></button>` : ''}<button class="btn sm danger" data-act="bkDelete" data-v="${esc(open.name)}"><span>${t('حذف', 'Delete')}</span></button></div></div>`;
  }
  return top + `<main>
    <div class="row2"><button class="btn primary" data-act="bkNew"><span>${t('نسخة جديدة', 'New backup')}</span></button><button class="btn" data-act="bkVerifyAll" ${list && list.some(b => b.mine) ? '' : 'disabled'}><span>${t('تحقق من الكل', 'Verify all')}</span></button></div>
    ${items}${detail}
  </main>` + nav();
};

P.otg = () => {
  if (st.run && st.run.where === 'otg') return P.running();
  const o = st.o, p = o.phone;
  const top = head(t('إنقاذ OTG', 'OTG rescue'), t('فلّش جوال ثاني موصول بكيبل OTG', 'Flash another phone over an OTG cable'),
    p ? chip('ok', 'USB') : o.devices.length ? chip('info', t('موصول', 'Plugged in')) : chip('', t('ما فيه جهاز', 'No device')));
  if (!p) {
    const devs = o.devices.length ? `<div class="card list">${o.devices.map(d => `<button class="it" data-act="otgConnect" data-v="${esc(d.id)}"><div class="ic">${svg('otg')}</div><div><b>${esc(d.name || t('جوال في وضع fastboot', 'A phone in fastboot'))}</b><small class="ltr">${esc([d.maker, d.vid + ':' + d.pid].filter(Boolean).join(' · '))}</small></div>${o.connecting === d.id ? '<span class="spin"></span>' : chip('ok', t('اتصل', 'Connect'))}</button>`).join('')}</div>
      ${o.connecting ? `<div class="note">${t('وافق على طلب صلاحية USB اللي يطلع لك.', 'Allow the USB permission request.')}</div>` : ''}`
      : `<div class="card"><div class="empty"><div class="c">${svg('otg')}</div><h3>${t('وصّل الجوال الثاني', 'Connect the other phone')}</h3><div class="note">${t('التطبيق ينتظر جوال في وضع fastboot.', 'Waiting for a phone in fastboot.')}</div></div></div>`;
    return top + `<main>${o.err ? errBanner(o.err) : ''}${devs}
      <div class="card"><h2>${t('الطريقة', 'How to')}</h2><ol class="how">
        <li>${t('طفّ الجوال الثاني، وشغّله على fastboot (غالباً: زر الصوت تحت + التشغيل).', 'Turn the other phone off and start it in fastboot (usually volume down + power).')}</li>
        <li>${t('وصّل كيبل OTG بجوالك، وكيبل USB بالجوال الثاني.', 'Plug the OTG adapter into your phone and the USB cable into the other phone.')}</li>
        <li>${t('وافق على طلب الصلاحية اللي يطلع لك.', 'Allow the USB permission request.')}</li></ol></div>
      <div class="banner info">${svg('info')}<div class="body note">${t('قائمة OTG ما تحتاج روت على جوالك. والكتابة تكون على الجوال الموصول برقمه بس. تفليش ZIP من الريكفري (sideload) بيجي بعدين.', 'The OTG menu needs no root on your phone, and writes only to the connected phone, by its serial. Sideloading a zip in recovery comes later.')}</div></div>
    </main>` + nav();
  }
  const slot = p.slot ? (o.other ? (p.slot === '_a' ? '_b' : '_a') : p.slot) : '';
  let src = '';
  if (o.src) {
    const meta = o.src.type === 'folder' ? t(`من نسخة ${esc(o.folder ? o.folder.name : '')}`, `From the ${esc(o.folder ? o.folder.name : '')} backup`) : t(`ملف · ${LE(mib(o.src.size))}`, `A file · ${esc(mib(o.src.size))}`);
    src = fileCard('file', o.src.file || o.src.name, meta, '', '');
  }
  const folderFiles = o.folder && o.src && o.src.type === 'folder' ? '' : o.folder && !o.src ? `<div class="card"><h2>${t('اختر صورة من النسخة', 'Pick an image from the backup')} ${chip(o.folder.serial === p.serial ? 'ok' : o.folder.serial ? 'bad' : 'info', esc(o.folder.serial || t('بدون رقم', 'no serial')))}</h2><div class="files">${o.folder.files.filter(f => f.partition && ALLOWED.includes(baseOf(f.partition))).map(f => `<button class="file" data-act="otgFile" data-v="${esc(f.name)}"><div class="ico">${svg('file')}</div><div style="flex:1;min-width:0"><div class="n">${esc(f.name)}</div><div class="m">${f.listed ? 'SHA256SUMS ✓' : t('مو في SHA256SUMS', 'not in SHA256SUMS')}</div></div></button>`).join('') || `<div class="note">${t('ما فيها صور للأقسام المسموحة', 'No images for the allowed partitions')}</div>`}</div></div>` : '';
  const flashable = o.check && o.check.check.canFlash && !o.check.tooBigForDownload;
  return top + `<main>
    <div class="card"><div class="usb"><div class="ph">${svg('otg')}</div><div style="flex:1;min-width:0"><div style="font-weight:600">${p.userspace ? t('جوال في وضع fastbootd', 'A phone in fastbootd') : t('جوال في وضع fastboot', 'A phone in fastboot')}</div><div class="note">${LE(p.serial + (p.product ? ' · ' + p.product : ''))}</div></div><button class="chip ok" data-act="otgClose">${t('افصل', 'Disconnect')}</button></div></div>
    <div class="card"><h2>${t('الجوال الموصول', 'The connected phone')} ${chip('', p.userspace ? 'fastbootd' : 'fastboot')}</h2>
    ${rows([[t('المنتج', 'Product'), esc(p.product || '?')], [t('البوت لودر', 'Bootloader'), p.unlocked === 'yes' ? t('مفتوح', 'Unlocked') : p.unlocked === 'no' ? t('مقفل', 'Locked') : t('غير معروف', 'Unknown'), A],
      [t('السلوت', 'Slot'), esc(p.slot || t('بدون', 'none')), !p.slot && A], p.maxDownload ? ['max-download', esc(mib(p.maxDownload))] : null])}</div>
    ${p.unlocked === 'no' ? `<div class="banner warn">${svg('lock')}<div class="body note">${t('البوت لودر مقفل: غالباً بيرفض التفليش.', 'The bootloader is locked: it will most likely refuse to flash.')}</div></div>` : ''}
    ${o.err ? errBanner(o.err) : ''}
    <div><div class="label">${t('١. الملف', '1. The file')}</div>${src}
      <div class="row2" style="margin-top:${src ? 8 : 0}px"><button class="btn sm" data-act="otgFolder"><span>${t('من مجلد نسخة', 'From a backup folder')}</span></button><button class="btn sm" data-act="otgPickFile"><span>${t('ملف IMG', 'IMG file')}</span></button></div></div>
    ${folderFiles}
    <div><div class="label">${t('٢. القسم', '2. Partition')}</div>
      <div class="parts">${ALLOWED.map(b => `<button class="${o.base === b ? 'on' : ''}" data-act="otgBase" data-v="${b}" ${(p.targets || []).includes(b) ? '' : 'disabled'}>${b}</button>`).join('')}</div></div>
    ${p.slot ? `<div><div class="label">${t('٣. السلوت', '3. Slot')}</div><div class="seg"><button class="${o.other ? '' : 'on'}" data-act="otgSlot" data-v="0"><b>${t('الحالي', 'Current')} ${LE(p.slot)}</b></button><button class="${o.other ? 'on' : ''}" data-act="otgSlot" data-v="1"><b>${t('الثاني', 'Other')} ${LE(p.slot === '_a' ? '_b' : '_a')}</b></button></div></div>` : ''}
    ${o.src && o.src.type === 'file' ? `<div><div class="label">${t('بصمة المطور SHA-256 (اختياري)', 'Developer’s SHA-256 (optional)')}</div><input class="input" data-in="oexp" value="${esc(o.expected)}" placeholder="${t('الصق البصمة اللي نشرها المطور', 'Paste the hash the developer published')}" spellcheck="false" autocomplete="off"></div>` : ''}
    ${o.checking ? spinner(t('يتحقق من الملف…', 'Checking the file…')) : o.check ? checkBanner(o.check.check, 'otg') + (o.check.tooBigForDownload ? errBanner('too-big-for-download') : '') : ''}
    ${o.check && !o.check.check.canFlash ? whyButton('otg') : ''}<button class="btn primary" data-act="otgFlash" ${flashable ? '' : 'disabled'}><span>${o.base ? t(`أرسل وفلّش ${LE(o.base)}${slot ? ' على ' + LE(slot) : ''}`, `Send and flash ${esc(o.base)}${slot ? ' to ' + esc(slot) : ''}`) : t('أرسل وفلّش', 'Send and flash')}</span></button>
    <div class="label">${t('تشغيل الجوال الموصول', 'Restart the connected phone')}</div>
    <div class="row3"><button class="btn sm" data-act="otgReboot" data-v=""><span>${t('النظام', 'System')}</span></button><button class="btn sm" data-act="otgReboot" data-v="bootloader"><span>bootloader</span></button><button class="btn sm" data-act="otgReboot" data-v="recovery"><span>recovery</span></button></div>
    <div class="note">${t('يكتب على هذا الجوال الموصول برقمه بس، وما يلمس أي جهاز ثاني.', 'Writes to this connected phone by its serial only, never to another device.')}</div>
  </main>` + nav();
};

P.more = () => {
  if (st.page) return P[st.page]();
  const items = [['settings', 'gear', t('الإعدادات', 'Settings'), t('المظهر واللغة ووضع المطور', 'Appearance, language, developer mode')],
    ['logs', 'log', t('السجل', 'Logs'), t('كل أمر تنفذ وكل نتيجة', 'Every command run and its result')],
    ['link:https://github.com/ifoknr/FastbootStudio', 'pc', t('برنامج الويندوز', 'The Windows app'), L('Fastboot Studio')],
    ['link:https://t.me/BeNeXTBrO', 'tg', t('قروب التيليجرام', 'Telegram group'), L('@BeNeXTBrO')],
    ['link:https://github.com/ifoknr', 'gh', t('السورس', 'Source'), L('github.com/ifoknr')],
    ['about', 'about', t('حول', 'About'), L('v' + esc(st.version))]];
  return head(t('المزيد', 'More'), 'Fastboot Studio Mobile') + `<main><div class="card list">
    ${items.map(([to, i, b, s]) => `<button class="it" data-act="${to.startsWith('link:') ? 'link' : 'page'}" data-v="${to.replace(/^link:/, '')}"><div class="ic">${svg(i)}</div><div><b>${b}</b><small>${s}</small></div><span class="chev">${A ? '‹' : '›'}</span></button>`).join('')}
  </div></main>` + nav();
};

P.settings = () => {
  const s = st.s, seg = (key, opts) => `<div class="seg">${opts.map(([v, b, small]) => `<button class="${s[key] === v ? 'on' : ''}" data-act="set" data-k="${key}" data-v="${v}"><b>${b}</b>${small || ''}</button>`).join('')}</div>`;
  const tog = (key, b, small) => `<button class="setrow" data-act="toggle" data-k="${key}"><div class="tx"><b>${b}</b><small>${small}</small></div><span class="toggle${s[key] ? ' on' : ''}"></span></button>`;
  return head(t('الإعدادات', 'Settings'), t('المظهر واللغة والأمان', 'Appearance, language and safety'), '', true) + `<main>
    <div class="card"><h2>${t('المظهر', 'Appearance')}</h2>
      ${seg('theme', [['auto', t('تلقائي', 'Auto'), t('مثل الجوال', 'Like the phone')], ['dark', t('ليلي', 'Night'), t('داكن', 'Dark')], ['light', t('نهاري', 'Day'), t('فاتح', 'Light')]])}
      <div class="label" style="margin-top:12px">${t('اللغة', 'Language')}</div>
      ${seg('lang', [['ar', 'العربية'], ['en', 'English']])}</div>
    <div class="card"><h2>${t('الأمان', 'Safety')}</h2>
      ${tog('backupFirst', t('نسخة قبل كل تفليش', 'Back up before every flash'), t('ياخذ نسخة من القسم قبل الكتابة', 'Copies the partition before writing'))}
      ${tog('readBack', t('قراءة بعد الكتابة', 'Read back after writing'), t('يقارن القسم بالملف بعد التفليش', 'Compares the partition with the file'))}
      <div class="setrow"><div class="tx"><b>${t('أقل بطارية للتفليش', 'Lowest battery to flash')}</b></div></div>
      ${seg('minBattery', [[30, L('30%')], [50, L('50%')], [70, L('70%')]])}</div>
    <div class="card" style="border-color:var(--wline)"><h2>${t('وضع المطور', 'Developer mode')} <button class="toggle warn${s.dev ? ' on' : ''}" data-act="toggle" data-k="dev"></button></h2>
      <div class="note" style="color:var(--text)">${t('يسمح بملفات مكتوبة لجهاز ثاني أو أندرويد ثاني أو كيرنل ثاني، بعد ما تكتب اسم القسم.', 'Allows files made for another device, Android version or kernel, once you type the partition name.')}</div>
      <div class="note" style="margin-top:6px;color:var(--danger)">${t('الملفات الخبيثة أو التالفة أو نسخ جوال ثاني تبقى ممنوعة دايماً.', 'Harmful or damaged files, and other phones’ backups, are always refused.')}</div></div>
  </main>` + nav();
};

P.logs = () => {
  const cls = l => /error|failed|exit [1-9]/.test(l) ? 'e' : /\bblock\b|Block /.test(l) ? 'w' : /\bok\b|done|Ok /.test(l) ? 'ok' : '';
  const lines = st.logs.map(l => { const m = /^(\d\d:\d\d:\d\d) (.*)$/.exec(l); return m ? `<span class="t">${m[1]}</span> <span class="${cls(m[2])}">${esc(m[2])}</span>` : esc(l); });
  return head(t('السجل', 'Logs'), t('كل أمر تنفذ وكل نتيجة', 'Every command run and its result'), `<button class="chip" data-act="copyLogs">${t('نسخ', 'Copy')}</button>`, true) + `<main>
    <div class="card"><div class="log">${lines.join('\n') || t('السجل فاضي', 'The log is empty')}</div></div>
    <button class="btn sm" data-act="logs">${svg('refresh')}<span>${t('تحديث', 'Refresh')}</span></button></main>` + nav();
};

P.about = () => head(t('حول', 'About'), 'Fastboot Studio Mobile', '', true) + `<main>
  <div class="card" style="text-align:center"><div class="big-ok"><img src="icon.png" alt="" style="width:72px;height:72px;border-radius:18px"><h3>Fastboot Studio Mobile</h3><div class="note ltr">v${esc(st.version)} · MIT</div></div>
    <div class="note">${t('تطوير IFOKNR. مبني على مكتبة Fastboot Studio، المبنية على Fastboot Enhance من LibXZR.', 'By IFOKNR. Built on the Fastboot Studio library, which is based on Fastboot Enhance by LibXZR.')}</div></div>
  <div class="card list">
    ${[['https://t.me/IFOKNR1', 'tg', t('تواصل', 'Contact'), '@IFOKNR1'], ['https://github.com/ifoknr', 'gh', 'GitHub', 'ifoknr'], ['https://github.com/ifoknr/FastbootStudio', 'pc', 'Fastboot Studio (Windows)', 'github.com/ifoknr/FastbootStudio']].map(([u, i, b, s]) => `<button class="it" data-act="link" data-v="${u}"><div class="ic">${svg(i)}</div><div><b>${b}</b><small class="ltr">${s}</small></div><span class="chev">${A ? '‹' : '›'}</span></button>`).join('')}
  </div>
  <div class="note">${t('التطبيق يقرا ويكتب على الجوال بس، وما يرسل أي بيانات لأي جهة. الخطوط Roboto و Noto Kufi Arabic برخصة OFL.', 'The app only reads and writes the phone; it sends no data anywhere. Fonts: Roboto and Noto Kufi Arabic, under the OFL.')}</div>
</main>` + nav();

// ---------------------------------------------------------------- sheets

const S = {};

S.confirm = sh => {
  const c = sh.check.check, typed = sh.check.typed, ctx = sh.ctx;
  const go = { image: 'doFlash', kernel: 'doKernel', otg: 'doOtg' }[ctx];
  if (c.verdict === 'block') {
    const over = c.items.some(i => i.level === 'block' && i.override);
    const hard = c.items.some(i => i.level === 'block' && !i.override);
    return sheet('danger', `<div class="banner bad">${svg('warn')}<div class="body"><div class="big">${t('ممنوع: الملف ما يناسب', 'Refused: this file does not fit')}</div><div class="note">${t('تفليشه ممكن يخلي الجوال ما يقلع.', 'Flashing it may leave the phone unable to start.')}</div></div></div>
      <div class="card" style="padding:10px 14px">${checkRows(c, ctx, true)}</div>
      <div class="note" style="color:var(--text)">${hard ? t('هذا الرفض ما يتغيّر حتى في وضع المطور، لأن الملف تالف أو خطير أو لجوال ثاني أو لقسم ثاني.', 'This cannot be overridden, not even in developer mode: the file is damaged, harmful, another phone’s or another partition’s.')
        : over ? t('الملف مكتوب لجهاز أو أندرويد أو كيرنل ثاني. وضع المطور في الإعدادات يسمح فيه على مسؤوليتك.', 'The file is made for another device, Android version or kernel. Developer mode in Settings allows it at your own risk.') : ''}</div>
      <button class="btn primary" data-act="sheetClose"><span>${t('رجوع واختيار ملف ثاني', 'Back, pick another file')}</span></button>`);
  }
  if (c.verdict === 'warn') {
    const dev = c.items.some(i => i.override && i.level === 'warn');
    return sheet('warn', `<div class="banner warn">${svg('warn')}<div class="body"><div class="big">${dev ? t('وضع المطور: الملف مكتوب لجهاز ثاني', 'Developer mode: made for another device') : t('انتبه: ما نقدر نتأكد من الملف', 'Careful: this file cannot be confirmed')}</div>
      <div class="note">${dev ? t('عادةً هذا ممنوع. تقدر تكمل على مسؤوليتك، والنسخة الاحتياطية ترجّع القسم.', 'Normally refused. You may go ahead at your own risk; the backup puts the partition back.') : t('ما فيه بصمة تثبت إنه الملف الصحيح لجوالك، فلازم تأكيدك.', 'Nothing proves it is the right file for your phone, so it needs your confirmation.')}</div></div></div>
      <div class="card" style="padding:10px 14px">${checkRows(c, ctx)}</div>
      <div class="note">${t(`للمتابعة اكتب اسم القسم ${LE(typed)}:`, `To go ahead, type the partition name ${esc(typed)}:`)}</div>
      <input class="typed" data-in="typed" autocomplete="off" autocapitalize="off" spellcheck="false" placeholder="${esc(typed)}">
      <div class="row2"><button class="btn" data-act="sheetClose"><span>${t('إلغاء', 'Cancel')}</span></button><button class="btn warnb" id="typedGo" data-act="${go}" disabled><span>${dev ? t('فلّش على مسؤوليتي', 'Flash at my own risk') : t('فلّش', 'Flash')}</span></button></div>`);
  }
  return sheet('notice', `<div class="banner info">${svg('info')}<div class="body"><div class="big">${t('تنبيه خفيف', 'Worth knowing')}</div><div class="note">${t('الملف سليم، بس فيه شي مختلف عن اللي شغّال الحين. أنت تختار.', 'The file is fine, but something differs from what runs now. Your call.')}</div></div></div>
    <div class="card" style="padding:10px 14px">${checkRows(c, ctx)}</div>
    <div class="row2"><button class="btn" data-act="sheetClose"><span>${t('إلغاء', 'Cancel')}</span></button><button class="btn primary" data-act="${go}" data-notice="1"><span>${t('أكمل', 'Continue')}</span></button></div>`);
};

S.pickBackup = () => {
  const mine = (st.backups || []).filter(b => b.mine);
  const body = !st.backups ? spinner(t('يقرا النسخ…', 'Reading the backups…')) : !mine.length
    ? `<div class="note">${t('ما فيه نسخ لهذا الجوال.', 'No backups of this phone.')}</div>`
    : mine.filter(b => flashableFiles(b).length).map(b => `<div class="label">${esc(backupDate(b.name))} · ${setLabel(b.set)}</div><div class="files">${flashableFiles(b).map(f =>
        `<button class="file" data-act="useBackup" data-v="${esc(b.name)}" data-file="${esc(f.name)}"><div class="ico">${svg('file')}</div><div style="flex:1;min-width:0"><div class="n">${esc(f.name)}</div><div class="m ltr">${esc(short(f.sha256))}</div></div></button>`).join('')}</div>`).join('');
  return sheet('line', `<h3>${t('اختر صورة من نسخة', 'Pick an image from a backup')}</h3>${body}<button class="btn" data-act="sheetClose"><span>${t('إلغاء', 'Cancel')}</span></button>`);
};

S.restore = sh => {
  const b = (st.backups || []).find(x => x.name === sh.name);
  if (!b) return '';
  return sheet('line', `<h3>${t('استرجاع من النسخة', 'Restore from the backup')}</h3><div class="note">${t('اختر الصورة. التطبيق يتحقق منها ثم تفلّشها من صفحة التفليش.', 'Pick the image. The app checks it, then you flash it from the Flash page.')}</div>
    <div class="files">${flashableFiles(b).map(f => `<button class="file" data-act="useBackup" data-v="${esc(b.name)}" data-file="${esc(f.name)}"><div class="ico">${svg('restore')}</div><div style="flex:1;min-width:0"><div class="n">${esc(f.name)}</div><div class="m ltr">${esc(short(f.sha256))}</div></div></button>`).join('')}</div>
    ${flashableFiles(b).length < b.files.length ? `<div class="note">${t('باقي صور النسخة (البوت لودر والمودم وغيرها) ما تنكتب من الجوال: ترجع من برنامج الويندوز بوضع fastboot.', 'The backup’s other images (bootloader chain, modem...) are not written from the phone: they go back from the Windows app, in fastboot.')}</div>` : ''}
    <button class="btn" data-act="sheetClose"><span>${t('إلغاء', 'Cancel')}</span></button>`);
};

S.newBackup = () => {
  const p = st.phone || {};
  return sheet('accent', `<h3>${t('نسخة جديدة', 'New backup')}</h3><div class="note">${t(`ينسخ أقسام الإقلاع (${L((p.targets || []).join(', '))}) إلى ${LE(st.backupRoot)}`, `Copies the boot partitions (${esc((p.targets || []).join(', '))}) to ${esc(st.backupRoot)}`)}</div>
    <button class="btn primary" data-act="doBackup" data-v="0"><span>${p.ab ? t(`السلوت الحالي ${LE(p.slot)}`, `The current slot ${esc(p.slot)}`) : t('ابدأ', 'Start')}</span></button>
    ${p.ab ? `<button class="btn" data-act="doBackup" data-v="1"><span>${t('السلوتين a و b', 'Both slots, a and b')}</span></button>` : ''}
    ${st.comp && st.comp.usable ? `<div class="label" style="margin-top:4px">${t('من الإضافة (Companion)', 'With the Companion module')}</div>
    <button class="btn" data-act="compBackup" data-v="critical"><span>${t('حرجة: البوت لودر والمودم و persist', 'Critical: bootloader chain, modem, persist')}</span></button>
    <button class="btn" data-act="compBackup" data-v="full"><span>${t('كاملة: كل شي إلا userdata', 'Full: everything but userdata')}</span></button>
    <div class="note">${t('النسخة الكاملة فيها super، فتاخذ عدة جيجا. الإضافة تتأكد من المساحة قبل ما تبدأ.', 'The full backup includes super, so it takes several GB; the module checks the free space first.')}</div>` : ''}
    <button class="btn sm" data-act="sheetClose"><span>${t('إلغاء', 'Cancel')}</span></button>`);
};

S.deleteBackup = sh => sheet('danger', `<h3>${t('حذف النسخة؟', 'Delete the backup?')}</h3><div class="note ltr">${esc(sh.name)}</div>
  <div class="note" style="color:var(--text)">${t('الحذف نهائي. إذا هذي آخر نسخة لجوالك، خذ نسخة جديدة قبل أي تفليش.', 'This cannot be undone. If it is your phone’s last backup, take a new one before flashing.')}</div>
  <div class="row2"><button class="btn" data-act="sheetClose"><span>${t('إلغاء', 'Cancel')}</span></button><button class="btn danger" data-act="doDelete" data-v="${esc(sh.name)}"><span>${t('حذف', 'Delete')}</span></button></div>`);

S.dev = () => sheet('warn', `<div class="banner warn">${svg('warn')}<div class="body"><div class="big">${t('تشغيل وضع المطور؟', 'Turn on developer mode?')}</div>
  <div class="note">${t('يسمح بملفات مكتوبة لجهاز ثاني أو أندرويد ثاني بعد ما تكتب اسم القسم. ممكن الجوال ما يقلع، والنسخة الاحتياطية ترجّعه.', 'Files made for another device or Android version are allowed once you type the partition name. The phone may not start; the backup puts it back.')}</div></div></div>
  <div class="note" style="color:var(--danger)">${t('الملفات الخبيثة أو التالفة أو نسخ جوال ثاني تبقى ممنوعة.', 'Harmful or damaged files, and other phones’ backups, stay refused.')}</div>
  <div class="row2"><button class="btn" data-act="sheetClose"><span>${t('إلغاء', 'Cancel')}</span></button><button class="btn warnb" data-act="devOn"><span>${t('شغّل', 'Turn on')}</span></button></div>`);

S.message = sh => sheet(sh.color || 'line', `<div class="banner ${sh.kind || 'bad'}">${svg('warn')}<div class="body"><div class="big" style="font-size:14px">${sh.html}</div></div></div><button class="btn" data-act="sheetClose"><span>${t('تمام', 'OK')}</span></button>`);

// ---------------------------------------------------------------- render

const root = document.getElementById('root');
let lastView = '';
function render() {
  if (!st.ready) return;
  const view = st.tab + '/' + (st.page || '');
  const main = root.querySelector('main');
  const scroll = main && view === lastView ? main.scrollTop : 0;
  const sheetEl = root.querySelector('.sheet');
  const sheetScroll = sheetEl ? sheetEl.scrollTop : 0;
  const typedEl = root.querySelector('[data-in=typed]');
  const typed = typedEl ? typedEl.value : null, typedFocus = typedEl && document.activeElement === typedEl;
  root.innerHTML = P[st.tab]() + (st.sheet ? S[st.sheet.type](st.sheet) : '') + (st.toast ? `<div class="toast">${st.toast}</div>` : '');
  const m = root.querySelector('main');
  if (m) m.scrollTop = scroll;
  const sh = root.querySelector('.sheet');
  if (sh) sh.scrollTop = sheetScroll;
  // A re-render (a toast, a step) keeps what was typed on the warning sheet.
  const typedNew = root.querySelector('[data-in=typed]');
  if (typedNew && typed != null) {
    typedNew.value = typed;
    const go = root.querySelector('#typedGo');
    if (go) go.disabled = typed.trim() !== st.sheet.check.typed;
    if (typedFocus) typedNew.focus();
  }
  lastView = view;
}

function applyLook() {
  A = st.s.lang !== 'en';
  document.documentElement.lang = A ? 'ar' : 'en';
  document.documentElement.dir = A ? 'rtl' : 'ltr';
  const dark = st.s.theme === 'auto' ? st.systemDark : st.s.theme === 'dark';
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  call('ui.bars', { dark }).catch(() => {});
}

let toastTimer = 0;
function toast(html) {
  st.toast = html;
  render();
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { st.toast = null; render(); }, 3200);
}
const fail = e => toast(errText(e && e.message));

// ---------------------------------------------------------------- actions

async function refresh() {
  st.refreshing = true; render();
  try { st.phone = await call('device.refresh'); st.phoneErr = null; }
  catch (e) { st.phoneErr = e.message; }
  st.refreshing = false;
  render();
  if (st.phone && st.phone.root) { loadBackups(); loadCompanion(); }
  else st.comp = null;
}

async function loadCompanion() {
  try { st.comp = await call('companion.find'); } catch (e) { st.comp = null; }
  render();
}

// Backups of the partitions the app may write: a critical backup of the Companion has none.
const flashableFiles = b => b.files.filter(f => f.partition && ALLOWED.includes(baseOf(f.partition)));

async function loadBackups() {
  try { st.backups = await call('backup.list'); } catch (e) { st.backups = []; fail(e); }
  render();
}

async function checkImage() {
  const f = st.f;
  f.check = null; f.err = null;
  if (!f.src || !f.base) { render(); return; }
  f.checking = true; render();
  const args = f.src.type === 'backup' ? { source: 'backup', name: f.src.name, file: f.src.file } : { source: 'file', token: f.src.token, expected: f.expected };
  try { f.check = await call('flash.check', Object.assign(args, { base: f.base, other: f.other })); }
  catch (e) { f.err = e.message; }
  f.checking = false;
  render();
}

async function checkKernel() {
  const k = st.k;
  k.check = null; k.err = null; k.checking = true; render();
  try { k.check = await call('kernel.check', { token: k.file.token, expected: k.expected }); }
  catch (e) { k.err = e.message; }
  k.checking = false;
  render();
}

async function checkOtg() {
  const o = st.o;
  o.check = null; o.err = null;
  if (!o.src || !o.base) { render(); return; }
  o.checking = true; render();
  const args = o.src.type === 'folder' ? { source: 'folder', token: o.folder.token, file: o.src.file } : { source: 'file', token: o.src.token, expected: o.expected };
  try { o.check = await call('otg.check', Object.assign(args, { base: o.base, other: o.other })); }
  catch (e) { o.err = e.message; }
  o.checking = false;
  render();
}

function consent(el) {
  const input = root.querySelector('[data-in=typed]');
  return { notice: !!(el && el.dataset.notice), typed: input ? input.value.trim() : null };
}

// One flash at a time; steps arrive through fbsEvent('progress').
async function startRun(run, method, args, after) {
  st.sheet = null;
  st.run = Object.assign({ lines: [], state: 'running', backupFirst: st.s.backupFirst, readBack: st.s.readBack }, run);
  render();
  try {
    const out = await call(method, args);
    st.run.outcome = out;
    if (out && out.backup) st.run.backup = out.backup;
    if (out && out.sha256) st.run.sha256 = out.sha256;
    if (out && out.name) st.run.name = out.name;
    st.run.state = out && out.error ? 'failed' : 'done';
    st.run.error = out && out.error;
  } catch (e) {
    st.run.state = 'failed';
    st.run.error = e.message;
  }
  render();
  if (after) after();
}

on('progress', d => {
  if (!st.run || !d) return;
  st.run.lines.push(String(d.line || ''));
  const m = /^backup ok (.*)$/.exec(d.line || '');
  if (m) st.run.backup = m[1].split('/').pop();
  render();
});
on('otgProgress', d => { if (st.run && st.run.kind === 'otg') { st.run.sent = d.sent; st.run.total = d.total; render(); } });
on('otg', () => { if (st.tab === 'otg') scanOtg(); });
on('system', d => { st.systemDark = !!d.dark; applyLook(); render(); });

async function scanOtg() {
  try { st.o.devices = await call('otg.scan'); } catch (e) { st.o.devices = []; }
  if (st.o.phone && !st.o.devices.some(d => d.open)) {
    st.o.phone = null; st.o.check = null; st.o.err = 'usb-gone';
  }
  render();
}
setInterval(() => { if (st.ready && st.tab === 'otg' && !st.o.phone && !st.run) scanOtg(); }, 3000);

function resetOtgFile() { Object.assign(st.o, { src: null, folder: null, check: null, expected: '' }); }

const actions = {
  tab: el => {
    if (st.run && st.run.state === 'running' && st.run.where !== el.dataset.v) toast(t('انتظر العملية تخلص', 'Wait for it to finish'));
    st.tab = el.dataset.v; st.page = null; st.sheet = null; render();
    if (st.tab === 'otg') scanOtg();
    if (st.tab === 'backup' && st.phone && st.phone.root) loadBackups();
  },
  back: () => { st.page = null; render(); },
  page: el => { st.page = el.dataset.v; render(); if (st.page === 'logs') actions.logs(); },
  link: el => call('open', { url: el.dataset.v }).catch(fail),
  refresh: () => refresh(),
  sheetClose: () => { st.sheet = null; render(); },

  pickSource: () => { st.sheet = { type: 'pickBackup' }; render(); },
  pickBackup: () => { st.sheet = { type: 'pickBackup' }; render(); if (!st.backups) loadBackups(); },
  pickFile: async () => {
    let r;
    try { r = await call('pick'); } catch (e) { fail(e); return; }
    if (!r) return;
    if (r.zip) { st.k = { file: r, expected: '', check: null }; render(); checkKernel(); return; }
    st.k = null;
    st.f.src = { type: 'file', token: r.token, name: r.name, size: r.size };
    st.f.expected = '';
    const guess = ALLOWED.slice().sort((a, b) => b.length - a.length).find(b => r.name.toLowerCase().includes(b));
    if (guess && (st.phone.targets || []).includes(guess) && !st.f.base) st.f.base = guess;
    if (/magisk|kernelsu|apatch|patched/i.test(r.name) && !st.f.base) st.f.base = (st.phone.targets || []).includes('init_boot') ? 'init_boot' : 'boot';
    checkImage();
  },
  useBackup: el => {
    const file = el.dataset.file;
    st.sheet = null; st.k = null; st.tab = 'flash'; st.page = null;
    st.f.src = { type: 'backup', name: el.dataset.v, file };
    const part = file.replace(/\.img$/, '');
    st.f.base = baseOf(part);
    st.f.other = !!(st.phone && st.phone.slot && /_[ab]$/.test(part) && part.slice(-2) !== st.phone.slot);
    checkImage();
  },
  base: el => { st.f.base = el.dataset.v; checkImage(); },
  slot: el => { st.f.other = el.dataset.v === '1'; checkImage(); },
  flash: () => {
    const c = st.f.check;
    if (!c || !c.check.canFlash) return;
    if (c.check.verdict === 'ok') actions.doFlash(null);
    else { st.sheet = { type: 'confirm', ctx: 'image', check: c }; render(); }
  },
  doFlash: el => {
    const c = st.f.check, args = Object.assign({ checkId: c.checkId }, consent(el));
    const size = st.f.src && st.f.src.size;
    startRun({ kind: 'image', where: 'flash', partition: c.partition, size, sha256: c.check.sha256 }, 'flash.run', args, () => { st.f.check = null; loadBackups(); });
  },
  why: el => {
    const c = { image: st.f.check, kernel: st.k && st.k.check, otg: st.o.check }[el.dataset.v];
    if (c) { st.sheet = { type: 'confirm', ctx: el.dataset.v, check: c }; render(); }
  },
  kernelClose: () => { st.k = null; render(); },
  flashKernel: () => {
    const c = st.k.check;
    if (!c || !c.check.canFlash) return;
    if (c.check.verdict === 'ok') actions.doKernel(null);
    else { st.sheet = { type: 'confirm', ctx: 'kernel', check: c }; render(); }
  },
  doKernel: el => {
    const c = st.k.check;
    startRun({ kind: 'kernel', where: 'flash', partition: c.typed }, 'kernel.run', Object.assign({ checkId: c.checkId }, consent(el)), () => { st.k.check = null; loadBackups(); });
  },
  runClose: () => {
    const r = st.run;
    st.run = null;
    if (r && r.kind === 'image') checkImage();
    if (r && r.kind === 'kernel' && st.k) checkKernel();
    if (r && r.kind === 'otg') checkOtg();
    render();
  },
  restoreFrom: el => {
    st.run = null;
    const name = el.dataset.v;
    if (el.dataset.file) { actions.useBackup({ dataset: { v: name, file: el.dataset.file } }); return; }
    st.tab = 'backup'; st.bkOpen = name; st.sheet = { type: 'restore', name }; render(); loadBackups();
  },
  reboot: el => call('reboot', { target: el.dataset.v || null }).catch(fail),

  bkOpen: el => { st.bkOpen = st.bkOpen === el.dataset.v ? null : el.dataset.v; render(); },
  compBackup: el => {
    st.tab = 'backup'; st.page = null;
    startRun({ kind: 'backup', where: 'backup' }, 'companion.backup', { set: el.dataset.v }, () => loadBackups());
  },
  compBundle: async () => {
    toast(t('يجهّز التقرير…', 'Making the report…'));
    try { const r = await call('companion.bundle'); toast(t(`انحفظ التقرير (الرقم و IMEI مخفية): ${LE(r.file)}`, `Report saved (serial and IMEI masked): ${esc(r.file)}`)); }
    catch (e) { fail(e); }
  },
  bkNew: () => { st.sheet = { type: 'newBackup' }; render(); },
  doBackup: el => startRun({ kind: 'backup', where: 'backup' }, 'backup.create', { both: el.dataset.v === '1' }, () => loadBackups()),
  bkVerify: async el => {
    const name = el.dataset.v;
    st.bk[name] = 'run'; render();
    try { const r = await call('backup.verify', { name }); st.bk[name] = r.failed.length ? { failed: r.failed } : 'ok'; }
    catch (e) { delete st.bk[name]; fail(e); }
    render();
  },
  bkVerifyAll: async () => {
    for (const b of (st.backups || []).filter(x => x.mine)) await actions.bkVerify({ dataset: { v: b.name } });
  },
  bkRestore: el => { st.sheet = { type: 'restore', name: el.dataset.v }; render(); },
  bkDelete: el => { st.sheet = { type: 'deleteBackup', name: el.dataset.v }; render(); },
  doDelete: async el => {
    st.sheet = null;
    try { await call('backup.delete', { name: el.dataset.v }); toast(t('انحذفت النسخة', 'Backup deleted')); }
    catch (e) { fail(e); }
    st.bkOpen = null;
    loadBackups();
  },

  otgConnect: async el => {
    st.o.connecting = el.dataset.v; st.o.err = null; render();
    try { st.o.phone = await call('otg.connect', { id: el.dataset.v }); st.o.id = el.dataset.v; resetOtgFile(); st.o.base = null; }
    catch (e) { st.o.err = e.message; }
    st.o.connecting = false;
    render();
  },
  otgClose: async () => { await call('otg.close').catch(() => {}); st.o.phone = null; resetOtgFile(); scanOtg(); },
  otgFolder: async () => {
    let r;
    try { r = await call('pickFolder'); } catch (e) { fail(e); return; }
    if (!r) return;
    st.o.folder = r; st.o.src = null; st.o.check = null; render();
  },
  otgPickFile: async () => {
    let r;
    try { r = await call('pick'); } catch (e) { fail(e); return; }
    if (!r) return;
    if (r.zip) { st.sheet = { type: 'message', kind: 'info', color: 'notice', html: t('تفليش ZIP على جوال ثاني يحتاج الريكفري (sideload)، وهذا بيجي بعدين. فلّش صورة IMG هنا.', 'Flashing a zip on another phone needs recovery sideload, which comes later. Flash an IMG here.') }; render(); return; }
    st.o.folder = null; st.o.src = { type: 'file', token: r.token, name: r.name, size: r.size }; st.o.expected = '';
    const guess = ALLOWED.slice().sort((a, b) => b.length - a.length).find(b => r.name.toLowerCase().includes(b));
    if (guess && (st.o.phone.targets || []).includes(guess) && !st.o.base) st.o.base = guess;
    checkOtg();
  },
  otgFile: el => {
    const file = el.dataset.v, part = file.replace(/\.img$/, '');
    st.o.src = { type: 'folder', file, name: file };
    st.o.base = baseOf(part);
    st.o.other = !!(st.o.phone.slot && /_[ab]$/.test(part) && part.slice(-2) !== st.o.phone.slot);
    checkOtg();
  },
  otgBase: el => { st.o.base = el.dataset.v; checkOtg(); },
  otgSlot: el => { st.o.other = el.dataset.v === '1'; checkOtg(); },
  otgFlash: () => {
    const c = st.o.check;
    if (!c || !c.check.canFlash || c.tooBigForDownload) return;
    if (c.check.verdict === 'ok') actions.doOtg(null);
    else { st.sheet = { type: 'confirm', ctx: 'otg', check: c }; render(); }
  },
  doOtg: el => {
    const c = st.o.check;
    startRun({ kind: 'otg', where: 'otg', partition: c.partition, sent: 0, total: c.size, serial: st.o.phone.serial, sha256: c.check.sha256 }, 'otg.flash', Object.assign({ checkId: c.checkId }, consent(el)), () => { st.o.check = null; });
  },
  otgReboot: async el => {
    try { await call('otg.reboot', { target: el.dataset.v || null }); toast(t('انرسل أمر التشغيل', 'Restart sent')); }
    catch (e) { fail(e); }
    st.run = null; st.o.phone = null; resetOtgFile(); scanOtg();
  },

  set: async el => {
    const key = el.dataset.k, value = key === 'minBattery' ? +el.dataset.v : el.dataset.v;
    try { st.s = await call('settings.set', { key, value }); } catch (e) { fail(e); }
    applyLook(); render();
  },
  toggle: async el => {
    const key = el.dataset.k;
    if (key === 'dev' && !st.s.dev) { st.sheet = { type: 'dev' }; render(); return; }
    try { st.s = await call('settings.set', { key, value: !st.s[key] }); } catch (e) { fail(e); }
    recheck(); render();
  },
  devOn: async () => {
    st.sheet = null;
    try { st.s = await call('settings.set', { key: 'dev', value: true }); } catch (e) { fail(e); }
    recheck(); render();
  },
  logs: async () => { try { st.logs = await call('logs'); } catch (e) { st.logs = []; } render(); },
  copyLogs: () => call('copy', { text: st.logs.join('\n') }).then(() => toast(t('انسخ السجل', 'Log copied'))).catch(fail),
};

// Developer mode changes what a check allows: checked again so the page never shows an old verdict.
function recheck() {
  if (st.f.check) checkImage();
  if (st.k && st.k.check) checkKernel();
  if (st.o.check) checkOtg();
}

root.addEventListener('click', e => {
  const el = e.target.closest('[data-act]');
  if (!el || el.disabled) return;
  const f = actions[el.dataset.act];
  if (f) f(el);
});
root.addEventListener('input', e => {
  const el = e.target, k = el.dataset && el.dataset.in;
  if (k === 'typed') {
    const go = root.querySelector('#typedGo');
    if (go && st.sheet) go.disabled = el.value.trim() !== st.sheet.check.typed;
  } else if (k === 'fexp') st.f.expected = el.value.trim();
  else if (k === 'kexp') st.k.expected = el.value.trim();
  else if (k === 'oexp') st.o.expected = el.value.trim();
});
root.addEventListener('change', e => {
  const k = e.target.dataset && e.target.dataset.in;
  if (k === 'fexp') checkImage();
  else if (k === 'kexp') checkKernel();
  else if (k === 'oexp') checkOtg();
});

// The phone's back button: a sheet, then a page, then the Device tab, then out of the app.
window.fbsBack = () => {
  if (st.sheet) { st.sheet = null; render(); return true; }
  if (st.run && st.run.state === 'running') { toast(t('لا تقفل التطبيق قبل ما تخلص العملية', 'Do not close the app until it finishes')); return true; }
  if (st.page) { st.page = null; render(); return true; }
  if (st.tab === 'flash' && st.k) { st.k = null; render(); return true; }
  if (st.tab !== 'device') { st.tab = 'device'; render(); return true; }
  return false;
};

async function start() {
  try {
    const i = await call('init');
    st.s = i.settings; st.systemDark = i.systemDark; st.version = i.version || st.version; st.backupRoot = i.backupRoot || st.backupRoot;
  } catch (e) { console.error(e); }
  st.ready = true;
  applyLook();
  render();
  refresh();
}

if (window.fbsNative) start();
else {
  // In a desktop browser: a pretend phone (demo.js is not packed into the app).
  const s = document.createElement('script');
  s.src = 'demo.js';
  s.onload = start;
  document.head.appendChild(s);
}
