'use strict';
// What the checks and errors say, in Arabic (Gulf) and English. Every value from the phone
// or a file goes through esc() before it is shown.

let A = true;
const t = (ar, en) => A ? ar : en;
const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const L = x => `<span class="ltr">${x}</span>`;
const LE = x => L(esc(x));
const short = sha => sha && sha.length > 12 ? sha.slice(0, 4) + '…' + sha.slice(-4) : (sha || '');
const baseOf = p => p && /_[ab]$/.test(p) ? p.slice(0, -2) : p;
const mib = bytes => {
  if (bytes == null || bytes < 0) return '';
  if (bytes >= 1048576) return (bytes / 1048576).toFixed(bytes >= 104857600 ? 0 : 1).replace(/\.0$/, '') + ' MiB';
  if (bytes >= 1024) return Math.round(bytes / 1024) + ' KiB';
  return bytes + ' B';
};

// What the pages say beyond the checks and errors below, by name.
const M = {
  back: () => t('رجوع', 'Back'),
  scanNote: () => t('التطبيق يقرا سكربتات الملف ويمنع الأوامر الخطيرة المعروفة، بس هذا الفحص مو ضمان وما يكشف كل شي. فلّش ملفات من مطوّر تثق فيه بس.',
    'The app reads the zip’s scripts and refuses the dangerous commands it knows, but this scan is best-effort and cannot catch everything. Only flash zips from a developer you trust.'),
  copyDetails: () => t('نسخ معلومات الجهاز', 'Copy device details'),
  detailsCopied: () => t('انسخت معلومات الجهاز', 'Device details copied'),
  saveLog: () => t('حفظ في ملف', 'Save to a file'),
  logSaved: () => t('انحفظ السجل', 'Log saved'),
  logNotSaved: () => t('ما انحفظ السجل', 'The log was not saved'),
  spaceReading: () => t('يحسب المساحة…', 'Checking free space…'),
  spaceLine: (need, free) => t(`تحتاج ${LE(mib(need))} · المتاح ${LE(mib(free))}`, `Needs ${esc(mib(need))} · ${esc(mib(free))} free`),
  spaceBoth: (need) => t(`السلوتين: ${LE(mib(need))}`, `Both slots: ${esc(mib(need))}`),
  spaceShort: () => t('المساحة ما تكفي لهذي النسخة. فضّي مساحة وجرّب مرة ثانية.', 'Not enough free space for this backup. Free some space and try again.'),
  cancelSend: () => t('إلغاء الإرسال', 'Cancel sending'),
  cancelling: () => t('يلغي…', 'Cancelling…'),
  cancelNote: () => t('الإلغاء ممكن وقت الإرسال بس. إذا بدأ البوت لودر يكتب، ما ينوقف.', 'Cancelling works only while sending. Once the bootloader starts writing, it cannot be stopped.'),
  cancelTooLate: () => t('البوت لودر بدأ يكتب، ما نقدر نوقفه', 'The bootloader is already writing; it cannot be stopped'),
  otgCancelled: () => t('ما انكتب شي على الجوال الموصول. شغّله على fastboot من جديد قبل ما تحاول مرة ثانية.', 'Nothing was written to the connected phone. Restart it into fastboot before trying again.'),
  otgGone: () => t('الكيبل انفصل قبل ما يأكد البوت لودر الكتابة. وصّل الجوال وفلّش القسم مرة ثانية قبل ما تشغّله.', 'The cable came out before the bootloader confirmed the write. Reconnect the phone and flash the partition again before starting it.'),
  recordedOk: () => t('ومطابقة لسجل التطبيق الخاص، مو بس لملف SHA256SUMS.', 'It also matches the app’s own record, not only SHA256SUMS.'),
  slotWord: slot => slot ? t(` · السلوت ${LE(slot.replace('_', ''))}`, ` · slot ${esc(slot.replace('_', ''))}`) : '',
  targetThis: (partition, slot) => t(`بينكتب على القسم ${LE(partition)}${M.slotWord(slot)} في هذا الجوال.`, `This writes partition ${esc(partition)}${M.slotWord(slot)} on this phone.`),
  targetOtg: (partition, slot, serial) => t(`بينكتب على القسم ${LE(partition)}${M.slotWord(slot)} في الجوال الموصول ${LE(serial)}.`, `This writes partition ${esc(partition)}${M.slotWord(slot)} on the connected phone ${esc(serial)}.`),
  rebootSilent: () => t('انرسل أمر التشغيل. الجوال طفى الاتصال بدون رد، وهذا عادي في بعض البوت لودرات.', 'Restart sent. The phone dropped the link without answering, which is normal for some bootloaders.'),
  sizesUnknown: () => t('هذا البوت لودر ما يعطي حجم الأقسام، فالتطبيق ما يقدر يتأكد إن الملف يكفيه القسم. إذا الملف أكبر، البوت لودر نفسه بيرفضه.', 'This bootloader does not report partition sizes, so the app cannot check that a file fits its partition. If the file is too big, the bootloader itself refuses it.'),
  // Before the first write of a session
  pfTitle: () => t('قبل ما نكتب أي شي', 'Before anything is written'),
  pfSub: () => t('فحص سريع يطلع مرة قبل أول كتابة. ما يغني عن فحص كل ملف، يزيد عليه.', 'A quick list shown before the first write. It adds to the check every file gets; it replaces none of it.'),
  pfBlocked: () => t('فيه شي يمنع الكتابة الحين. صلّحه وارجع.', 'Something here stops a write for now. Fix it and come back.'),
  pfAccept: () => t('قريت التنبيهات وأبغى أكمل', 'I have read the warnings and want to go on'),
  pfSkip: () => t('لا تطلعها مرة ثانية لين أقفل التطبيق', 'Do not show this again until I close the app'),
  pfBackupNow: () => t('خذ نسخة الحين', 'Back up now'),
  pfGo: () => t('أكمل', 'Continue'),
  // Putting a whole backup back
  restoreAll: () => t('رجّع النسخة كاملة', 'Restore the whole backup'),
  exportOne: () => t('صدّرها ملف واحد', 'Export as one file'),
  exporting: () => t('يصدّر النسخة ويتحقق منها…', 'Exporting the backup and checking it…'),
  exportDone: n => t(`انحفظت النسخة ملف واحد، و${L(n)} صور تطابقت بصماتها بعد الحفظ ✓`, `Saved as one file; ${n} images matched their SHA-256 after saving ✓`),
  exportNone: () => t('ما انحفظ شي', 'Nothing was saved'),
  supportArchive: () => t('ملف للدعم', 'Support archive'),
  supportDone: () => t('انحفظ ملف الدعم (السجل ومعلومات الجهاز، والرقم مخفي)', 'Support archive saved (the log and the device details, serial masked)'),
  planTitle: () => t('رجّع النسخة كاملة', 'Restore the whole backup'),
  planChecking: (d) => t(`يفحص الصور وحدة وحدة${d ? ' ' + L(d.n + ' / ' + d.total) + ' · ' + LE(d.file) : '…'}`, `Checking the images one by one${d ? ' ' + esc(d.n + ' / ' + d.total) + ' · ' + esc(d.file) : '…'}`),
  planTo: (p) => t(`على هذا الجوال ${LE((p.model || '') + ' · ' + (p.serial || ''))}${M.slotWord(p.slot)}`, `To this phone, ${esc((p.model || '') + ' · ' + (p.serial || ''))}${M.slotWord(p.slot)}`),
  planNote: () => t('كل صورة مرّت بنفس فحص التفليش العادي. الكتابة قسم قسم مع قراءة بعد كل قسم، وتوقف عند أول خطأ.', 'Every image went through the same check as a single flash. They are written one by one, each read back, and it stops at the first failure.'),
  planBackup: (need, free) => t(`أول شي ينأخذ نسخة من الأقسام اللي بتنكتب${need != null && free != null ? `: تحتاج ${LE(mib(need))} · المتاح ${LE(mib(free))}` : ''}.`, `First, a backup of the partitions about to be overwritten is taken${need != null && free != null ? `: needs ${esc(mib(need))} · ${esc(mib(free))} free` : ''}.`),
  planNoBackup: () => t('النسخة قبل التفليش مقفلة في الإعدادات: ما راح ينأخذ نسخة من اللي بينكتب عليه.', 'Backup before flashing is off in Settings: no copy is taken of what gets overwritten.'),
  planSkipped: () => t('ما تنكتب', 'Not written'),
  planNone: () => t('ما فيه في هالنسخة شي ينكتب من الجوال.', 'Nothing in this backup can be written from the phone.'),
  planGo: n => t(n === 1 ? 'رجّع قسم واحد' : n === 2 ? 'رجّع قسمين' : `رجّع ${L(n)} أقسام`, `Restore ${n} partition${n === 1 ? '' : 's'}`),
  planReason: r => ({
    'other-slot': t('السلوت الثاني: ما ينكتب إلا إذا اخترته', 'The other slot: written only if you tick it'),
    'verified-boot': t('vbmeta يحدد هل تنقبل باقي الصور. نسخة قديمة منه مع boot معدّل ما تقلع. اختره بس إذا ترجّع كل شي مثل ما كان.', 'vbmeta decides whether the other images are accepted. An older one beside a patched boot will not start. Tick it only when putting everything back as it was.'),
    'not-allowed': t('التطبيق ما يكتب هذا القسم أبداً (يرجع من برنامج الويندوز)', 'The app never writes this partition (it goes back from the Windows app)'),
    'no-partition': t('الجوال ما فيه هذا القسم', 'The phone has no such partition'),
    'not-an-image': t('مو صورة قسم', 'Not a partition image'),
    'check-failed': t('الفحص رفضها', 'Its check refused it'),
  })[r] || esc(r || ''),
  restoreRefused: r => ({
    'not-recorded': t('هالنسخة ما أخذها التطبيق، فما عنده سجل خاص يثبتها. رجّع منها صورة صورة من "استرجاع"، وكل صورة تنفحص لحالها.', 'The app did not make this backup, so it has no record of its own to vouch for it. Restore from it image by image with "Restore"; each image is checked on its own.'),
    'other-phone': t('النسخة من جوال ثاني: ما ترجع على هذا الجوال.', 'This backup is from another phone: it does not go back to this one.'),
    'no-serial': t('ما نقدر نثبت إن النسخة لهذا الجوال (ما فيه رقم تسلسلي).', 'It cannot be proven that this backup is this phone’s (no serial).'),
    'tampered': t('النسخة تغيّرت بعد ما أخذها التطبيق: SHA256SUMS ما يطابق سجل التطبيق الخاص. ما ترجع.', 'The backup was changed after the app made it: SHA256SUMS no longer matches the app’s own record. It is not restored.'),
  })[r] || esc(r || ''),
  confirmRestore: (p, n) => t(`بينكتب ${L(n)} ${n === 1 ? 'قسم' : n === 2 ? 'قسمين' : 'أقسام'} على هذا الجوال ${LE((p.model || '') + ' · ' + (p.serial || ''))}${M.slotWord(p.slot)}.`, `This writes ${n} partition${n === 1 ? '' : 's'} on this phone, ${esc((p.model || '') + ' · ' + (p.serial || ''))}${M.slotWord(p.slot)}.`),
  restoring: () => t('يرجّع النسخة', 'Restoring the backup'),
  restoreStep: p => t(`كتابة ${LE(p)} وقراءته`, `Write ${esc(p)} and read it back`),
  restoreBackupStep: () => t('نسخة من الأقسام اللي بتنكتب', 'Back up the partitions about to be overwritten'),
  restoredAll: n => t(`رجعت النسخة: ${L(n)} ${n === 1 ? 'قسم' : n === 2 ? 'قسمين' : 'أقسام'}`, `The backup is back: ${n} partition${n === 1 ? '' : 's'}`),
  restoredNote: () => t('كل قسم انقرا بعد الكتابة وتطابق مع صورته. شغّل الجوال من جديد.', 'Every partition was read back and matched its image. Restart the phone.'),
  rWritten: () => t('انكتبت وتطابقت', 'Written and verified'),
  rFailed: () => t('وقف عندها (ممكن تغيّرت)', 'Stopped here (may be changed)'),
  rNotWritten: () => t('ما انكتبت', 'Not written'),
  rNone: () => t('ولا شي', 'none'),
  restoreHow: (touched, backup) => t(
    (touched ? 'القسم اللي وقف عنده ممكن فيه جزء من الصورة. لا تعيد التشغيل. ' : 'ما انكتب شي على القسم اللي وقف عنده. ') + 'الأقسام اللي انكتبت سليمة وتبقى. تقدر تعيد استرجاع النسخة من صفحة النسخ: يعيد الفحص ويكتب اللي تختاره من جديد.' + (backup ? ' وحالة الجوال قبل الاسترجاع محفوظة في نسخة.' : ''),
    (touched ? 'The partition it stopped at may hold part of the image. Do not restart. ' : 'Nothing was written to the partition it stopped at. ') + 'The partitions already written are complete and stay. You can run the restore again from the Backups page: it checks again and writes what you choose from the start.' + (backup ? ' The phone as it was before the restore is kept in a backup.' : '')),
  // Sideload to a recovery (experimental)
  experimental: () => t('تجريبي', 'Experimental'),
  sideRow: () => t('جوال في الريكفري (sideload)', 'A phone in recovery (sideload)'),
  sideTitle: () => t('إرسال ZIP للريكفري', 'Send a zip to recovery'),
  sideExp: () => t('ميزة تجريبية: انكتبت على وصف بروتوكول adb وانجرّبت على جهاز وهمي، ولسّا ما انجرّبت على كل ريكفري. إذا ما اشتغلت معك، استخدم adb sideload من الكمبيوتر وبلّغنا.', 'Experimental: written from the adb protocol’s description and tried against a stand-in, not yet against every recovery. If it does not work for you, use adb sideload from a computer and tell us.'),
  sideHow1: () => t('في الجوال الثاني: ادخل الريكفري واختر Apply update from ADB (أو ADB Sideload).', 'On the other phone: open recovery and choose Apply update from ADB (or ADB Sideload).'),
  sideHow2: () => t('وصّله بكيبل OTG، واختر ملف ZIP من جوالك.', 'Connect it with the OTG cable, and pick the zip from this phone.'),
  sideHow3: () => t('إذا سألك الجوال الثاني عن السماح لهذا الجهاز، وافق.', 'If the other phone asks whether to allow this device, allow it.'),
  sideHonest: () => t('التطبيق ما يفحص محتوى هالملف: الريكفري هو اللي يتحقق من توقيعه وهل هو لذاك الجوال، ويثبّته بنفسه. أرسل ملفات من مصدر تثق فيه بس.', 'The app does not inspect this package: the recovery verifies its signature and whether it is for that phone, and installs it itself. Only send packages from a source you trust.'),
  sidePick: () => t('اختر ملف ZIP', 'Pick the zip'),
  sideSend: () => t('أرسل للريكفري', 'Send to recovery'),
  sideBack: () => t('رجوع لقائمة OTG', 'Back to the OTG list'),
  sideNotZip: () => t('هذا مو ملف ZIP', 'That is not a zip'),
  sideConfirm: (name, size) => t(`بينرسل ${LE(name)} (${LE(mib(size))}) للريكفري في الجوال الموصول، وهو اللي يثبّته.`, `This sends ${esc(name)} (${esc(mib(size))}) to the recovery on the connected phone, which installs it.`),
  sideConnect: () => t('الاتصال بالريكفري', 'Connect to the recovery'),
  sideApprove: () => t('وافق على السماح لهذا الجهاز في الجوال الثاني', 'Allow this device on the other phone'),
  sideServe: () => t('الريكفري يقرا الملف ويثبّته', 'The recovery reads the package and installs it'),
  sideRunning: () => t('يرسل للريكفري', 'Sending to recovery'),
  sideDone: () => t('الريكفري استلم الملف', 'The recovery took the package'),
  sideDoneNote: () => t('شوف شاشة الجوال الثاني: يكمّل التثبيت بنفسه ويقولك النتيجة، وبعدها شغّله من قائمته.', 'Watch the other phone’s screen: it finishes installing by itself and says how it went; then restart it from its menu.'),
  sideFailNote: code => ({
    'sideload-failed': t('الريكفري رفض الملف أو وقف التثبيت (توقيع، أو الملف مو لذاك الجوال). شوف شاشته للسبب.', 'The recovery refused the package or stopped installing (its signature, or it is not for that phone). Its screen says why.'),
    'not-sideload': t('الجوال الموصول مو في وضع sideload. اختر Apply update from ADB في الريكفري.', 'The connected phone is not in sideload mode. Choose Apply update from ADB in its recovery.'),
    'sideload-refused': t('هالريكفري ما قبل استلام ملف بهالطريقة.', 'This recovery does not take a package this way.'),
    'auth-required': t('الجوال الثاني ما سمح لهذا الجهاز. وافق على السؤال اللي يطلع فيه وجرّب مرة ثانية.', 'The other phone did not allow this device. Accept the question it shows and try again.'),
    'cancelled': t('انلغى الإرسال. الريكفري ما يثبّت ملف ناقص؛ ابدأ sideload فيه من جديد إذا تبغى تعيد.', 'Sending was cancelled. The recovery does not install half a package; start sideload on it again to retry.'),
  })[code] || t('انقطع الاتصال بالريكفري قبل ما يخلص. الريكفري ما يثبّت ملف ناقص؛ شوف شاشته وابدأ sideload من جديد.', 'The link to the recovery was lost before it finished. The recovery does not install half a package; check its screen and start sideload again.'),
  otgNote: () => t('قائمة OTG ما تحتاج روت على جوالك. والكتابة تكون على الجوال الموصول برقمه بس. وجوال في الريكفري (Apply update from ADB) يطلع هنا عشان ترسل له ملف ZIP.', 'The OTG menu needs no root on your phone, and writes only to the connected phone, by its serial. A phone in recovery (Apply update from ADB) shows here too, to be sent a zip.'),
  otgZip: () => t('ملف ZIP يروح للريكفري مو للـ fastboot: دخّل الجوال الثاني الريكفري واختر Apply update from ADB، ويطلع لك في قائمة OTG. هنا فلّش صورة IMG.', 'A zip goes to a recovery, not to fastboot: put the other phone in recovery, choose Apply update from ADB, and it shows in the OTG list. Flash an IMG here.'),
  on: () => t('شغّال', 'On'),
  off: () => t('مقفل', 'Off'),
};

