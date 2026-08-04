// Debug Pipeline console client behaviour. Pure progressive enhancement: the drive / inject / seed
// buttons work as plain form posts without JS (the controller redirects back); with JS this script
// upgrades them to fetch() so the board is not interrupted by a full-page reload, mirrors the batch
// size onto each drive form, and enriches the automated-coverage panel from the served
// uat-coverage.json / uat-status.json. No server-side or client-side storage of results — this is a
// dev aid only.
(function () {
  'use strict';

  // --- Batch size mirror ----------------------------------------------------------------------

  function batchSize() {
    var batch = document.querySelector('[data-uat-batch]');
    if (!batch) return 1;
    var n = parseInt(batch.value, 10);
    if (isNaN(n) || n < 1) n = 1;
    if (n > 20) n = 20;
    return n;
  }

  function syncBatch() {
    var n = batchSize();
    document.querySelectorAll('[data-uat-batch-mirror]').forEach(function (input) {
      input.value = n;
    });
  }

  // --- AJAX drives ----------------------------------------------------------------------------
  // Upgrade the drive/inject/seed forms to fetch() so a drive updates the board in place rather
  // than reloading the page (the reload interrupts the live SSE view). The form's own action,
  // method and antiforgery token are reused; on success we update the "last reference" line and
  // nudge the board to re-read its feed. On any failure we fall back to a normal submit so the
  // no-JS server path still runs.

  function antiForgeryToken(form) {
    var input = form.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : null;
  }

  function updateLastReference(reference) {
    if (!reference) return;
    var el = document.querySelector('[data-uat-last-reference]');
    if (!el) return;
    el.innerHTML = '';
    el.appendChild(document.createTextNode('Last driven reference: '));
    var strong = document.createElement('strong');
    strong.textContent = reference;
    el.appendChild(strong);
  }

  // Ask the board to re-animate: the live SSE feed pushes fresh snapshots on its own, but a manual
  // refresh gives immediate feedback after a drive. The board exposes a global hook when present.
  function refreshBoard() {
    if (window.ObservabilityBoard && typeof window.ObservabilityBoard.refresh === 'function') {
      window.ObservabilityBoard.refresh();
    }
  }

  function submitDriveForm(form) {
    var token = antiForgeryToken(form);
    var body = new URLSearchParams();
    // Carry every named field the no-JS post would (count etc.).
    form.querySelectorAll('input[name]').forEach(function (input) {
      body.append(input.name, input.value);
    });
    var headers = { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' };
    // Program.cs configures Antiforgery.HeaderName = 'X-XSRF-TOKEN'; the framework's default
    // 'RequestVerificationToken' name silently 400s every POST that reaches the pipeline via
    // fetch. Match the configured header name so the AJAX path actually reaches the server
    // and the .catch() form-submit fallback isn't the load-bearing route.
    if (token) headers['X-XSRF-TOKEN'] = token;

    fetch(form.action, {
      method: (form.method || 'post').toUpperCase(),
      headers: headers,
      body: body.toString().length ? body : null,
      credentials: 'same-origin'
    })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) {
        if (data && data.ok) {
          updateLastReference(data.reference);
          refreshBoard();
        } else {
          form.submit(); // unexpected response — fall back to the full-page post
        }
      })
      .catch(function () { form.submit(); });
  }

  function wireDrives() {
    document.querySelectorAll('.uat-inline-form').forEach(function (form) {
      form.addEventListener('submit', function (e) {
        // Only intercept our action forms; let anything else post normally.
        if (!/\/dev\/uat\/(drive|inject-failure|seed-dlq)/.test(form.getAttribute('action') || '')) {
          return;
        }
        e.preventDefault();
        syncBatch();
        submitDriveForm(form);
      });
    });
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
    syncBatch();
    enrichCoverage();
    wireDrives();

    document.addEventListener('change', function (e) {
      if (e.target.matches('[data-uat-batch]')) syncBatch();
    });

    document.addEventListener('click', function (e) {
      if (e.target.matches('[data-coverage-copy]')) copyCommand(e.target);
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
