// Captures the screenshots the in-app guide "How to use the CMS" shows, from the local Docker
// stack, into the web project's wwwroot/assets/cms-help.
//
// Prerequisite: `docker compose up -d --build` from the repo root and /healthcheck = 200.
// Signs in through the dev-only impersonation route, so this only works where
// Dev:ToolsEnabled is on (the compose stack). Never point it at a deployed environment.
//
//   npm run capture                      every screenshot
//   npm run capture -- versions-tab      only the ones named
//   CPD_BASE_URL=http://localhost:8086 npm run capture
//
// Before the first screenshot it imports the example page from demo-content.mjs, so the pictures
// do not depend on what happens to be in the database. Each screenshot is one entry in `shots`
// below: where to go, what to do there, and which part of the screen to keep.
import { chromium } from 'playwright';
import { mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { demoBundle, demoPageId, demoPagePath } from './demo-content.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const outDir = resolve(here, '..', '..', '..', '..', 'src', 'DfE.CheckPerformanceData.Web', 'wwwroot', 'assets', 'cms-help');
const baseUrl = (process.env.CPD_BASE_URL ?? 'http://localhost:8080').replace(/\/$/, '');
const only = process.argv.slice(2);

const helpRootId = '00000000-cd94-4a01-8f01-000000000003';
const guidanceRootId = '00000000-cd94-4a01-8f01-000000000004';
const guideHomeId = 'a969611f-33ad-518d-9ce5-dcd82a9b2656';
const editUrl = `/admin/pages/${demoPageId}/edit`;

await mkdir(outDir, { recursive: true });
const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1100, height: 800 }, deviceScaleFactor: 1 });
await context.addCookies([{
  name: 'cookies_policy', value: '{"analytics":false}', domain: new URL(baseUrl).hostname, path: '/',
}]);
const page = await context.newPage();

const health = await page.request.get(`${baseUrl}/healthcheck`);
if (!health.ok()) throw new Error(`Stack not healthy: ${health.status()}`);

// ---- helpers -------------------------------------------------------------------------------

async function signInAs(role) {
  await page.goto(`${baseUrl}/dev/impersonate/${role}`);
}

// The observability pages hold a stream open, so 'networkidle' is not safe everywhere. Everything
// captured here settles, and waiting for it keeps fonts and charts from being caught half-drawn.
async function go(url) {
  // Going to another tab of the page already open changes only the fragment, which loads nothing.
  let response = await page.goto(baseUrl + url, { waitUntil: 'networkidle' });
  if (!response) response = await page.reload({ waitUntil: 'networkidle' });
  if (!response || response.status() >= 400) throw new Error(`HTTP ${response?.status()} for ${url}`);
  if (await page.locator('.govuk-cookie-banner:visible').count()) throw new Error('cookie banner visible');
}

// Saves the rectangle that encloses every element given, with a margin. Elements may be
// selectors or locators. With no elements it saves the visible window.
//
// `inView` is for anything that closes when the window scrolls or resizes, such as the tree's
// right-click menu: the elements must already be on screen, and only the window is captured.
// `maxHeight` keeps the top of a long screen and drops the rest; `maxWidth` does the same for
// the left of a wide one.
async function save(name, { around = [], pad = 12, fullPage = false, inView = false, maxHeight = null, maxWidth = null } = {}) {
  const path = join(outDir, `${name}.png`);
  if (around.length === 0) {
    await page.screenshot({ path, fullPage });
    return;
  }
  let box = null;
  for (const item of around) {
    const locator = typeof item === 'string' ? page.locator(item).first() : item;
    await locator.waitFor({ state: 'visible' });
    const b = await locator.evaluate((el) => {
      const r = el.getBoundingClientRect();
      return { x: r.x, y: r.y, width: r.width, height: r.height, scrollX: window.scrollX, scrollY: window.scrollY };
    });
    if (!inView) { b.x += b.scrollX; b.y += b.scrollY; }
    box = box === null ? b : {
      x: Math.min(box.x, b.x),
      y: Math.min(box.y, b.y),
      width: Math.max(box.x + box.width, b.x + b.width) - Math.min(box.x, b.x),
      height: Math.max(box.y + box.height, b.y + b.height) - Math.min(box.y, b.y),
    };
  }
  const size = inView ? page.viewportSize() : await page.evaluate(() => ({
    width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight,
  }));
  if (maxHeight) box.height = Math.min(box.height, maxHeight);
  if (maxWidth) box.width = Math.min(box.width, maxWidth);
  const x = Math.max(0, box.x - pad);
  const y = Math.max(0, box.y - pad);
  await page.screenshot({
    path,
    fullPage: !inView,
    clip: {
      x, y,
      width: Math.min(size.width - x, box.width + pad * 2),
      height: Math.min(size.height - y, box.height + pad * 2),
    },
  });
}

