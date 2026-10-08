// Builds the in-app guide "How to use the CMS" from the Markdown in ../pages.
//
// Writes one file: the content-staging bundle the application imports at start-up,
// src/DfE.CheckPerformanceData.Web/Data/Import/cms-guide.json, which Data/Import/manifest.json lists.
//
// Each Markdown file is one page of the guide. Its front matter says where the page sits:
//
//   ---
//   title: Create a page
//   subtitle: Add a page to the site and choose what kind of page it is
//   parent: for-editors        # the file name of the page above it; leave out for the guide's home
//   order: 0                   # position among the pages with the same parent
//   keywords: new add          # optional extra search terms
//   ---
//
// The file name is the last part of the page's address. index.md is the guide's home page.
//
// The body is ordinary Markdown. Each `## heading` becomes a Heading widget and the text beneath
// it a Rich text widget, so the guide is built from the same widgets it describes. Three things
// go beyond plain Markdown:
//
//   > **Warning** text         a GOV.UK warning
//   > text                     GOV.UK inset text
//   ```widget                  any widget, given as JSON: { "type": "search", "props": { ... } }
//   ```cards                   a row of Card widgets, given as a JSON array of { title, body, href }
//   ---                        a Divider across the full width of the page. A `##` heading straight
//                              after it runs the full width too.
//
// A Divider is also put before every `##` heading after the first, to space the sections out.
//
// Links to other pages are written as links to their Markdown files, and images as paths to the
// PNG files, so the pages also read properly on GitHub. Both are rewritten for the application.
//
// The build fails on a link to a page that does not exist, an image that has not been captured,
// or an image without alt text.
import { marked } from 'marked';
import { createHash } from 'node:crypto';
import { readFile, readdir, writeFile, access } from 'node:fs/promises';
import { dirname, join, resolve, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const pagesDir = resolve(here, '..', 'pages');
const repoRoot = resolve(here, '..', '..', '..', '..');
const imagesDir = join(repoRoot, 'src', 'DfE.CheckPerformanceData.Web', 'wwwroot', 'assets', 'cms-help');
const bundlePath = join(repoRoot, 'src', 'DfE.CheckPerformanceData.Web', 'Data', 'Import', 'cms-guide.json');

const helpRootId = '00000000-cd94-4a01-8f01-000000000003';
const guidePath = 'help/how-to-use-the-cms';
const imageUrl = '/assets/cms-help/';
const problems = [];

// ---- identity ------------------------------------------------------------------------------

// Every page has a fixed id worked out from its file name, so a rebuilt guide updates the pages
// an earlier one created instead of adding a second copy. Do not change the namespace or the
// names: environments that already have the guide would stop recognising it.
function pageId(name) {
  const namespace = Buffer.from('11111111222233334444555555555555', 'hex');
  const hash = createHash('sha1').update(namespace).update(name, 'utf8').digest();
  hash[6] = (hash[6] & 0x0f) | 0x50;
  hash[8] = (hash[8] & 0x3f) | 0x80;
  const hex = hash.subarray(0, 16).toString('hex');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

// ---- reading the pages ---------------------------------------------------------------------

function parse(file, text) {
  const match = text.replace(/\r\n/g, '\n').match(/^---\n([\s\S]*?)\n---\n([\s\S]*)$/);
  if (!match) throw new Error(`${file}: no front matter`);
  const meta = {};
  for (const line of match[1].split('\n')) {
    const pair = line.match(/^(\w+):\s*(.*?)\s*(?:#.*)?$/);
    if (pair) meta[pair[1]] = pair[2];
  }
  if (!meta.title) throw new Error(`${file}: no title`);
  const slug = basename(file, '.md');
  return {
    slug,
    isHome: slug === 'index',
    title: meta.title,
    subtitle: meta.subtitle || null,
    parent: meta.parent || null,
    order: Number(meta.order ?? 0),
    keywords: meta.keywords || null,
    body: match[2],
  };
}

const pages = [];
for (const file of (await readdir(pagesDir)).filter((f) => f.endsWith('.md')).sort()) {
  pages.push(parse(file, await readFile(join(pagesDir, file), 'utf8')));
}
const bySlug = new Map(pages.map((p) => [p.slug, p]));
const home = bySlug.get('index');
if (!home) throw new Error('pages/index.md is missing');

for (const page of pages) {
  if (page.isHome) {
    page.segment = 'how-to-use-the-cms';
    page.path = guidePath;
    page.id = pageId(guidePath);
    page.parentId = helpRootId;
    continue;
  }
  page.segment = page.slug;
  page.id = pageId(`${guidePath}/${page.slug}`);
}
for (const page of pages.filter((p) => !p.isHome)) {
  const parent = page.parent ? bySlug.get(page.parent) : home;
  if (!parent) throw new Error(`${page.slug}.md: parent '${page.parent}' is not a page`);
  page.parentPage = parent;
  page.parentId = parent.id;
}
const pathOf = (page) => (page.isHome ? guidePath : `${pathOf(page.parentPage)}/${page.segment}`);
for (const page of pages) page.path = pathOf(page);
const childrenOf = (page) => pages.filter((p) => p.parentPage === page).sort((a, b) => a.order - b.order);

// ---- Markdown to GOV.UK HTML ---------------------------------------------------------------

const escapeHtml = (text) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

async function exists(path) {
  try { await access(path); return true; } catch { return false; }
}

function linkTarget(href, from) {
  const [file, anchor] = href.split('#');
  if (!file.endsWith('.md')) return href;
  const target = bySlug.get(basename(file, '.md'));
  if (!target) {
    problems.push(`${from.slug}.md links to ${file}, which is not a page`);
    return href;
  }
  return `/${target.path}${anchor ? `#${anchor}` : ''}`;
}

const imagesUsed = new Set();

function renderer(from) {
  const r = new marked.Renderer();
  const inline = (tokens) => r.parser.parseInline(tokens);

  r.paragraph = ({ tokens }) => {
    // An image on a line of its own is a figure, not a paragraph with a picture in it.
    if (tokens.length === 1 && tokens[0].type === 'image') return r.image(tokens[0]);
    return `<p class="govuk-body">${inline(tokens)}</p>`;
  };
  r.heading = ({ tokens, depth }) => {
    const size = depth <= 3 ? 'm' : 's';
    return `<h${depth} class="govuk-heading-${size}">${inline(tokens)}</h${depth}>`;
  };
  r.list = (token) => {
    const tag = token.ordered ? 'ol' : 'ul';
    const style = token.ordered ? 'number' : 'bullet';
    const items = token.items.map((item) => `<li>${r.parser.parse(item.tokens).replace(/^<p class="govuk-body">([\s\S]*?)<\/p>$/, '$1')}</li>`).join('');
    return `<${tag} class="govuk-list govuk-list--${style}">${items}</${tag}>`;
  };
  r.link = ({ href, tokens }) => {
    const target = linkTarget(href, from);
    const external = /^https?:/.test(target);
    return `<a class="govuk-link" href="${escapeHtml(target)}"${external ? ' rel="noreferrer noopener" target="_blank"' : ''}>${inline(tokens)}</a>`;
  };
  r.image = ({ href, text, title }) => {
    const name = basename(href);
    imagesUsed.add(name);
    if (!text || text.length < 10) problems.push(`${from.slug}.md: image ${name} needs alt text that describes it`);
    const src = imageUrl + name;
    const caption = title ? `<figcaption class="govuk-!-margin-top-2">${escapeHtml(title)}</figcaption>` : '';
    return `<figure><a href="${src}"><img alt="${escapeHtml(text)}" src="${src}" loading="lazy"></a>${caption}</figure>`;
  };
  r.table = (token) => {
    const head = token.header.map((cell) => `<th scope="col" class="govuk-table__header">${inline(cell.tokens)}</th>`).join('');
    const rows = token.rows.map((row) => `<tr class="govuk-table__row">${row.map((cell) => `<td class="govuk-table__cell">${inline(cell.tokens)}</td>`).join('')}</tr>`).join('');
    return `<table class="govuk-table"><thead class="govuk-table__head"><tr class="govuk-table__row">${head}</tr></thead><tbody class="govuk-table__body">${rows}</tbody></table>`;
  };
  r.blockquote = ({ tokens }) => {
    const html = r.parser.parse(tokens);
    const warning = html.match(/^<p class="govuk-body"><strong>Warning<\/strong>\s*([\s\S]*?)<\/p>\s*$/);
    if (warning) {
      return '<div class="govuk-warning-text"><span class="govuk-warning-text__icon" aria-hidden="true">!</span>' +
        `<strong class="govuk-warning-text__text"><span class="govuk-visually-hidden">Warning</span>${warning[1]}</strong></div>`;
    }
    return `<div class="govuk-inset-text">${html}</div>`;
  };
  r.hr = () => '<hr class="govuk-section-break govuk-section-break--l govuk-section-break--visible">';
  return r;
}

// ---- pages to widgets ----------------------------------------------------------------------

const widget = (type, props) => ({ kind: 'widget', type, props });
const region = (layout, ...columns) => ({ kind: 'region', layout, columns });

// Splits a page into widgets: a Heading for each `##`, a Rich text for the text between, and
// whatever the ```widget and ```cards blocks ask for. Returns a list of rows; a row is either
// a run of widgets for the main column or a region that spans the page.
function widgetsFor(page) {
  const r = renderer(page);
  const tokens = marked.lexer(page.body);
  const rows = [];
  // The editor gives every Heading widget an anchor when a page is saved. An imported page has
  // not been through the editor, so the anchors are worked out here, the same way.
  const anchors = new Set();
  const anchorFor = (text) => {
    const slug = text.toLowerCase().replace(/[^a-z0-9\s-]/g, '').replace(/\s+/g, '-').replace(/-{2,}/g, '-').replace(/^-+|-+$/g, '') || 'section';
    let anchor = slug;
    for (let n = 2; anchors.has(anchor); n++) anchor = `${slug}-${n}`;
    anchors.add(anchor);
    return anchor;
  };
  let run = [];
  let pending = [];
  const flushText = () => {
    if (pending.length === 0) return;
    const html = marked.parser(Object.assign(pending, { links: tokens.links }), { renderer: r }).trim();
    if (html) run.push(widget('richtext', { html }));
    pending = [];
  };
  const flushRun = () => {
    flushText();
    if (run.length) rows.push(run);
    run = [];
  };
  const divider = () => widget('divider', {});
  const heading = (token) => ({ ...widget('heading', { level: 2, text: token.text }), anchor: anchorFor(token.text) });
  let headings = 0;
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];
    if (token.type === 'heading' && token.depth === 2) {
      flushText();
      if (headings++ > 0) run.push(divider());
      run.push(heading(token));
    } else if (token.type === 'hr') {
      // A full-width break. A heading straight after it belongs to the break, not to the
      // column of text that follows, so that it lines up with the left edge of the page.
      flushRun();
      const row = [divider()];
      let next = i + 1;
      while (tokens[next]?.type === 'space') next++;
      if (tokens[next]?.type === 'heading' && tokens[next].depth === 2) {
        row.push(heading(tokens[next]));
        headings++;
        i = next;
      }
      rows.push(region('single', row));
    } else if (token.type === 'code' && token.lang === 'widget') {
      flushText();
      const spec = JSON.parse(token.text);
      run.push(widget(spec.type, spec.props ?? {}));
    } else if (token.type === 'code' && token.lang === 'cards') {
      flushRun();
      const cards = JSON.parse(token.text).map((c) => [widget('card', { title: c.title, body: c.body, href: linkTarget(c.href, page) })]);
      const layout = { 2: 'halves', 3: 'thirds', 4: 'quarters' }[cards.length];
      if (!layout) throw new Error(`${page.slug}.md: a row of cards holds 2, 3 or 4`);
      rows.push(region(layout, ...cards));
    } else {
      pending.push(token);
    }
  }
  flushRun();
  return rows;
}

// The left-hand column: a search of the guide alone, where you are in the guide, and on a long
// page a list of its sections.
function sideColumn(page, headingCount) {
  const section = page.isHome || childrenOf(page).length ? page : page.parentPage;
  const label = (text) => widget('richtext', { html: `<p class="govuk-body govuk-!-font-weight-bold govuk-!-margin-top-6 govuk-!-margin-bottom-1">${text}</p>` });
  return [
    // The home page has the search box in its main column instead.
    ...(page.isHome ? [] : [widget('search', {
      label: 'Search this guide', placeholder: '', action: '/search', buttonText: 'Search',
      scope: '', scopePageIds: home.id, searchIn: 'path',
      instant: 'true', showButton: 'true', buttonBelow: 'true', noResultsText: 'Nothing in this guide matches',
    })]),
    widget('pagenav', {
      mode: 'children', childrenParentPath: section.path,
      showSearch: false, searchPath: '', searchLabel: 'Search',
      h1: 'false', h2: 'true', h3: 'true', h4: 'false', h5: 'false', h6: 'false',
    }),
    ...(page.isHome ? [] : [widget('richtext', {
      html: `<p class="govuk-body-s govuk-!-margin-top-4"><a class="govuk-link" href="/${section === page ? home.path : section.path}">Back to ${escapeHtml(section === page ? home.title : section.title)}</a></p>`,
    })]),
    // A contents list for the page itself, once it is long enough to need one.
    ...(headingCount >= 4 ? [
      label('On this page'),
      widget('pagenav', {
        mode: 'headings', childrenParentPath: '', showSearch: false, searchPath: '', searchLabel: 'Search',
        h1: 'false', h2: 'true', h3: 'false', h4: 'false', h5: 'false', h6: 'false',
      }),
    ] : []),
  ];
}

function contentTree(page) {
  const tree = [];
  let first = true;
  const rows = widgetsFor(page);
  const headingCount = JSON.stringify(rows).split('"type":"heading"').length - 1;
  for (const row of rows) {
    if (!Array.isArray(row)) { tree.push(row); continue; }
    // Only the first run sits beside the menu. Later runs follow a full-width row of cards.
    tree.push(first ? region('one-third-two-thirds', sideColumn(page, page.isHome ? 0 : headingCount), row) : region('two-thirds-one-third', row, []));
    first = false;
  }
  return tree;
}

function plainText(tree) {
  const parts = [];
  const walk = (node) => {
    if (Array.isArray(node)) return node.forEach(walk);
    if (node.kind === 'region') return walk(node.columns);
    if (node.type === 'heading') parts.push(node.props.text);
    if (node.type === 'richtext') parts.push(node.props.html.replace(/<figcaption>[\s\S]*?<\/figcaption>/g, ' ').replace(/<[^>]+>/g, ' '));
    if (node.type === 'card') parts.push(node.props.title, node.props.body);
  };
  walk(tree);
  return parts.join(' ').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/\s+/g, ' ').trim();
}

// ---- the bundle ----------------------------------------------------------------------------

// Parents before children, so the importer always finds a page's parent already in place.
const ordered = [];
const visit = (page) => { ordered.push(page); childrenOf(page).forEach(visit); };
visit(home);
if (ordered.length !== pages.length) throw new Error('some pages are not reachable from index.md');

const built = ordered.map((page) => {
  const tree = contentTree(page);
  return { page, content: JSON.stringify(tree), text: plainText(tree) };
});

for (const name of imagesUsed) {
  if (!(await exists(join(imagesDir, name)))) problems.push(`image ${name} is not in wwwroot/assets/cms-help — run npm run capture`);
}
for (const file of await readdir(imagesDir).catch(() => [])) {
  if (!imagesUsed.has(file)) problems.push(`wwwroot/assets/cms-help/${file} is not shown on any page — use it or delete it`);
}
if (problems.length) {
  console.error('The guide was not built:\n  ' + problems.join('\n  '));
  process.exit(1);
}

// The issue date tells the application which pages are older than this guide. It moves forward
// only when the content has changed, so rebuilding an unchanged guide changes nothing.
const fingerprint = createHash('sha256').update(JSON.stringify(built.map((b) => [b.page.id, b.page.parentId, b.page.title, b.page.subtitle, b.page.order, b.page.keywords, b.content]))).digest('hex');
let issued = new Date().toISOString().replace(/\.\d+Z$/, 'Z');
try {
  const previous = JSON.parse(await readFile(bundlePath, 'utf8'));
  if (previous.exportedBy === `cms-guide ${fingerprint}`) issued = previous.exportedAtUtc;
} catch { /* no earlier bundle */ }

const bundle = {
  $schema: 'cpd-content-v2',
  schemaVersion: 2,
  exportedAtUtc: issued,
  exportedBy: `cms-guide ${fingerprint}`,
  pageNodes: built.map(({ page, content, text }) => ({
    id: page.id,
    parentId: page.parentId,
    segment: page.segment,
    title: page.title,
    ...(page.subtitle ? { subtitle: page.subtitle } : {}),
    pageType: 'content',
    sortOrder: page.order,
    appearInSearch: true,
    ...(page.keywords ? { keywords: page.keywords } : {}),
    versions: [{ versionId: 1, minorVersion: 0, publishFrom: issued, content, bodyPlainText: text }],
  })),
  contentBlocks: [],
};
await writeFile(bundlePath, JSON.stringify(bundle, null, 2) + '\n', 'utf8');
console.log(`wrote ${bundlePath}\n  ${bundle.pageNodes.length} pages, ${imagesUsed.size} screenshots, issued ${issued}`);
