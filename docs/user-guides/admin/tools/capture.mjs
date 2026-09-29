// Captures every screenshot listed in captures.json from the local Docker stack.
// Prerequisite: `docker compose up -d --build` from the repo root and /healthcheck = 200.
// Signs in through the dev-only impersonation route, so this only works where
// Dev:ToolsEnabled is on (the compose stack). Never point it at a deployed environment.
import { chromium } from 'playwright';
import { readFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const manifest = JSON.parse(await readFile(join(here, 'captures.json'), 'utf8'));
const outDir = join(here, '..', 'images');
await mkdir(outDir, { recursive: true });

const only = process.argv.slice(2); // optional: names to capture
const browser = await chromium.launch();
const context = await browser.newContext({ viewport: manifest.viewport, deviceScaleFactor: 1 });
const origin = new URL(manifest.baseUrl);
await context.addCookies([{
  name: 'cookies_policy', value: '{"analytics":false}', domain: origin.hostname, path: '/',
}]);
const page = await context.newPage();

const health = await page.request.get(`${manifest.baseUrl}/healthcheck`);
if (!health.ok()) throw new Error(`Stack not healthy: ${health.status()}`);
await page.goto(`${manifest.baseUrl}/dev/impersonate/admin`);

let failures = 0;
for (const c of manifest.captures) {
  if (only.length && !only.includes(c.name)) continue;
  try {
    const response = await page.goto(manifest.baseUrl + c.url, { waitUntil: 'networkidle' });
    if (!response || response.status() !== 200) throw new Error(`HTTP ${response?.status()}`);
    if (c.clickFirst) {
      await page.locator(c.clickFirst).first().click();
      await page.waitForLoadState('networkidle');
    }
    if (c.waitFor) await page.locator(c.waitFor).first().waitFor();
    const banner = page.locator('.govuk-cookie-banner:visible');
    if (await banner.count()) throw new Error('cookie banner visible');
    await page.screenshot({ path: join(outDir, `${c.name}.png`), fullPage: c.fullPage !== false });
    console.log(`ok   ${c.name}`);
  } catch (err) {
    failures++;
    console.error(`FAIL ${c.name}: ${err.message}`);
  }
}
await browser.close();
if (failures) { console.error(`${failures} capture(s) failed`); process.exit(1); }