// The public layout has one <main>. The admin layout nests a second inside it, beside the menu.
const main = 'main#main-content.govuk-main-wrapper .govuk-width-container, main#main-content >> nth=0';
const adminMain = '.admin-main';
const adminMenu = '#admin-rail';

async function openDetails(summaryText, nth = 0) {
  const summary = page.locator('summary').filter({ hasText: summaryText }).nth(nth);
  await summary.scrollIntoViewIfNeeded();
  await summary.click();
  return summary.locator('xpath=..');
}

// ---- set-up --------------------------------------------------------------------------------

// Imports the example page through Content staging, overwriting whatever a previous run left.
// The review screen is one of the screenshots, so it is taken on the way through.
async function importExamplePage() {
  const dir = join(tmpdir(), 'cpd-cms-guide');
  await mkdir(dir, { recursive: true });
  const file = join(dir, 'example-page.json');
  await writeFile(file, JSON.stringify(demoBundle()), 'utf8');

  await go('/admin/content-staging');
  await page.setInputFiles('#bundle', file);
  await Promise.all([
    page.waitForURL(/content-staging\/preview/i),
    page.getByRole('button', { name: 'Preview import' }).click(),
  ]);
  await page.waitForLoadState('networkidle');
  if (await page.locator('#g-replace').isEnabled()) await page.locator('#g-replace').check();
  if (wanted('staging-review')) {
    await save('staging-review', { around: [adminMain] });
    console.log('ok   staging-review');
  }
  await Promise.all([
    page.waitForNavigation(),
    page.getByRole('button', { name: 'Confirm import' }).click(),
  ]);
  await page.waitForLoadState('networkidle');
  if (wanted('staging-import-complete')) {
    await save('staging-import-complete', { around: ['.govuk-notification-banner'] });
    console.log('ok   staging-import-complete');
  }
  await rm(dir, { recursive: true, force: true });
}

// The Deleted pages screen is only worth a picture with something in it.
async function ensureADeletedPage() {
  await go('/admin/pages/deleted');
  if (await page.getByRole('cell', { name: 'Summer checking exercise' }).count()) return;
  await go(`/admin/pages/new?parentId=${guidanceRootId}`);
  await page.getByLabel('Page type').selectOption({ label: 'Folder' });
  await page.getByLabel('URL segment').fill('summer-checking-exercise');
  await page.getByLabel('Title').fill('Summer checking exercise');
  await Promise.all([page.waitForNavigation(), page.getByRole('button', { name: 'Create page' }).click()]);
  await go(`/admin/pages/${guidanceRootId}`);
  const row = page.getByRole('row').filter({ hasText: 'Summer checking exercise' });
  await Promise.all([page.waitForNavigation(), row.locator('a[href$="/delete"]').click()]);
  await Promise.all([page.waitForNavigation(), page.getByRole('button', { name: 'Delete page' }).click()]);
}

// Search analytics needs searches to chart. Seeds a week of sample data if there is none.
async function ensureSearchData() {
  await go('/admin/Search');
  const searches = Number((await page.locator('.sa-tiles').innerText()).match(/[\d,]+/)?.[0].replace(/,/g, '') ?? 0);
  if (searches >= 200) return;
  await go('/admin/test-data/sample-search-data');
  await page.locator('#preset-week').check();
  await page.locator('form[action="/admin/test-data/sample-search-data"] button[type=submit]').click();
  await page.waitForURL(/sample-search-data/, { timeout: 600000 });
  await page.locator('.govuk-notification-banner--success, .govuk-panel--confirmation').first().waitFor({ timeout: 600000 });
}

function wanted(name) {
  return only.length === 0 || only.includes(name);
}

// ---- screenshots ---------------------------------------------------------------------------

