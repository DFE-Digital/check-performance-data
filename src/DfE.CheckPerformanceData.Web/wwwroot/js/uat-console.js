// UAT test console client behaviour. Pure progressive enhancement: the runner, its verdict radios
// and notes work as plain form controls without JS; this script persists them per-browser in
// localStorage (keyed by phase+item so a run survives refresh and redeploy), drives the progress
// meter and phase filter, clears and exports results, mirrors the batch size onto each drive form,
// and enriches the automated-coverage panel from the served uat-coverage.json / uat-status.json.
// No server-side storage of results — this is a dev aid only.
(function () {
  'use strict';

  var STORAGE_KEY = 'cpd-uat-results-v1';

  // --- Result store (localStorage) ------------------------------------------------------------

  function loadResults() {
    try {
      return JSON.parse(window.localStorage.getItem(STORAGE_KEY)) || {};
    } catch (e) {
      return {};
    }
  }

  function saveResults(results) {
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(results));
    } catch (e) {
      /* storage may be unavailable (private mode); the page still works, it just won't persist. */
    }
  }

  function itemKey(el) {
    var item = el.closest('[data-uat-item]');
    if (!item) return null;
    return item.getAttribute('data-uat-phase') + ':' + item.getAttribute('data-uat-item');
  }

  // --- Restore persisted verdicts + notes -----------------------------------------------------

  function restore(results) {
    document.querySelectorAll('[data-uat-item]').forEach(function (item) {
      var key = item.getAttribute('data-uat-phase') + ':' + item.getAttribute('data-uat-item');
      var saved = results[key];
      if (!saved) return;
      if (saved.verdict) {
        var radio = item.querySelector('input[type="radio"][value="' + saved.verdict + '"]');
        if (radio) radio.checked = true;
      }
      if (saved.notes) {
        var notes = item.querySelector('[data-uat-notes]');
        if (notes) notes.value = saved.notes;
      }
    });
  }

  function recordVerdict(results, el) {
    var key = itemKey(el);
    if (!key) return;
    results[key] = results[key] || {};
    results[key].verdict = el.value;
    saveResults(results);
  }

  function recordNotes(results, el) {
    var key = itemKey(el);
    if (!key) return;
    results[key] = results[key] || {};
    results[key].notes = el.value;
    saveResults(results);
  }

  // --- Progress meter -------------------------------------------------------------------------

  function updateProgress() {
    var total = document.querySelectorAll('[data-uat-item]').length;
    var done = 0;
    document.querySelectorAll('[data-uat-item]').forEach(function (item) {
      if (item.querySelector('input[type="radio"]:checked')) done += 1;
    });
    var text = document.querySelector('[data-uat-progress-text]');
    if (text) text.textContent = done + ' / ' + total;
  }

  // --- Phase filter ---------------------------------------------------------------------------

  function applyFilter(value) {
    document.querySelectorAll('[data-uat-item]').forEach(function (item) {
      var phase = item.getAttribute('data-uat-phase');
      item.hidden = !(value === 'all' || phase === value);
    });
  }

  // --- Batch size mirror ----------------------------------------------------------------------

  function syncBatch() {
    var batch = document.querySelector('[data-uat-batch]');
    if (!batch) return;
    var n = parseInt(batch.value, 10);
    if (isNaN(n) || n < 1) n = 1;
    if (n > 20) n = 20;
    document.querySelectorAll('[data-uat-batch-mirror]').forEach(function (input) {
      input.value = n;
    });
  }

  // --- Clear + export -------------------------------------------------------------------------

  function clearResults() {
    if (!window.confirm('Clear all UAT results stored in this browser?')) return;
    try {
      window.localStorage.removeItem(STORAGE_KEY);
    } catch (e) { /* ignore */ }
    document.querySelectorAll('[data-uat-item]').forEach(function (item) {
      item.querySelectorAll('input[type="radio"]:checked').forEach(function (r) { r.checked = false; });
      var notes = item.querySelector('[data-uat-notes]');
      if (notes) notes.value = '';
    });
    updateProgress();
  }

  function exportResults() {
    var out = { exportedAt: new Date().toISOString(), items: [] };
    document.querySelectorAll('[data-uat-item]').forEach(function (item) {
      var checked = item.querySelector('input[type="radio"]:checked');
      var notes = item.querySelector('[data-uat-notes]');
      var title = item.querySelector('h3');
      out.items.push({
        id: item.getAttribute('data-uat-item'),
        phase: item.getAttribute('data-uat-phase'),
        title: title ? title.textContent.trim() : '',
        verdict: checked ? checked.value : null,
        notes: notes ? notes.value : ''
      });
    });
    var blob = new Blob([JSON.stringify(out, null, 2)], { type: 'application/json' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = 'uat-results-' + new Date().toISOString().slice(0, 10) + '.json';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  // --- Automated coverage panel ---------------------------------------------------------------

  function coverageCommand(entry) {
    return 'dotnet test tests/' + entry.project + ' --filter "' + entry.filter + '"';
  }

  function enrichCoverage() {
    var table = document.querySelector('[data-uat-coverage]');
    if (!table) return;
    var coverageUrl = table.getAttribute('data-coverage-url');
    var statusUrl = table.getAttribute('data-status-url');

    fetch(coverageUrl).then(function (r) { return r.ok ? r.json() : null; }).then(function (manifest) {
      if (!manifest || !manifest.items) return;
      var byId = {};
      manifest.items.forEach(function (e) { byId[e.id] = e; });

      // First pass: render the mapping (filter + copyable command) from the manifest alone.
      document.querySelectorAll('[data-uat-coverage-id]').forEach(function (row) {
        var entry = byId[row.getAttribute('data-uat-coverage-id')];
        if (!entry) return;
        var filterCell = row.querySelector('[data-coverage-filter]');
        if (filterCell) filterCell.textContent = entry.description || entry.filter;
        var command = row.querySelector('[data-coverage-command]');
        if (command) command.textContent = coverageCommand(entry);
      });

      // Second pass: overlay live pass/fail/last-run from uat-status.json when present.
      return fetch(statusUrl).then(function (r) { return r.ok ? r.json() : null; }).then(function (status) {
        if (!status || !status.items) return;
        var statusById = {};
        status.items.forEach(function (s) { statusById[s.id] = s; });
        document.querySelectorAll('[data-uat-coverage-id]').forEach(function (row) {
          var s = statusById[row.getAttribute('data-uat-coverage-id')];
          var cell = row.querySelector('[data-coverage-status]');
          if (!cell) return;
          if (!s) { cell.textContent = 'Run to refresh'; return; }
          var mark = s.failed > 0 ? '✗' : '✓';
          cell.textContent = mark + ' ' + s.passed + '/' + s.total +
            (status.generatedAtUtc ? ' · last run ' + status.generatedAtUtc : '');
        });
      });
    }).catch(function () { /* coverage panel falls back to its server-rendered neutral state. */ });
  }

  function copyCommand(button) {
    var row = button.closest('[data-uat-coverage-id]');
    if (!row) return;
    var command = row.querySelector('[data-coverage-command]');
    if (!command || !navigator.clipboard) return;
    navigator.clipboard.writeText(command.textContent).then(function () {
      var original = button.textContent;
      button.textContent = 'Copied';
      window.setTimeout(function () { button.textContent = original; }, 1500);
    });
  }

  // --- Wire up --------------------------------------------------------------------------------

  function init() {
    var results = loadResults();
    restore(results);
    updateProgress();
    syncBatch();
    enrichCoverage();

    document.addEventListener('change', function (e) {
      if (e.target.matches('[data-uat-verdict] input[type="radio"]')) {
        recordVerdict(results, e.target);
        updateProgress();
      } else if (e.target.matches('[data-uat-filter] input[type="radio"]')) {
        applyFilter(e.target.value);
      } else if (e.target.matches('[data-uat-batch]')) {
        syncBatch();
      }
    });

    document.addEventListener('input', function (e) {
      if (e.target.matches('[data-uat-notes]')) recordNotes(results, e.target);
    });

    document.addEventListener('click', function (e) {
      if (e.target.matches('[data-uat-clear]')) clearResults();
      else if (e.target.matches('[data-uat-export]')) exportResults();
      else if (e.target.matches('[data-coverage-copy]')) copyCommand(e.target);
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
