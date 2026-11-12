#!/usr/bin/env node
// Đồng bộ MÃ LỖI BE ↔ i18n FE/mobile (12/11/2026).
//
// BE trả lỗi dạng ProblemDetails `{ status, code, detail, ... }`. Hai client dịch `code` thành câu
// tiếng Việt/Anh qua i18n:
//   1. `errors.<CODE>`      — câu soạn riêng cho mã đó.
//   2. `errorsByKind.<kind>` — câu theo nhóm hậu tố (`_NOT_FOUND`, `_INVALID_STATE`, …) để mã BE
//                              mới thêm vẫn ra tiếng Việt, không lộ `detail` tiếng Anh.
//   3. Không khớp → client hiển thị `detail` của BE (tiếng Anh).
//
// Cách dùng (từ thư mục gốc repo BE):
//   node tools/audit-error-codes.mjs                 # liệt kê mã BE chưa có câu riêng ở FE + mobile
//   node tools/audit-error-codes.mjs --json          # in JSON để so sánh trong CI
//   node tools/audit-error-codes.mjs --fail-on-missing  # exit 1 nếu còn mã chưa map (dùng trong CI)
//
// Mặc định đọc `../GrottoWorks_FE` và `../GrottoWokrs_Mobile` (hai repo sibling); đổi bằng
// `--fe=<path>` / `--mobile=<path>`.
import { readdirSync, statSync, readFileSync } from 'node:fs';
import { join, extname, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const beRoot = join(here, '..');
const args = process.argv.slice(2);
const argValue = (name, fallback) => {
  const hit = args.find((a) => a.startsWith(`--${name}=`));
  return hit ? hit.slice(name.length + 3) : fallback;
};
const feRoot = argValue('fe', join(beRoot, '..', 'GrottoWorks_FE'));
const mobileRoot = argValue('mobile', join(beRoot, '..', 'GrottoWokrs_Mobile'));
const asJson = args.includes('--json');
const failOnMissing = args.includes('--fail-on-missing');

const CODE = '[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+';
// Quét trên TOÀN VĂN file để bắt được lời gọi xuống dòng:
//   throw new DomainRuleException(
//       "WORK_AREA_NOT_IN_READINESS_REVIEW",
const PATTERNS = [
  new RegExp(`Exception(?:\\.\\w+)?\\(\\s*"(${CODE})"`, 'g'),
  new RegExp(`\\bcode:\\s*"(${CODE})"`, 'g'),
];

const walk = (dir, out = []) => {
  for (const name of readdirSync(dir)) {
    if (['bin', 'obj', 'node_modules', '.git'].includes(name)) continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path, out);
    else out.push(path);
  }
  return out;
};

/** @returns {Map<string, string[]>} code -> ["Service/File.cs:line"] */
function collectBeCodes() {
  const codes = new Map();
  const roots = ['Services', 'BuildingBlocks'].map((dir) => join(beRoot, dir));
  for (const root of roots) {
    for (const file of walk(root)) {
      if (extname(file) !== '.cs') continue;
      const relative = file.slice(beRoot.length + 1).replace(/\\/g, '/');
      const text = readFileSync(file, 'utf8');
      const lineOf = (index) => text.slice(0, index).split('\n').length;
      for (const pattern of PATTERNS) {
        pattern.lastIndex = 0;
        let match;
        while ((match = pattern.exec(text))) {
          const code = match[1];
          const where = `${relative}:${lineOf(match.index)}`;
          if (!codes.has(code)) codes.set(code, []);
          if (!codes.get(code).includes(where)) codes.get(code).push(where);
        }
      }
    }
  }
  return codes;
}

/** i18n phẳng (`keySeparator: false`) → lấy các key bắt đầu bằng `errors.` / `errorsByKind.`. */
function collectClientKeys(localePath) {
  const json = JSON.parse(readFileSync(localePath, 'utf8'));
  const codes = new Set();
  const kinds = new Set();
  for (const key of Object.keys(json)) {
    if (key.startsWith('errors.')) codes.add(key.slice('errors.'.length));
    else if (key.startsWith('errorsByKind.')) kinds.add(key.slice('errorsByKind.'.length));
  }
  return { codes, kinds };
}

