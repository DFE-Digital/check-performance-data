// The example page the screenshots are taken of. capture.mjs imports it through Content staging
// before it starts, so every screenshot shows the same page whatever else is in the database.
//
// The page lives under /guidance and uses every widget once. It carries four versions, dated
// relative to today, so the Versions tab shows each status: Past, Live, Scheduled and Draft.
export const demoPageId = '5b1d6a54-0c4e-4d5b-9c0e-56200000d001';
export const demoPagePath = 'guidance/autumn-checking-exercise';
const guidanceRootId = '00000000-cd94-4a01-8f01-000000000004';

const widget = (type, props) => ({ kind: 'widget', type, props });
const region = (layout, ...columns) => ({ kind: 'region', layout, columns });
const daysFromNow = (days) => {
  const d = new Date(Date.now() + days * 86400000);
  d.setUTCHours(9, 0, 0, 0);
  return d.toISOString();
};

const intro =
  "<p class='govuk-body-l'>Use the autumn checking exercise to check your school's provisional key stage 4 data before it is published.</p>" +
  "<p class='govuk-body'>The exercise is open for 2 weeks. During that time you can ask for a pupil to be added or removed, and query a result that looks wrong.</p>";

const before =
  "<p class='govuk-body'>You need a DfE Sign-in account with access to this service. Ask your school's approver if you do not have one.</p>" +
  "<ul class='govuk-list govuk-list--bullet'><li>the pupil's unique pupil number (UPN)</li><li>the date they joined or left your school</li><li>evidence that supports the change, such as an attendance record</li></ul>" +
  "<div class='govuk-inset-text'>You can save a request and come back to it later. It is not sent until you select Submit.</div>";

const after =
  "<p class='govuk-body'>We check every request against the rules for the exercise. Most requests get a decision within 5 working days.</p>" +
  "<h3 class='govuk-heading-m'>If a request is rejected</h3><p class='govuk-body'>The decision explains why. You can send a new request with more evidence while the exercise is open.</p>";

const content = (lead) => [
  region('two-thirds-one-third',
    [
      widget('richtext', { html: lead }),
      widget('published', { text: 'Last reviewed 1 September. Next review due 1 March.' }),
      widget('search', {
        label: 'Search this page', placeholder: 'For example, evidence', action: '/search', buttonText: 'Search',
        scope: '', scopePageIds: '', searchIn: 'page', instant: 'true', showButton: 'false', noResultsText: 'Nothing on this page matches',
      }),
      widget('heading', { level: 2, text: 'Key dates' }),
      widget('summarylist', { rows: [
        { key: 'Exercise opens', value: 'Monday 14 September, 9am' },
        { key: 'Exercise closes', value: 'Friday 25 September, 5pm' },
        { key: 'Decisions sent by', value: 'Friday 9 October' },
      ] }),
      widget('heading', { level: 2, text: 'Before you start' }),
      widget('richtext', { html: before }),
      widget('divider', {}),
      widget('heading', { level: 2, text: 'After you submit a request' }),
      widget('richtext', { html: after }),
    ],
    [
      widget('pagenav', {
        mode: 'headings', childrenParentPath: '', showSearch: false, searchPath: '', searchLabel: 'Search',
        h1: 'false', h2: 'true', h3: 'true', h4: 'false', h5: 'false', h6: 'false',
      }),
    ]),
  region('halves',
    [widget('card', { title: 'Add a pupil', body: 'Ask for a pupil who is missing from your data to be included.', href: '/help/submit-amendment' })],
    [widget('card', { title: 'Get help', body: 'Contact the helpline if you cannot find what you need.', href: '/support/contact-helpline' })]),
];

// The words a visitor reads, for the search index: headings, text and the rows of the summary list.
const plainText = (tree) => {
  const parts = [];
  const walk = (node) => {
    if (Array.isArray(node)) return node.forEach(walk);
    if (node.kind === 'region') return walk(node.columns);
    const p = node.props;
    if (node.type === 'heading') parts.push(p.text);
    if (node.type === 'richtext') parts.push(p.html.replace(/<[^>]+>/g, ' '));
    if (node.type === 'card') parts.push(p.title, p.body);
    if (node.type === 'published') parts.push(p.text);
    if (node.type === 'summarylist') p.rows.forEach((r) => parts.push(r.key, r.value));
  };
  walk(tree);
  return parts.join(' ').replace(/\s+/g, ' ').trim();
};

const version = (versionId, lead, publishFrom, publishTo) => {
  const tree = content(lead);
  return {
    versionId, minorVersion: 0,
    ...(publishFrom ? { publishFrom } : {}),
    ...(publishTo ? { publishTo } : {}),
    content: JSON.stringify(tree),
    bodyPlainText: plainText(tree),
  };
};

export function demoBundle() {
  return {
    $schema: 'cpd-content-v2',
    schemaVersion: 2,
    exportedAtUtc: new Date().toISOString(),
    exportedBy: 'cms-guide-capture',
    pageNodes: [{
      id: demoPageId,
      parentId: guidanceRootId,
      segment: 'autumn-checking-exercise',
      title: 'Autumn checking exercise',
      subtitle: 'What to check, when to check it and how to ask for a change',
      pageType: 'content',
      sortOrder: 10,
      appearInSearch: true,
      keywords: 'ks4 amendments deadline',
      versions: [
        version(1, intro.replace('2 weeks', '10 days'), daysFromNow(-60), daysFromNow(-30)),
        version(2, intro, daysFromNow(-30)),
        version(3, intro.replace('2 weeks', '3 weeks'), daysFromNow(14)),
        version(4, intro.replace('before it is published', 'before it is published in the performance tables')),
      ],
    }],
    contentBlocks: [],
  };
}
