/*
 * Builds the HTML and console reports from the data that
 * k6 passes to handleSummary(). The reports hold aggregated
 * metrics only. Every value is HTML-escaped.
 */

export interface SummaryCheck {
    name: string;
    passes: number;
    fails: number;
}

export interface SummaryMetric {
    type: string;
    values: Record<string, number>;
    thresholds?: Record<string, { ok: boolean }>;
}

export interface SummaryData {
    metrics: Record<string, SummaryMetric>;
    root_group?: {
        checks?: SummaryCheck[];
    } | undefined;
}

export interface ReportContext {
    runId: string;
    generatedAt: string;
    settings: string[];
    summary: SummaryData;
}

export function escapeHtml(value: string): string {
    return value
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}

/*
 * Sub-metrics carry a tag, for example
 * http_req_duration{transaction:login}.
 */
function isSubMetric(name: string): boolean {
    return name.indexOf("{") >= 0;
}

function valueOf(
    summary: SummaryData,
    metricName: string,
    key: string
): number | undefined {
    const metric = summary.metrics[metricName];

    if (metric === undefined) {
        return undefined;
    }

    return metric.values[key];
}

function formatValue(
    metric: SummaryMetric,
    key: string,
    value: number
): string {
    if (metric.type === "rate" && key === "rate") {
        return `${(value * 100).toFixed(2)}%`;
    }

    if (key === "rate") {
        return `${value.toFixed(2)} per second`;
    }

    if (key === "count") {
        return String(value);
    }

    if (metric.type === "trend") {
        return `${value.toFixed(2)} ms`;
    }

    return Number.isInteger(value) ? String(value) : value.toFixed(2);
}

interface ThresholdRow {
    metric: string;
    expression: string;
    ok: boolean;
}

function thresholdRows(summary: SummaryData): ThresholdRow[] {
    const rows: ThresholdRow[] = [];

    for (const metric of Object.keys(summary.metrics).sort()) {
        const thresholds = summary.metrics[metric].thresholds;

        if (thresholds === undefined) {
            continue;
        }

        for (const expression of Object.keys(thresholds)) {
            rows.push({
                metric,
                expression,
                ok: thresholds[expression].ok,
            });
        }
    }

    return rows;
}

function checksOf(summary: SummaryData): SummaryCheck[] {
    return summary.root_group?.checks ?? [];
}

/**
 * True when every threshold passed and no check failed.
 */
export function runPassed(summary: SummaryData): boolean {
    const thresholdsPassed = thresholdRows(summary).every(
        (row) => row.ok
    );

    const checksPassed = checksOf(summary).every(
        (check) => check.fails === 0
    );

    return thresholdsPassed && checksPassed;
}

function checkTotals(summary: SummaryData): { passes: number; total: number } {
    let passes = 0;
    let total = 0;

    for (const check of checksOf(summary)) {
        passes += check.passes;
        total += check.passes + check.fails;
    }

    return { passes, total };
}

function percentOrDash(value: number | undefined): string {
    return value === undefined ? "not recorded" : `${(value * 100).toFixed(2)}%`;
}

function countOrDash(value: number | undefined): string {
    return value === undefined ? "not recorded" : String(value);
}

function msOrDash(value: number | undefined): string {
    return value === undefined ? "not recorded" : `${value.toFixed(2)} ms`;
}

/*
 * Shows a duration in the unit that reads best. Values in
 * milliseconds become minutes and seconds from one minute up.
 */
function durationText(value: number | undefined): string {
    if (value === undefined) {
        return "not recorded";
    }

    if (value < 1000) {
        return `${value.toFixed(0)} ms`;
    }

    const totalSeconds = Math.round(value / 1000);

    if (totalSeconds < 60) {
        return `${(value / 1000).toFixed(1)} s`;
    }

    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;

    return `${minutes} min ${seconds} s`;
}

interface BusinessDuration {
    label: string;
    metric: string;
}

const BUSINESS_DURATIONS: BusinessDuration[] = [
    { label: "Journey duration", metric: "journey_duration" },
    { label: "Pupil search duration", metric: "pupil_search_duration" },
    { label: "Evidence upload duration", metric: "evidence_upload_duration" },
    { label: "Submission duration", metric: "submission_duration" },
];