// [text, small] for one line of the list shown before the first write. v[0] says which case it is.
function preText(i) {
  const v = i.values || [];
  const power = v[3] === 'charging' ? t(' · يشحن', ' · charging') : '';
  switch (i.code) {
    case 'battery': return v[0] === 'unknown' ? [t('البطارية غير معروفة', 'Battery level unknown'), t('تأكد إنها تكفي قبل ما تكمل', 'Make sure it is enough before going on')]
      : v[0] === 'low' ? [t(`البطارية ${L(v[1] + '%')}: أقل من ${L(v[2] + '%')}`, `Battery ${esc(v[1])}%: below the ${esc(v[2])}% minimum`), t('اشحن الجوال قبل أي كتابة', 'Charge the phone before any write') + power]
      : [t(`البطارية ${L(v[1] + '%')}`, `Battery ${esc(v[1])}%`), t(`الحد الأدنى ${L(v[2] + '%')}`, `The minimum is ${esc(v[2])}%`) + power];
    case 'root': return v[0] === 'ok' ? [t('صلاحية الروت متاحة', 'Root access granted')] : [t('ما فيه صلاحية روت', 'No root access'), t('وافق على التطبيق في مدير الروت وحدّث صفحة الجهاز', 'Allow the app in your root manager and refresh the Device page')];
    case 'bootloader': return v[0] === 'unlocked' ? [t('البوت لودر مفتوح', 'Bootloader unlocked')]
      : v[0] === 'locked' ? [t('البوت لودر مقفل', 'Bootloader locked'), t('الصور المعدّلة ما تقلع على بوت لودر مقفل. الصور الأصلية عادي.', 'Modified images do not start on a locked bootloader. Stock images are fine.')]
      : [t('حالة البوت لودر غير معروفة', 'Bootloader state unknown'), t('إذا كان مقفل، الصور المعدّلة ما تقلع', 'If it is locked, modified images will not start')];
    case 'backup': return v[0] === 'today' ? [t('فيه نسخة من اليوم للي بينكتب عليه', 'There is a backup from today of what gets written')]
      : v[0] === 'automatic' ? [t('نسخة تنأخذ تلقائي قبل الكتابة', 'A backup is taken by itself before the write'), t('ما فيه نسخة من اليوم في سجل التطبيق', 'No backup from today in the app’s record')]
      : [t('ما فيه نسخة من اليوم، والنسخة التلقائية مقفلة', 'No backup from today, and the automatic one is off'), t('لو صار شي، ما فيه نسخة ترجع لها', 'If something goes wrong there is nothing to go back to')];
    case 'space': return v[0] === 'unknown' ? [t('المساحة المتاحة غير معروفة', 'Free space unknown'), t('النسخة التلقائية تحتاج مكان', 'The automatic backup needs room')]
      : v[0] === 'short' ? [t('المساحة ما تكفي للنسخة التلقائية', 'Not enough room for the automatic backup'), M.spaceLine(+v[1], +v[2])]
      : [t('المساحة تكفي للنسخة التلقائية', 'Room for the automatic backup'), M.spaceLine(+v[1], +v[2])];
    case 'otg-serial': return v[0] === 'ok' ? [t('الجوال الموصول', 'The connected phone'), LE(v[1])] : [t('الجوال الموصول ما أعطى رقمه', 'The connected phone gave no serial'), t('تأكد إنه الجوال الصحيح على الكيبل', 'Make sure the right phone is on the cable')];
    case 'otg-bootloader': return v[0] === 'unlocked' ? [t('بوت لودر الجوال الموصول مفتوح', 'The connected phone’s bootloader is unlocked')]
      : v[0] === 'locked' ? [t('بوت لودر الجوال الموصول مقفل', 'The connected phone’s bootloader is locked'), t('غالباً بيرفض التفليش', 'It will most likely refuse to flash')]
      : [t('حالة بوت لودر الجوال الموصول غير معروفة', 'The connected phone’s bootloader state is unknown')];
  }
  return [esc(i.code), esc(v.join(' · '))];
}

