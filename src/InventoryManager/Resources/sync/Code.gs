/**
 * Inventory Manager → Google Sheet sync.
 *
 * 1. Paste this whole file into Extensions → Apps Script of your Google Sheet and Save.
 * 2. Choose "authorize" in the function list at the top and click Run, then allow access (one time only).
 * 3. Deploy → New deployment → Web app (Execute as: Me, Who has access: Anyone) and copy the /exec link into the app.
 * The app only sends data here; nothing in this Sheet is read back into the app.
 */
var SECRET = '__SECRET__';   // must match the key in the app (filled in automatically when you copy the script)

/**
 * Run this once from the editor before deploying: it makes Google show the permission screen,
 * so the web app can write to this Sheet. Running it changes nothing.
 */
function authorize() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  Logger.log('Access granted. The app can now write to "' + ss.getName() + '". Next: Deploy → New deployment → Web app.');
}

function doPost(e) {
  var req;
  try {
    req = JSON.parse(e.postData.contents);
  } catch (err) {
    return reply({ ok: false, error: 'Invalid request' });
  }
  if (!req || req.key !== SECRET) return reply({ ok: false, error: 'Wrong key' });

  var lock = LockService.getScriptLock();
  lock.waitLock(30000);
  try {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    (req.ops || []).forEach(function (op) { applyOp(ss, op); });
    return reply({ ok: true, applied: (req.ops || []).length });
  } catch (err) {
    return reply({ ok: false, error: String(err) });
  } finally {
    lock.releaseLock();
  }
}

/** Opening the link in a browser shows this, so you can check the deployment works. */
function doGet() {
  return reply({ ok: true, app: 'Inventory Manager sync' });
}

function applyOp(ss, op) {
  var sh = ss.getSheetByName(op.sheet) || ss.insertSheet(op.sheet);
  var headers = op.headers || [];
  var rows = op.rows || [];

  if (op.type === 'replace') {
    sh.clearContents();
    writeHeader(sh, headers);
    if (rows.length) sh.getRange(2, 1, rows.length, headers.length).setValues(rows);
    formatColumns(sh, op.formats, rows.length);
    return;
  }

  if (op.type === 'append') {
    if (sh.getLastRow() === 0) writeHeader(sh, headers);
    // Skip rows whose ID (first column) is already in the sheet, so a retried sync never duplicates.
    var existing = {};
    var last = sh.getLastRow();
    if (last > 1) {
      sh.getRange(2, 1, last - 1, 1).getValues().forEach(function (r) { existing[String(r[0])] = true; });
    }
    var fresh = rows.filter(function (r) { return !existing[String(r[0])]; });
    if (fresh.length) {
      sh.getRange(sh.getLastRow() + 1, 1, fresh.length, headers.length).setValues(fresh);
      formatColumns(sh, op.formats, sh.getLastRow() - 1);
    }
    return;
  }

  if (op.type === 'delete') {
    var lastRow = sh.getLastRow();
    if (lastRow < 2 || !op.ids || !op.ids.length) return;
    var remove = {};
    op.ids.forEach(function (id) { remove[String(id)] = true; });
    var ids = sh.getRange(2, 1, lastRow - 1, 1).getValues();
    for (var i = ids.length - 1; i >= 0; i--) {
      if (remove[String(ids[i][0])]) sh.deleteRow(i + 2);
    }
  }
}

function writeHeader(sh, headers) {
  if (!headers.length) return;
  sh.getRange(1, 1, 1, headers.length).setValues([headers])
    .setFontWeight('bold').setBackground('#e7f3f1').setFontColor('#0f766e');
  sh.setFrozenRows(1);
}

/** formats: one number format per column ('' = leave as is), e.g. '#,##0.00'. */
function formatColumns(sh, formats, rowCount) {
  if (!formats || !rowCount) return;
  for (var c = 0; c < formats.length; c++) {
    if (formats[c]) sh.getRange(2, c + 1, rowCount, 1).setNumberFormat(formats[c]);
  }
}

function reply(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}