/** Mirror of KIND_RULES in `GrottoWorks_FE/src/lib/api.ts` and mobile `src/api/client.ts`. */
const KIND_RULES = [
  [/(_VERSION_MISMATCH|^INVALID_IF_MATCH|^SOURCE_VERSION_INVALID)$/, 'versionConflict'],
  [/(_NOT_FOUND|_UNKNOWN)$/, 'notFound'],
  [/(_REASON_REQUIRED|_NOTE_REQUIRED|_REQUIRED|_ID_REQUIRED)$/, 'required'],
  [/(_INVALID_STATE|_NOT_OPEN|_NOT_READY|_NOT_COMPLETED|_NOT_ACCEPTED|_NOT_APPROVED|_NOT_LOCKED)$/, 'invalidState'],
  [/(_ACCESS_DENIED|_SCOPE_DENIED|_DENIED|^FORBIDDEN|^UNAUTHENTICATED)$/, 'denied'],
  [/(_ALREADY_EXISTS|_EXISTS|_DUPLICATE|_CONFLICT|_ALREADY_POSTED|_ALREADY_RECORDED|_ALREADY_RECEIVED|_ALREADY_LINKED|_ALREADY_LOCKED|_RESERVED|_ATTACHED)$/, 'conflict'],
  [/(_UNAVAILABLE|_SERVICE_UNAVAILABLE|_STORAGE_UNAVAILABLE)$/, 'unavailable'],
  [/(_TOO_LONG|_TOO_MANY|_LIMIT_EXCEEDED|_LIMIT|_EXCEEDED|CAPACITY)/, 'tooMany'],
  [/(_PENDING|_IN_PROGRESS|_SYNC_PENDING)$/, 'pending'],
  [/(_MISMATCH|_INVALID|_INVALID_RANGE|_DATE_RANGE_INVALID|_OUT_OF_SEASON|_OUT_OF_WORK_AREA|_IN_FUTURE|_IN_PAST|^INVALID_)/, 'invalid'],
];

const kindOf = (code) => KIND_RULES.find(([pattern]) => pattern.test(code))?.[1];

const beCodes = collectBeCodes();
const report = (root, label) => {
  const vi = collectClientKeys(join(root, 'src/locales/vi.json'));
  const en = collectClientKeys(join(root, 'src/locales/en.json'));
  const exact = (code) => vi.codes.has(code) && en.codes.has(code);
  const viaKind = (code) => {
    const kind = kindOf(code);
    return Boolean(kind && vi.kinds.has(kind) && en.kinds.has(kind));
  };
  const all = [...beCodes.keys()].sort();
  return {
    label,
    beCodes: all.length,
    exactMapped: all.filter(exact).length,
    kindMapped: all.filter((code) => !exact(code) && viaKind(code)).length,
    unmapped: all.filter((code) => !exact(code) && !viaKind(code)),
    stale: [...vi.codes].filter((code) => !beCodes.has(code)).sort(),
    parityMissingEn: [...vi.codes].filter((code) => !en.codes.has(code)).sort(),
    missingKinds: [...new Set(KIND_RULES.map(([, kind]) => kind))].filter(
      (kind) => !vi.kinds.has(kind) || !en.kinds.has(kind),
    ),
  };
};

const results = [report(feRoot, 'FE'), report(mobileRoot, 'mobile')];

if (asJson) {
  console.log(JSON.stringify({ beCodes: [...beCodes.keys()].sort(), clients: results }, null, 2));
} else {
  for (const result of results) {
    console.log(`\n== ${result.label} ==`);
    console.log(`  mã BE: ${result.beCodes}`);
    console.log(`  có câu riêng (errors.<CODE>): ${result.exactMapped}`);
    console.log(`  dùng câu theo nhóm (errorsByKind.*): ${result.kindMapped}`);
    console.log(`  CÒN LỘ detail tiếng Anh: ${result.unmapped.length}`);
    if (result.unmapped.length) console.log(`    ${result.unmapped.join(', ')}`);
    if (result.missingKinds.length) console.log(`  thiếu errorsByKind: ${result.missingKinds.join(', ')}`);
    if (result.parityMissingEn.length) console.log(`  thiếu bản en: ${result.parityMissingEn.join(', ')}`);
    if (result.stale.length) console.log(`  key không match pattern quét (có thể do client tự sinh hoặc endpoint tự dựng ProblemDetails): ${result.stale.join(', ')}`);
  }
}

const failing = results.some((result) => result.unmapped.length || result.missingKinds.length || result.parityMissingEn.length);
process.exit(failOnMissing && failing ? 1 : 0);