/*
 * Journeys completed, and journeys attempted. Attempted is the
 * number of journey iterations that ended in a pass or a fail.
 */
function journeyTotals(summary: SummaryData): {
    completed: number | undefined;
    attempted: number | undefined;
} {
    const passes = valueOf(summary, "journey_completion", "passes");
    const fails = valueOf(summary, "journey_completion", "fails");

    return {
        completed: valueOf(summary, "journeys_completed", "count"),
        attempted:
            passes === undefined || fails === undefined
                ? undefined
                : passes + fails,
    };
}

function journeysCardText(summary: SummaryData): string {
    const { completed, attempted } = journeyTotals(summary);

    if (attempted === undefined) {
        return countOrDash(completed);
    }

    return `${completed ?? 0} of ${attempted}`;
}

function durationRowsHtml(summary: SummaryData): string {
    return BUSINESS_DURATIONS
        .map((row) => {
            const samples = valueOf(summary, row.metric, "count");
            const median = valueOf(summary, row.metric, "med");
            const p95 = valueOf(summary, row.metric, "p(95)");

            return `<tr><th scope="row">${escapeHtml(row.label)}</th>` +
                `<td>${countOrDash(samples)}</td>` +
                `<td>${durationText(median)}</td>` +
                `<td>${durationText(p95)}</td></tr>`;
        })
        .join("\n");
}

function metricRowsHtml(summary: SummaryData): string {
    return Object.keys(summary.metrics)
        .filter((name) => !isSubMetric(name))
        .sort()
        .map((name) => {
            const metric = summary.metrics[name];
            const values = Object.keys(metric.values)
                .map((key) => {
                    const text = formatValue(metric, key, metric.values[key]);
                    return `${escapeHtml(key)}: ${escapeHtml(text)}`;
                })
                .join("<br>");

            return `<tr><th scope="row">${escapeHtml(name)}</th>` +
                `<td>${escapeHtml(metric.type)}</td>` +
                `<td>${values}</td></tr>`;
        })
        .join("\n");
}

function thresholdRowsHtml(summary: SummaryData): string {
    const rows = thresholdRows(summary);

    if (rows.length === 0) {
        return "<tr><td colspan=\"3\">No thresholds were set.</td></tr>";
    }

    return rows
        .map((row) => {
            const result = row.ok ? "Pass" : "Fail";
            const cls = row.ok ? "pass" : "fail";

            return `<tr><td>${escapeHtml(row.metric)}</td>` +
                `<td>${escapeHtml(row.expression)}</td>` +
                `<td class="${cls}">${result}</td></tr>`;
        })
        .join("\n");
}

function checkRowsHtml(summary: SummaryData): string {
    const checks = checksOf(summary);

    if (checks.length === 0) {
        return "<tr><td colspan=\"3\">No checks were recorded.</td></tr>";
    }

    return checks
        .map((check) => {
            const cls = check.fails === 0 ? "pass" : "fail";

            return `<tr><td>${escapeHtml(check.name)}</td>` +
                `<td>${check.passes}</td>` +
                `<td class="${cls}">${check.fails}</td></tr>`;
        })
        .join("\n");
}

const STYLE = `
body { font-family: Arial, Helvetica, sans-serif; margin: 0; color: #0b0c0c; background: #fff; }
main { max-width: 1100px; margin: 0 auto; padding: 24px; }
h1 { font-size: 28px; margin-bottom: 8px; }
h2 { font-size: 20px; margin-top: 32px; border-bottom: 2px solid #1d70b8; padding-bottom: 4px; }
table { border-collapse: collapse; width: 100%; margin-top: 12px; font-size: 14px; }
th, td { border: 1px solid #b1b4b6; padding: 6px 8px; text-align: left; vertical-align: top; }
thead th { background: #f3f2f1; }
.cards { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 16px; }
.card { border: 1px solid #b1b4b6; padding: 12px; min-width: 200px; flex: 1; }
.card dt { font-size: 14px; color: #505a5f; }
.card dd { margin: 4px 0 0 0; font-size: 22px; font-weight: bold; }
.result-passed { color: #00703c; }
.result-failed { color: #d4351c; }
.pass { color: #00703c; font-weight: bold; }
.fail { color: #d4351c; font-weight: bold; }
.note { color: #505a5f; font-size: 14px; }
`;