// [text, small] for one check item. v = values, lv = its level.
function checkText(item, ctx) {
  const v = item.values || [], c = item.code, lv = item.level;
  switch (c) {
    case 'HashMatches': return [t('SHA-256 يطابق النسخة', 'SHA-256 matches the backup'), LE(short(v[0]))];
    case 'HashMismatch': return [t('SHA-256 ما يطابق النسخة', 'SHA-256 does not match the backup'), t('الملف تغيّر أو انقطع بعد النسخ', 'The file changed or was cut off after the backup')];
    case 'HashMissing': return [t('الملف مو مسجّل في SHA256SUMS', 'The file is not in SHA256SUMS'), t('ما نقدر نثبت إنه ما تغيّر بعد النسخ', 'There is no way to prove it did not change')];
    case 'ExpectedHashMatches': return [t('يطابق بصمة المطور ✓', 'Matches the developer’s SHA-256 ✓'), LE(short(v[0]))];
    case 'ExpectedHashMismatch': return [t('ما يطابق بصمة المطور', 'Does not match the developer’s SHA-256'), t('الملف مو اللي نشره المطور، أو انقطع وقت التنزيل', 'Not the file the developer published, or the download was cut off')];
    case 'NoFingerprint':
      return lv === 'notice'
        ? [t('ما لصقت بصمة المطور', 'No published SHA-256 pasted'), t('إذا المطور نشر بصمة، الصقها فوق للتأكد', 'If the developer published one, paste it above to be sure')]
        : [t('ما فيه نسخة تثبت مصدره', 'No backup vouches for it'), LE(v[0])];
    case 'SamePhone': return [t('من نفس الجوال', 'From this phone'), LE(v[0])];
    case 'OtherPhone': return [t('النسخة من جوال ثاني', 'The backup is from another phone'), null,
      `<div class="cmp"><span class="k">${t('النسخة', 'Backup')}</span><span class="v bad">${esc(v[0])}</span><span class="k">${ctx === 'otg' ? t('الجوال الموصول', 'Connected phone') : t('هذا الجوال', 'This phone')}</span><span class="v">${esc(v[1])}</span></div>`];
    case 'PhoneUnknown': return [t('النسخة ما فيها رقم الجوال', 'The backup has no serial'), t('الموديل ما يتعارض، بس تأكد إنها لنفس الجوال', 'Nothing contradicts it, but make sure it is this phone’s')];
    case 'SamePartition': return [t(`صورة ${LE(baseOf(v[0]))} للقسم ${LE(v[1])}`, `${esc(baseOf(v[0]))} image for ${esc(v[1])}`)];
    case 'OtherPartition': return [t(`الصورة من ${LE(v[0])}، مو من ${LE(baseOf(v[1]))}`, `The image is ${esc(v[0])}, not ${esc(baseOf(v[1]))}`), t('اختر القسم الصحيح أو ملف ثاني', 'Pick the right partition or another file')];
    case 'NotAllowed': return [t('هذا القسم ممنوع في التطبيق', 'The app never writes this partition'), LE(v[0])];
    case 'NoSuchPartition': return [t('الجوال ما فيه هذا القسم', 'The phone has no such partition'), LE(v[0])];
    case 'WrongImageKind': return v[1] === 'sparse'
      ? [t('صورة sparse، مو صورة قسم عادية', 'A sparse image, not a plain partition image'), t('هذي تنفلش من الكمبيوتر', 'Flash it from a computer')]
      : [t(`الملف مو صورة ${LE(v[0])}`, `Not a ${esc(v[0])} image`), LE(v[1])];
    case 'TooBig': return [t('الملف أكبر من القسم', 'The file is bigger than the partition'), L(`${esc(mib(+v[0]))} &gt; ${esc(mib(+v[1]))}`)];
    case 'EmptyFile': return [t('الملف فاضي أو ما ينقرا', 'The file is empty or unreadable'), LE(v[0])];
    case 'KernelSeriesDiffers': return [t('الكيرنل ما يناسب جوالك', 'This kernel does not fit your phone'),
      t(`الملف ${LE(v[0])} وجوالك ${LE(v[1])}: إصدار ثاني تماماً، الجوال ما راح يقلع فيه`, `The file is ${esc(v[0])} and your phone runs ${esc(v[1])}: another series, the phone will not start with it`)
      + (v[2] === 'name' ? `<br>${t('(عرفناه من اسم الملف)', '(read from the file name)')}` : '')];
    case 'KernelInterfaceDiffers': return [t('واجهة الكيرنل (KMI) مختلفة', 'Another kernel interface (KMI)'),
      L(`${esc(v[1])} → ${esc(v[0])}`) + `<br>${t('ممكن ما تشتغل التعريفات ويعلق الإقلاع؛ النسخة التلقائية ترجّعه', 'Modules may not load and it may bootloop; the automatic backup undoes it')}`];
    case 'RecordedMatches': return [t('يطابق السجل الخاص بالتطبيق', 'Matches the app’s own record'), t('البصمة اللي حفظها التطبيق وقت النسخ', 'The SHA-256 the app kept when it made the backup')];
    case 'BackupTampered': return [t('النسخة تغيّرت بعد ما أخذها التطبيق', 'The backup was changed after the app made it'),
      t('الصورة و SHA256SUMS اتعدّلوا الاثنين، والسجل الخاص بالتطبيق ما يطابق. ممكن تطبيق ثاني عدّلها.', 'Both the image and SHA256SUMS were changed, and the app’s own record does not match. Another app may have changed it.')];
    case 'NotRecorded': return [t('الملف مو من النسخة اللي أخذها التطبيق', 'This file is not part of the backup the app made'),
      t(`انضاف للمجلد بعدين، وسجل التطبيق الخاص ما يعرفه: ${LE(v[0])}`, `It was added to the folder later; the app’s own record does not know it: ${esc(v[0])}`)];
    case 'KernelSame': return [t('نفس إصدار كيرنل جوالك', 'The same kernel version as your phone'), LE(v[0])];
    case 'KernelVersionDiffers': return [t('إصدار الكيرنل مختلف (عادي)', 'Different kernel version (normal)'), L(`${esc(v[1])} → ${esc(v[0])}`)];
    case 'NotAnyKernel': return [t('مو ملف AnyKernel3', 'Not an AnyKernel3 zip'), t('ما فيه anykernel.sh أو update-binary', 'No anykernel.sh or update-binary inside')];
    case 'UnsafeZipEntry': return [t('فيه مسار خطير داخل الملف', 'The zip has an unsafe path'), LE(v[0])];
    case 'DeviceListed': return [t(`مكتوب لـ ${LE(v[0])}، وجوالك ${LE(v[1])}`, `Made for ${esc(v[0])}; your phone is ${esc(v[1])}`)];
    case 'DeviceNotListed': return [t('الملف مكتوب لجهاز ثاني', 'Made for another device'), t(`الأجهزة: ${LE(v[0])} · جوالك ${LE(v[1])}`, `Devices: ${esc(v[0])} · your phone is ${esc(v[1])}`)];
    case 'DeviceCheckOff': return [t('فحص الجهاز مقفل في الملف', 'The zip’s device check is off'), t('AnyKernel3 ما راح يتأكد إنه لجوالك', 'AnyKernel3 will not check that it is for your phone')];
    case 'AndroidSupported': return [t(`أندرويد ${LE(v[0])} ضمن المدعوم`, `Android ${esc(v[0])} is supported`), LE(v[1])];
    case 'AndroidNotSupported': return [t(`أندرويد ${LE(v[0])} مو ضمن المدعوم`, `Android ${esc(v[0])} is not supported`), t(`المدعوم: ${LE(v[1])}`, `Supported: ${esc(v[1])}`)];
    case 'BootCheck': return bootText(v[0], v.slice(1));
    case 'DangerousScript': {
      const why = {
        write: t('يكتب على قسم ما يحتاجه أي كيرنل (مثل modem أو persist أو البوت لودر)', 'Writes a partition no kernel needs (modem, persist, the bootloader...)'),
        wipe: t('يفرمت أو يمسح التخزين', 'Formats or wipes storage'),
        delete: t('يحذف بيانات الجوال أو الذاكرة', 'Deletes the phone’s data or storage'),
        network: t('ينزّل أو يرسل شي من الإنترنت', 'Downloads or sends something over the network'),
        binary: t('المثبّت ملف تنفيذي ما ينقرا', 'The installer is a binary that cannot be read'),
        hidden: t('يشغّل ملف مخفي ما ينقرا', 'Runs a hidden file that cannot be read'),
        'too-big': t('سكربت كبير بشكل غير طبيعي', 'An unusually large script'),
      }[v[0]] || esc(v[0]);
      return [t('خطر: الملف فيه أوامر خبيثة أو مدمّرة', 'Danger: the zip has harmful or destructive commands'), why + (v[1] ? `<br>${LE(v[1])}${v[2] ? ': ' + LE(v[2]) : ''}` : '')];
    }
  }
  return [esc(c), esc(v.join(' · '))];
}

