// Puts the guide you have just built into the local Docker stack, so you can read it in the
// browser without rebuilding the image. It uploads the bundle through Content staging and
// overwrites the guide pages already there, which is what a new release does at start-up.
//
// Prerequisite: `npm run build`, and the compose stack running. Signs in through the dev-only
// impersonation route. Never point it at a deployed environment.
import { chromium } from 'playwright';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const bundle = resolve(here, '..', '..', '..', '..', 'src', 'DfE.CheckPerformanceData.Web', 'Data', 'Import', 'cms-guide.json');
const baseUrl = (process.env.CPD_BASE_URL ?? 'http://localhost:8080').replace(/\/$/, '');

const browser = await chromium.launch();
const page = await browser.newPage();
try {
  await page.goto(`${baseUrl}/dev/impersonate/admin`);
  await page.goto(`${baseUrl}/admin/content-staging`, { waitUntil: 'networkidle' });
  await page.setInputFiles('#bundle', bundle);
  await Promise.all([
    page.waitForURL(/content-staging\/preview/i),
    page.getByRole('button', { name: 'Preview import' }).click(),
  ]);
  if (await page.locator('#g-replace').isEnabled()) await page.locator('#g-replace').check();
  await Promise.all([
    page.waitForNavigation(),
    page.getByRole('button', { name: 'Confirm import' }).click(),
  ]);
  const summary = await page.locator('.govuk-notification-banner').first().innerText();
  console.log(summary.replace(/\s+/g, ' ').trim());
  console.log(`${baseUrl}/help/how-to-use-the-cms`);
} finally {
  await browser.close();
}
