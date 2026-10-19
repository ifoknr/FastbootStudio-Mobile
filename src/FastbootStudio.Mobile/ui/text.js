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