const shots = [
  // Finding your way in
  { name: 'edit-chip', role: 'editor', run: async () => {
    await go(`/${demoPagePath}`);
    await save('edit-chip', { around: ['header', 'h1'], pad: 16 });
  } },
  { name: 'admin-landing', role: 'editor', run: async () => {
    await go('/admin');
    await save('admin-landing');
  } },
  { name: 'pages-list', run: async () => {
    await go(`/admin/pages/${guidanceRootId}`);
    await save('pages-list', { around: ['.admin-layout'], maxHeight: 760 });
  } },
  { name: 'pages-row-actions', run: async () => {
    await go(`/admin/pages/${guidanceRootId}`);
    await save('pages-row-actions', { around: ['main table'] });
  } },

  // Creating and editing
  { name: 'create-page', run: async () => {
    await go(`/admin/pages/new?parentId=${guidanceRootId}`);
    await page.getByLabel('URL segment').fill('spring-checking-exercise');
    await page.getByLabel('Title').fill('Spring checking exercise');
    await save('create-page', { around: [adminMain] });
  } },
  { name: 'editor-overview', run: async () => {
    await go(editUrl);
    await save('editor-overview', { around: ['.govuk-caption-l, h1', page.locator('.govuk-tabs__panel:visible')], maxHeight: 1150 });
  } },
  { name: 'editor-imported-warning', run: async () => {
    await go(`/admin/pages/${guideHomeId}/edit`);
    await save('editor-imported-warning', { around: ['.govuk-caption-l', '[data-imported-content] + p', '.govuk-tabs__list'] });
  } },
  { name: 'editor-add-content', run: async () => {
    await go(editUrl);
    const details = await openDetails('Add content here');
    await save('editor-add-content', { around: [details], pad: 20 });
  } },
  { name: 'editor-rich-text', run: async () => {
    await go(editUrl);
    const details = await openDetails('Edit rich text');
    await page.locator('.tox-tinymce').first().waitFor();
    await page.waitForTimeout(500);
    await save('editor-rich-text', { around: [details], pad: 20 });
  } },
  { name: 'editor-heading', run: async () => {
    await go(editUrl);
    const details = await openDetails('Edit heading');
    await save('editor-heading', { around: [details], pad: 20 });
  } },
  { name: 'editor-search', run: async () => {
    await go(editUrl);
    const details = await openDetails('Edit search');
    await details.locator('select[name="props[searchIn]"]').selectOption({ label: 'Chosen pages' });
    await page.waitForTimeout(300);
    await save('editor-search', { around: [details], pad: 20 });
  } },
  { name: 'editor-page-navigation', run: async () => {
    await go(editUrl);
    const details = await openDetails('Edit page navigation');
    await save('editor-page-navigation', { around: [details], pad: 20 });
  } },
  { name: 'editor-summary-list', run: async () => {
    await go(editUrl);
    const details = await openDetails('Edit summary list');
    await save('editor-summary-list', { around: [details], pad: 20 });
  } },
  { name: 'editor-buttons', run: async () => {
    await go(editUrl);
    const save_ = page.getByRole('button', { name: 'Save', exact: true }).last();
    await save('editor-buttons', { around: [save_, page.getByRole('button', { name: 'Publish draft' }), page.getByRole('button', { name: 'Unpublish' }).last()], pad: 20 });
  } },
  { name: 'public-page', role: 'user', run: async () => {
    await go(`/${demoPagePath}`);
    await save('public-page', { around: [main] });
  } },

  // Properties
  { name: 'properties-tab', run: async () => {
    await go(`${editUrl}#properties`);
    await page.reload({ waitUntil: 'networkidle' });
    await save('properties-tab', { around: ['.govuk-tabs__list', page.locator('.govuk-tabs__panel:visible')] });
  } },
  { name: 'change-url', run: async () => {
    await go(`${editUrl}#properties`);
    await page.reload({ waitUntil: 'networkidle' });
    await page.getByLabel('URL segment').fill('autumn-exercise');
    await page.getByRole('button', { name: 'Save details' }).click();
    const dialog = page.locator('.govuk-modal-dialogue:visible, dialog[open]').first();
    await dialog.waitFor();
    await page.waitForTimeout(300);
    await save('change-url', { around: [dialog], pad: 24 });
  } },

  // Versions and publishing
  { name: 'versions-tab', viewport: { width: 1280, height: 900 }, run: async () => {
    await go(`${editUrl}#versions`);
    await page.reload({ waitUntil: 'networkidle' });
    await save('versions-tab', { around: ['#version-history', page.locator('.govuk-tabs__panel:visible table')] });
  } },
  { name: 'draft-preview', role: 'editor', run: async () => {
    await go('/help');
    await save('draft-preview', { around: ['.govuk-breadcrumbs', '.govuk-notification-banner', 'h1'] });
  } },

  // Content blocks
  { name: 'content-blocks', run: async () => {
    await go('/admin/content-blocks');
    await save('content-blocks', { around: ['h1', 'main table tbody tr >> nth=1'] });
  } },
  { name: 'content-block-edit', run: async () => {
    await go('/admin/content-blocks');
    await page.getByRole('row').filter({ hasText: 'home-content' }).getByRole('link', { name: 'Edit' }).click();
    await page.locator('.tox-tinymce').first().waitFor();
    await page.waitForTimeout(500);
    const form = page.locator('main form').filter({ has: page.locator('.tox-tinymce') }).first();
    await save('content-block-edit', { around: [form], pad: 16, maxWidth: 660 });
  } },
  { name: 'content-block-in-place', role: 'editor', run: async () => {
    await go('/');
    const pencil = page.locator('a[href*="edit=home-content"]');
    await pencil.scrollIntoViewIfNeeded();
    await pencil.hover();
    await save('content-block-in-place', { around: ['h1', pencil, page.getByRole('button', { name: 'Start now' })], pad: 20 });
  } },
  { name: 'content-block-in-place-form', role: 'editor', run: async () => {
    await go('/?edit=home-content');
    const form = page.locator('form').filter({ has: page.getByRole('button', { name: 'Save' }) }).first();
    await save('content-block-in-place-form', { around: [form], pad: 20 });
  } },
  { name: 'content-block-versions', run: async () => {
    await go('/content-block/versions/home-content');
    await save('content-block-versions', { around: ['h1', 'main table'] });
  } },

  // Search
  { name: 'search-results', role: 'user', run: async () => {
    await go('/search?q=checking+exercise');
    await save('search-results', { around: [main] });
  } },
  { name: 'search-this-page', role: 'user', run: async () => {
    await go(`/${demoPagePath}`);
    const box = page.getByLabel('Search this page');
    await box.evaluate((el) => window.scrollTo(0, el.getBoundingClientRect().top + window.scrollY - 120));
    await box.click();
    await box.pressSequentially('evidence', { delay: 40 });
    await page.waitForTimeout(800);
    await save('search-this-page', { around: [page.getByText('Search this page', { exact: true }), page.locator('.autocomplete__option').last()], pad: 24, inView: true });
  } },
  { name: 'search-guide', role: 'user', run: async () => {
    await go('/help/how-to-use-the-cms');
    const box = page.getByRole('searchbox').or(page.locator('main input[type=search], main input[name=q]')).first();
    await box.evaluate((el) => window.scrollTo(0, el.getBoundingClientRect().top + window.scrollY - 120));
    await box.click();
    await box.pressSequentially('publish', { delay: 40 });
    await page.locator('.autocomplete__option').nth(2).waitFor();
    await save('search-guide', { around: [page.getByText('Search this guide', { exact: true }), page.locator('.autocomplete__option').last(), page.getByRole('button', { name: 'Search' })], pad: 24, inView: true });
  } },
  { name: 'search-feedback', role: 'user', run: async () => {
    await go('/search?q=timetable');
    await page.getByRole('link', { name: /Send us a note/ }).click();
    await page.waitForLoadState('networkidle');
    await save('search-feedback', { around: [main] });
  } },

  // The page tree
  { name: 'tree', run: async () => {
    await go(`/admin/pages/${guidanceRootId}`);
    await save('tree', { around: [adminMenu], maxHeight: 520 });
  } },
  { name: 'tree-menu', run: async () => {
    await go(`/admin/pages/${guidanceRootId}`);
    const node = page.locator(`[data-page-id="${demoPageId}"]`).first();
    await page.locator(adminMenu).scrollIntoViewIfNeeded();
    await page.evaluate(() => window.scrollBy(0, 200));
    await node.click({ button: 'right', position: { x: 60, y: 10 } });
    const menu = page.locator('[aria-label="Page actions"]');
    await menu.waitFor();
    await save('tree-menu', { around: [page.locator(`[data-page-id="${guidanceRootId}"]`).first(), menu], pad: 24, inView: true });
  } },
  { name: 'tree-drag', run: async () => {
    await go(`/admin/pages/${guidanceRootId}`);
    await page.evaluate(() => window.scrollBy(0, 200));
    const from = page.locator(`.admin-nav-tree [data-page-id="${demoPageId}"]`);
    const to = page.locator(".admin-nav-tree [data-page-path=\"guidance/ks4-checking\"]");
    // A real drag cannot be scripted, so the same events the browser would raise are sent by hand.
    await from.evaluate((source, target) => {
      const dataTransfer = new DataTransfer();
      const rect = target.getBoundingClientRect();
      const at = { bubbles: true, cancelable: true, dataTransfer, clientX: rect.left + 40, clientY: rect.top + 2 };
      source.dispatchEvent(new DragEvent('dragstart', at));
      target.dispatchEvent(new DragEvent('dragover', at));
    }, await to.elementHandle());
    await page.locator('.cpb-tree-placeholder').waitFor({ state: 'attached', timeout: 5000 });
    await page.waitForTimeout(300);
    await save('tree-drag', { around: [page.locator(`[data-page-id="${guidanceRootId}"]`).first(), from, '.cpb-tree-placeholder'], pad: 28, inView: true });
    await from.dispatchEvent('dragend');
  } },
  { name: 'delete-page', run: async () => {
    await go(`/admin/pages/${demoPageId}/delete`);
    await save('delete-page', { around: [adminMain] });
  } },
  { name: 'deleted-pages', run: async () => {
    await go('/admin/pages/deleted');
    await save('deleted-pages', { around: [adminMain] });
  } },

  // Moving content between environments
  { name: 'staging', run: async () => {
    await go('/admin/content-staging');
    await save('staging', { around: [adminMain] });
  } },
  { name: 'staging-select', run: async () => {
    await go('/admin/content-staging/select');
    await page.getByLabel('Filter').fill('checking');
    await page.waitForTimeout(300);
    await save('staging-select', { around: ['h1', 'main table >> nth=0'] });
  } },

  // Search analytics and feedback
  { name: 'analytics', setUp: ensureSearchData, viewport: { width: 1280, height: 900 }, run: async () => {
    await go('/admin/Search');
    await save('analytics', { around: ['.sa-tiles', '.sa-chart-panel >> nth=0'] });
  } },
  { name: 'analytics-heatmap', viewport: { width: 1280, height: 900 }, run: async () => {
    await go('/admin/Search');
    await save('analytics-heatmap', { around: ['.sa-heatmap-card'] });
  } },
  { name: 'analytics-zero-results', viewport: { width: 1280, height: 900 }, run: async () => {
    await go('/admin/Search/ZeroResults');
    await save('analytics-zero-results', { around: [adminMain], maxHeight: 900 });
  } },
  { name: 'feedback-inbox', viewport: { width: 1280, height: 900 }, run: async () => {
    await go('/admin/Messages/Inbox');
    await save('feedback-inbox', { around: [adminMain], maxHeight: 900 });
  } },

  // Administration
  { name: 'role-settings', run: async () => {
    await go('/admin/system/roles');
    await save('role-settings', { around: [adminMain] });
  } },
  { name: 'settings', run: async () => {
    await go('/admin/settings');
    await page.getByLabel(/Filter settings/i).fill('CMS:');
    await page.waitForTimeout(300);
    await save('settings', { around: [adminMain] });
  } },
  { name: 'site-assets', run: async () => {
    await go('/admin/site-assets');
    await save('site-assets', { around: [adminMain] });
  } },
  { name: 'logs', run: async () => {
    await go('/admin/system-administration/logs');
    await save('logs', { around: [adminMain], maxHeight: 900 });
  } },
  { name: 'audit-log', run: async () => {
    await go('/admin/audit-log');
    const activity = page.getByLabel('Filter by activity');
    console.log('     activities: ' + (await activity.locator('option').allInnerTexts()).map((o) => o.trim()).join(', '));
    await activity.selectOption({ label: 'Content page version' });
    await Promise.all([page.waitForNavigation(), page.getByRole('button', { name: 'Apply filters' }).click()]);
    await page.waitForLoadState('networkidle');
    await save('audit-log', { around: [adminMain], maxHeight: 900 });
  } },
];

// ---- run -----------------------------------------------------------------------------------

await signInAs('admin');
await importExamplePage();
if (wanted('deleted-pages')) await ensureADeletedPage();

let failures = 0;
let role = 'admin';
for (const shot of shots) {
  if (!wanted(shot.name)) continue;
  try {
    const next = shot.role ?? 'admin';
    if (next !== role) { await signInAs(next); role = next; }
    await page.setViewportSize(shot.viewport ?? { width: 1100, height: 800 });
    if (shot.setUp) await shot.setUp();
    await shot.run();
    console.log(`ok   ${shot.name}`);
  } catch (err) {
    failures++;
    console.error(`FAIL ${shot.name}: ${err.message.split('\n')[0]}`);
  }
}
await browser.close();
if (failures) { console.error(`${failures} capture(s) failed`); process.exit(1); }
