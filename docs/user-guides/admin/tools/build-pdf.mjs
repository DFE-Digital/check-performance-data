// Renders ../../admin-user-guide.md to ../../admin-user-guide.pdf.
// Fails if the Markdown references an image that does not exist, so a stale
// captures.json cannot produce a PDF with holes in it.
import { chromium } from 'playwright';
import { marked } from 'marked';
import { readFile, writeFile, access } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const guideDir = resolve(here, '..', '..');
const mdPath = join(guideDir, 'admin-user-guide.md');
const htmlPath = join(guideDir, 'admin-user-guide.html'); // temporary, gitignored
const pdfPath = join(guideDir, 'admin-user-guide.pdf');

const md = await readFile(mdPath, 'utf8');
const missing = [];
for (const m of md.matchAll(/!\[[^\]]*\]\(([^)]+)\)/g)) {
  try { await access(join(guideDir, m[1])); } catch { missing.push(m[1]); }
}
if (missing.length) { console.error('Missing images:\n  ' + missing.join('\n  ')); process.exit(1); }

const css = await readFile(join(here, 'guide.css'), 'utf8');
const body = marked.parse(md, { gfm: true });
const title = (md.match(/^#\s+(.+)$/m) ?? [, 'Admin user guide'])[1];
const html = `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>${title}</title><style>${css}</style></head><body>${body}</body></html>`;
await writeFile(htmlPath, html, 'utf8');

const browser = await chromium.launch();
const page = await browser.newPage();
await page.goto(pathToFileURL(htmlPath).href, { waitUntil: 'networkidle' });
await page.pdf({
  path: pdfPath,
  format: 'A4',
  printBackground: true,
  margin: { top: '20mm', bottom: '20mm', left: '18mm', right: '18mm' },
  displayHeaderFooter: true,
  headerTemplate: '<span></span>',
  footerTemplate: `<div style="font-size:9px;width:100%;padding:0 18mm;color:#505a5f;display:flex;justify-content:space-between;">
    <span>${title}</span><span>Page <span class="pageNumber"></span> of <span class="totalPages"></span></span></div>`,
});
await browser.close();
console.log(`wrote ${pdfPath}`);