function bootText(code, v) {
  switch (code) {
    case 'NotBootImage': return [t('مو صورة إقلاع', 'Not a boot image'), LE(v[0])];
    case 'NoKernelForBoot': return [t('الصورة ما فيها كيرنل، وهذا القسم يحتاج كيرنل', 'No kernel in the image, and this partition needs one'), v[1] ? t(`يمكن الصورة لـ ${LE(v[1])}`, `It may be meant for ${esc(v[1])}`) : null];
    case 'KernelInInitBoot': return [t(`صورة فيها كيرنل على ${LE('init_boot')}`, 'An image with a kernel, for init_boot'), t('عادةً init_boot ما فيه كيرنل. تأكد من الملف', 'init_boot usually has no kernel; check the file')];
    case 'VendorBootMismatch': return [t('نوع الصورة ما يناسب القسم', 'The image type does not fit the partition'), LE('vendor_boot ≠ boot')];
    case 'KmiMismatch': return [t('واجهة الكيرنل (KMI) مختلفة', 'Another kernel interface (KMI)'), L(`${esc(v[1])} → ${esc(v[0])}`)
      + `<br>${t('ممكن ما تشتغل التعريفات ويعلق الإقلاع؛ النسخة التلقائية ترجّعه', 'Modules may not load and it may bootloop; the automatic backup undoes it')}`];
    case 'KernelVersionMismatch': return [t('إصدار الكيرنل مختلف (عادي)', 'Different kernel version (normal)'), L(`${esc(v[1])} → ${esc(v[0])}`)];
    case 'OlderPatchLevel': return [t('تصحيح أمني أقدم', 'Older security patch'), L(`${esc(v[0])} &lt; ${esc(v[1])}`)];
    case 'KernelSummary': return [t('الكيرنل', 'Kernel'), LE(v[0])];
    case 'RootSummary': return [t('فيها روت', 'Rooted'), LE(v[0])];
    case 'Stock': return [t('صورة أصلية بدون روت', 'A stock image, no root')];
    case 'RootManagerChange': return [t('روت مختلف عن اللي في جوالك', 'Another root manager than your phone runs'), L(`${esc(v[1])} → ${esc(v[0])}`)];
  }
  return [esc(code), esc(v.join(' · '))];
}

