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

  // The outcome a drive form intends, parsed from its action so the board can show one correctly
  // routed envelope immediately. Drive carries ?outcome=approved|rejected|scrutiny; inject/seed map
  // to a failed (dead-letter) envelope.
  function outcomeForForm(form) {
    var action = form.getAttribute('action') || '';
    var m = action.match(/outcome=([a-z]+)/i);
    if (m) { return m[1].toLowerCase(); }
    if (/inject-failure|seed-dlq/.test(action)) { return 'failed'; }
    return null;
  }

  // Give the board immediate, correctly-routed feedback for the just-driven message: one envelope,
  // animated through its status, deduped against the SSE rows that follow. Falls back silently when
  // the board hook is absent (no-JS / board not present).
  // outcomeOverride wins when the server resolved the outcome itself (the Random drive rolls one of
  // four server-side and returns it), so the optimistic envelope routes to the actual destination
  // rather than the literal "random" parsed from the action.
  function presentOnBoard(form, reference, outcomeOverride) {
    if (window.ObservabilityBoard && typeof window.ObservabilityBoard.present === 'function') {
      window.ObservabilityBoard.present(reference, outcomeOverride || outcomeForForm(form));
    }
  }

  function submitDriveForm(form, countOverride) {
    var token = antiForgeryToken(form);
    var body = new URLSearchParams();
    // Carry every named field the no-JS post would (count etc.). When a countOverride is given
    // (the staggered batch path drives one at a time), it replaces the form's count.
    form.querySelectorAll('input[name]').forEach(function (input) {
      if (countOverride != null && input.name === 'count') {
        body.append('count', String(countOverride));
      } else {
        body.append(input.name, input.value);
      }
    });
    var headers = { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' };
    if (token) headers['RequestVerificationToken'] = token;

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
          // Only the drive buttons present optimistically here; inject-failure already drives the
          // board from its own [data-obs-inject] click handler, and seed-dlq surfaces via the SSE
          // feed — presenting those here too would double the envelope.
          if (/\/dev\/uat\/drive/.test(form.getAttribute('action') || '')) {
            presentOnBoard(form, data.reference, data.outcome);
          }
        } else {
          form.submit(); // unexpected response — fall back to the full-page post
        }
      })
      .catch(function () { form.submit(); });
  }

  // Drive a batch of N one at a time with small randomised gaps, so a batch appears as a CLUSTER
  // entering the board and flowing through the stages — rather than the server looping N at once and
  // the board showing a single envelope. The first fires immediately; each subsequent one after a
  // randomised 150–450ms gap. Each single drive presents its own envelope and updates the last
  // reference.
  function staggerDrives(form, n) {
    var delay = 0;
    for (var i = 0; i < n; i++) {
      window.setTimeout(function () { submitDriveForm(form, 1); }, delay);
      delay += 150 + Math.floor(Math.random() * 300);
    }
  }

  function wireDrives() {
    document.querySelectorAll('.uat-inline-form').forEach(function (form) {
      form.addEventListener('submit', function (e) {
        var action = form.getAttribute('action') || '';
        // Only intercept our action forms; let anything else post normally.
        if (!/\/dev\/uat\/(drive|inject-failure|seed-dlq)/.test(action)) {
          return;
        }
        e.preventDefault();
        syncBatch();
        // A batch of more than one drive is staggered into a visible cluster; everything else
        // (a single drive, inject, seed) posts once.
        var n = /\/dev\/uat\/drive/.test(action) ? batchSize() : 1;
        if (n > 1) {
          staggerDrives(form, n);
        } else {
          submitDriveForm(form);
        }
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

  // --- Demo-tools toggle ----------------------------------------------------------------------
  // The Demo panel stays above the board, but its toggle is a subtle link rendered below the
  // Recent submissions table (decoupled via aria-controls). The panel renders visible with no JS;
  // here we collapse it on load and wire the trigger so the always-on dashboard does not lead with
  // the dev controls. Expanding scrolls the panel (which is above) into view and moves focus to it.

  function wireDemoToggle() {
    var toggle = document.querySelector('[data-obs-demo-toggle]');
    var panel = document.querySelector('[data-obs-demo-panel]');
    if (!toggle || !panel) { return; }

    // Make the panel focusable so focus can move to it when revealed from the link below it.
    if (!panel.hasAttribute('tabindex')) { panel.setAttribute('tabindex', '-1'); }

    function setExpanded(expanded) {
      panel.hidden = !expanded;
      toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
    }

    // Collapse on first load (progressive enhancement: without JS the panel just shows).
    setExpanded(false);

    toggle.addEventListener('click', function () {
      var expanded = toggle.getAttribute('aria-expanded') === 'true';
      setExpanded(!expanded);
      if (!expanded) {
        panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
        panel.focus({ preventScroll: true });
      }
    });
  }

  // --- Confirm before destructive demo actions ------------------------------------------------
  // A form carrying data-uat-confirm asks for confirmation before its (full-page) post — used by
  // the "Purge demo traffic" button, which deletes rows. With no JS the post just proceeds.
  function wireConfirms() {
    document.querySelectorAll('form[data-uat-confirm]').forEach(function (form) {
      form.addEventListener('submit', function (e) {
        var message = form.getAttribute('data-uat-confirm');
        if (message && !window.confirm(message)) {
          e.preventDefault();
        }
      });
    });
  }

  // --- Wire up --------------------------------------------------------------------------------

  function init() {
    syncBatch();
    enrichCoverage();
    wireDrives();
    wireDemoToggle();
    wireConfirms();

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