/**
 * Returns a complete HTML document. It has inline CSS only
 * and no script or remote asset.
 */
export function buildHtmlReport(context: ReportContext): string {
    const summary = context.summary;
    const passed = runPassed(summary);
    const totals = checkTotals(summary);

    const resultText = passed ? "PASSED" : "FAILED";
    const resultClass = passed ? "result-passed" : "result-failed";

    const settingsHtml = context.settings
        .map((line) => `<li>${escapeHtml(line)}</li>`)
        .join("\n");

    return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>k6 report ${escapeHtml(context.runId)}</title>
<style>${STYLE}</style>
</head>
<body>
<main>
<h1>k6 performance test report</h1>
<p class="note">Run ID ${escapeHtml(context.runId)}. Generated ${escapeHtml(context.generatedAt)}.</p>

<h2>Result</h2>
<p class="${resultClass}"><strong>${resultText}</strong></p>
<dl class="cards">
<div class="card"><dt>Checks passed</dt><dd>${totals.passes} of ${totals.total}</dd></div>
</dl>

<h2>Business metrics</h2>
<dl class="cards">
<div class="card"><dt>Journey success rate</dt><dd>${percentOrDash(valueOf(summary, "journey_completion", "rate"))}</dd></div>
<div class="card"><dt>Journeys completed</dt><dd>${journeysCardText(summary)}</dd></div>
<div class="card"><dt>Submissions</dt><dd>${countOrDash(valueOf(summary, "submissions", "count"))}</dd></div>
</dl>
<table>
<thead><tr><th scope="col">Measure</th><th scope="col">Samples</th><th scope="col">Median</th><th scope="col">95th percentile</th></tr></thead>
<tbody>
${durationRowsHtml(summary)}
</tbody>
</table>
<p class="note">Durations use successful steps only. Journey duration covers the journey from its start to submission. It excludes authentication and cleanup.</p>

<h2>Run settings</h2>
<ul>
${settingsHtml}
</ul>

<h2>Thresholds</h2>
<table>
<thead><tr><th scope="col">Metric</th><th scope="col">Threshold</th><th scope="col">Result</th></tr></thead>
<tbody>
${thresholdRowsHtml(summary)}
</tbody>
</table>

<h2>Checks</h2>
<table>
<thead><tr><th scope="col">Check</th><th scope="col">Passes</th><th scope="col">Fails</th></tr></thead>
<tbody>
${checkRowsHtml(summary)}
</tbody>
</table>

<h2>Technical metrics</h2>
<dl class="cards">
<div class="card"><dt>HTTP requests</dt><dd>${countOrDash(valueOf(summary, "http_reqs", "count"))}</dd></div>
<div class="card"><dt>HTTP failed rate</dt><dd>${percentOrDash(valueOf(summary, "http_req_failed", "rate"))}</dd></div>
<div class="card"><dt>HTTP p(95) duration</dt><dd>${msOrDash(valueOf(summary, "http_req_duration", "p(95)"))}</dd></div>
</dl>
<table>
<thead><tr><th scope="col">Metric</th><th scope="col">Type</th><th scope="col">Values</th></tr></thead>
<tbody>
${metricRowsHtml(summary)}
</tbody>
</table>
<p class="note">Per-journey and per-transaction thresholds appear in the Thresholds table.</p>
</main>
</body>
</html>
`;
}

/**
 * Returns the short text that k6 prints to the console.
 */
export function buildConsoleSummary(
    context: ReportContext,
    htmlPath: string,
    jsonPath: string
): string {
    const summary = context.summary;
    const totals = checkTotals(summary);
    const failed = thresholdRows(summary).filter((row) => !row.ok);

    const lines = [
        "",
        `Run ID: ${context.runId}`,
        `Result: ${runPassed(summary) ? "PASSED" : "FAILED"}`,
        `Journeys completed: ${countOrDash(valueOf(summary, "journeys_completed", "count"))}`,
        `Checks passed: ${totals.passes} of ${totals.total}`,
    ];

    for (const row of failed) {
        lines.push(`Failed threshold: ${row.metric} ${row.expression}`);
    }

    lines.push(`HTML report: ${htmlPath}`);
    lines.push(`JSON summary: ${jsonPath}`);
    lines.push("");

    return lines.join("\n");
}