function errText(code) {
  const c = String(code || '');
  const k = c.split(':')[0];
  const m = {
    'no-root': t('ما فيه صلاحية روت للتطبيق', 'The app has no root access'),
    'busy': t('فيه عملية شغّالة، انتظرها تخلص', 'Something is running; wait for it to finish'),
    'low-battery': t('البطارية أقل من الحد المسموح للتفليش (من الإعدادات)', 'The battery is below the flashing minimum (see Settings)'),
    'not-allowed': t('هذا القسم ممنوع', 'This partition is not allowed'),
    'not-confirmed': t('ما تم التأكيد المطلوب', 'The confirmation did not match'),
    'check-expired': t('الفحص انتهى، افحص الملف مرة ثانية', 'The check expired; check the file again'),
    'no-file': t('الملف ما عاد موجود، اختره مرة ثانية', 'The file is gone; pick it again'),
    'read-failed': t('ما قدرنا نقرا الملف', 'Could not read the file'),
    'file-changed': t('الملف تغيّر بعد الفحص، فما انكتب شي', 'The file changed after the check; nothing was written'),
    'no-partition': t('الجوال ما فيه هذا القسم', 'The phone has no such partition'),
    'no-size': t('ما قدرنا نعرف حجم القسم', 'Could not read the partition size'),
    'too-big': t('الملف أكبر من القسم', 'The file is bigger than the partition'),
    'backup-failed': t('النسخة الاحتياطية ما نجحت، فما انكتب شي', 'The backup failed, so nothing was written'),
    'no-space': t('المساحة ما تكفي للنسخة الاحتياطية، فما انكتب شي', 'Not enough free space for the backup, so nothing was written'),
    'preflight-needed': t('لازم تمر على فحص ما قبل الكتابة أول', 'The list before the first write comes first'),
    'preflight-blocked': t('فحص ما قبل الكتابة فيه شي يمنع', 'Something on the list before the write blocks it'),
    'nothing-to-restore': t('ما اخترت شي يرجع', 'Nothing was chosen to restore'),
    'export-failed': t('ما انحفظ الملف سليم، فانحذف', 'The file was not saved whole, so it was removed'),
    'not-a-zip': t('هذا مو ملف ZIP', 'That is not a zip'),
    'sideload-failed': t('الريكفري رفض الملف', 'The recovery refused the package'),
    'sideload-refused': t('الريكفري ما قبل الاستلام', 'The recovery would not take it'),
    'sideload-lost': t('انقطع الاتصال بالريكفري', 'The link to the recovery was lost'),
    'not-sideload': t('الجوال الموصول مو في وضع sideload', 'The connected phone is not in sideload mode'),
    'auth-required': t('الجوال الثاني ما سمح لهذا الجهاز', 'The other phone did not allow this device'),
    'cancelled': t('انلغى الإرسال', 'Sending was cancelled'),
    'backup-unreadable': t('ما قدرنا نقرا النسخة', 'Could not read the backup'),
    'write-failed': t('الكتابة على القسم ما نجحت', 'Writing the partition failed'),
    'verify-failed': t('القسم بعد الكتابة ما يطابق الملف', 'The partition does not match the file after writing'),
    'install-failed': t('AnyKernel3 وقف بخطأ', 'AnyKernel3 stopped with an error'),
    'delete-failed': t('ما قدرنا نحذف النسخة', 'Could not delete the backup'),
    'no-device': t('الجوال الموصول ما عاد موجود', 'The connected phone is gone'),
    'usb-denied': t('ما تمت الموافقة على صلاحية USB', 'The USB permission was not granted'),
    'usb-open-failed': t('ما قدرنا نفتح اتصال USB', 'Could not open the USB connection'),
    'usb-gone': t('انقطع اتصال الجوال الموصول', 'The connected phone was unplugged'),
    'serial-mismatch': t('رقم الجوال تغيّر: ما انكتب شي', 'The serial changed: nothing was written'),
    'no-serial': t('الجوال الموصول ما أعطى رقمه', 'The connected phone gave no serial'),
    'too-big-for-download': t('الملف أكبر من اللي يستقبله البوت لودر مرة وحدة', 'The file is bigger than the bootloader accepts at once'),
    'otg-failed': t('الجوال الموصول رفض', 'The connected phone refused'),
    'bad-link': t('رابط مو مسموح', 'Link not allowed'),
    'update-offline': t('ما فيه اتصال بالإنترنت', 'No internet connection'),
    'update-failed': t('GitHub ما رد، جرّب بعدين', 'GitHub did not answer; try later'),
    'bad-set': t('نوع النسخة مو معروف', 'Unknown backup set'),
    'companion-missing': t('الإضافة مو مركّبة أو موقفة', 'The Companion module is missing or disabled'),
    'companion-busy': t('الإضافة تاخذ نسخة ثانية الحين', 'The module is already taking a backup'),
    'companion-space': t('المساحة ما تكفي للنسخة', 'Not enough free space for this backup'),
    'companion-storage': t('ما قدرت الإضافة تكتب في الذاكرة', 'The module could not write to storage'),
    'companion-nothing': t('ما فيه أقسام لهذي النسخة', 'No partitions for this backup set'),
    'companion-no-by-name': t('الإضافة ما لقت مجلد الأقسام', 'The module found no by-name folder'),
    'companion-failed': t('الإضافة ما كملت النسخة', 'The module did not finish the backup'),
  }[k];
  const detail = c.includes(':') ? c.slice(c.indexOf(':') + 1).trim() : '';
  return (m || t('خطأ', 'Error')) + (detail ? ` <small class="ltr">${esc(detail)}</small>` : (m ? '' : ` <small class="ltr">${esc(c)}</small>`));
}
