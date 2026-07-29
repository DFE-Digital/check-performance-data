(function () {
    'use strict';

    // Sample-search-data seed page — progressive-enhancement layer.
    //
    // The server-side action is non-blocking: the POST kicks the seeder onto a background
    // task and 302-redirects back to the same page with a jobId query-string parameter.
    // With JS disabled, the user sees the page reload and can refresh to check state — no
    // modal, but the seed still runs and completes.
    //
    // With JS enabled we intercept the submit, POST via fetch, open a modal, and poll a
    // /progress endpoint every 500 ms so the user sees rows-written + current-cursor
    // updates in real time. Cancel button posts DELETE to a per-job endpoint; the seeder
    // respects the token between batches.
    //
    // No frameworks. Idempotent to double-submits (guard against multiple concurrent polls).

    var POLL_MS = 500;
    var LOG_MAX = 5;
    // Absolute floor for a rendered ETA value. Below this we display "About …"
    // instead of a duration — a "0 seconds remaining" line while the seeder is
    // still working looks like a bug.
    var ETA_FLOOR_SECONDS = 1;

    function el(role, root) {
        return (root || document).querySelector('[data-role="' + role + '"]');
    }

    // Read the anti-forgery token — first from the form's hidden field (Html.AntiForgeryToken
    // renders <input name="__RequestVerificationToken" ...>), fall back to a <meta> tag if
    // present. Returns null when neither exists; the caller can then decide to fall back to
    // a full form submit rather than trying an unauthenticated fetch.
    //
    // The token is sent on fetch/XHR requests via the 'X-XSRF-TOKEN' header (Program.cs
    // sets AntiforgeryOptions.HeaderName to that value). Using the default framework
    // 'RequestVerificationToken' name causes the ValidateAntiForgeryToken filter to reject
    // silently — the response ends up as a re-rendered 404 page via the app's
    // StatusCodePagesWithReExecute middleware.
    function readAntiforgery(form) {
        var input = form.querySelector('input[name="__RequestVerificationToken"]');
        if (input && input.value) return input.value;
        var meta = document.querySelector('meta[name="request-verification-token"]');
        return meta ? meta.getAttribute('content') : null;
    }

    function formatDate(iso) {
        if (!iso) return '—';
        try {
            var d = new Date(iso);
            if (isNaN(d.getTime())) return '—';
            // Compact UTC form: 15 May 2026 12:00
            var months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
            var pad = function (n) { return n < 10 ? '0' + n : '' + n; };
            return d.getUTCDate() + ' ' + months[d.getUTCMonth()] + ' ' + d.getUTCFullYear()
                 + ' ' + pad(d.getUTCHours()) + ':' + pad(d.getUTCMinutes());
        } catch (e) { return '—'; }
    }

    function formatTime(iso) {
        if (!iso) return '';
        try {
            var d = new Date(iso);
            var pad = function (n) { return n < 10 ? '0' + n : '' + n; };
            return pad(d.getUTCHours()) + ':' + pad(d.getUTCMinutes()) + ':' + pad(d.getUTCSeconds());
        } catch (e) { return ''; }
    }

    function State() {
        this.form = null;
        this.modal = null;
        this.jobId = null;
        this.pollTimer = null;
        this.polling = false;
        this.startedAtMs = 0;
        this.logEntries = [];
        this.lastEventsWritten = -1;
        // Persisted per-event seconds rate embedded on the form as
        // data-seconds-per-event. Used as the ETA baseline; blended with cumulative
        // rate once enough samples are in (see computeEtaSeconds).
        this.secondsPerEvent = 0.1;
    }

    // Pure helper: given elapsed time and events written, return the estimated seconds
    // remaining. Uses the cumulative rate (written / elapsed) blended with the persisted
    // per-event rate — cumulative is stable across the whole run (a batch job's rate is
    // roughly constant, and where it drifts it drifts monotonically as JIT + connection
    // pools warm up), so a rolling window's reactivity turned out to be a footgun: it
    // catches transient speedups near the end and drops the ETA faster than reality can
    // finish, then the seeder overshoots and completes while the readout still shows
    // several seconds. Cumulative avoids that by construction.
    //
    // The persisted rate is used as the baseline when the run is too young for cumulative
    // to be meaningful (< 3 s or < 100 events written). Once cumulative kicks in, we take
    // the FASTER of cumulative vs persisted so a fast-machine run converges downward from
    // the conservative persisted baseline rather than lingering above it.
    function computeEtaSeconds(eventsWritten, eventsTotal, startedAtMs, nowMs,
                               persistedSecondsPerEvent) {
        if (!eventsTotal || eventsTotal <= 0) return null;
        var written = eventsWritten || 0;
        if (written >= eventsTotal) return 0;
        var remaining = eventsTotal - written;

        var elapsedS = Math.max(0, (nowMs - startedAtMs) / 1000);
        var cumulativeRate = (elapsedS > 0 && written > 0) ? (written / elapsedS) : 0;
        var persistedRate = (persistedSecondsPerEvent > 0)
            ? (1 / persistedSecondsPerEvent)
            : 0;

        var rate;
        if (elapsedS >= 3 && written >= 100 && cumulativeRate > 0) {
            // Take the faster of cumulative vs persisted — a fast machine's cumulative
            // will exceed the persisted (conservative) rate and give a shorter, more
            // honest ETA. On a slow machine, cumulative is lower and dominates.
            rate = Math.max(cumulativeRate, persistedRate);
        } else if (persistedRate > 0) {
            rate = persistedRate;
        } else {
            rate = cumulativeRate;
        }
        if (rate <= 0) return null;

        return Math.max(0, Math.round(remaining / rate));
    }

    // Human-readable duration. `47` → `47 seconds`. `93` → `1 minute 33 seconds`.
    // `4335` → `1 hour 12 minutes 15 seconds`. Skips zero components once a bigger unit
    // is in play (so `3600` → `1 hour`, `3660` → `1 hour 1 minute`) — the reader is
    // scanning for magnitude first, precision second.
    function formatDuration(totalSeconds) {
        var s = Math.max(0, Math.round(totalSeconds || 0));
        var hours = Math.floor(s / 3600);
        var minutes = Math.floor((s % 3600) / 60);
        var seconds = s % 60;
        var parts = [];
        if (hours > 0)   parts.push(hours   + (hours   === 1 ? ' hour'   : ' hours'));
        if (minutes > 0) parts.push(minutes + (minutes === 1 ? ' minute' : ' minutes'));
        // Include seconds only when it's non-zero, OR when nothing else is (so `0` renders
        // as `0 seconds` rather than an empty string). Also always include for < 1 min so
        // "47 seconds" reads naturally.
        if (seconds > 0 || parts.length === 0) {
            parts.push(seconds + (seconds === 1 ? ' second' : ' seconds'));
        }
        return parts.join(' ');
    }

    // Expose for ad-hoc manual verification from DevTools; harmless in production.
    if (typeof window !== 'undefined') {
        window.__seedComputeEtaSeconds = computeEtaSeconds;
        window.__seedFormatDuration = formatDuration;
    }

    State.prototype.init = function (form, modal) {
        this.form = form;
        this.modal = modal;

        // Read the persisted per-event rate written by the server. Parsed in the
        // browser's default locale (Number(…)) but the server rendered in
        // InvariantCulture so decimal point is always ".". A malformed value
        // falls back to 0.1 — the same default the server uses.
        var raw = form.getAttribute('data-seconds-per-event');
        var parsed = raw ? Number(raw) : NaN;
        if (isFinite(parsed) && parsed > 0) {
            this.secondsPerEvent = parsed;
        }

        this.wireCancel();
        this.wireClose();
        this.wireRetry();

        // Auto-resume: if the URL carries ?jobId=… OR the form has data-resume-job-id
        // (server-set on GET with jobId), open the modal and start polling that job.
        var resumeId = form.getAttribute('data-resume-job-id');
        var url = new URL(window.location.href);
        var queryId = url.searchParams.get('jobId');
        if (queryId) resumeId = queryId;
        if (resumeId && resumeId.length > 0) {
            this.jobId = resumeId;
            this.setSubmittingUi(true);
            this.openModal();
            this.startPolling();
        }

        this.wireSubmit();
    };

    State.prototype.wireSubmit = function () {
        var self = this;
        this.form.addEventListener('submit', function (e) {
            e.preventDefault();
            if (self.polling) return; // already in flight — ignore duplicate submit
            self.submitAsync();
        });
    };

    State.prototype.setSubmittingUi = function (busy) {
        var btn = this.form.querySelector('#seed-sample-search-data-submit');
        if (!btn) return;
        var busyLabel = btn.getAttribute('data-busy-label') || 'Seeding…';
        var idleLabel = btn.getAttribute('data-idle-label') || 'Seed data';
        btn.disabled = busy;
        btn.textContent = busy ? busyLabel : idleLabel;
    };

    State.prototype.submitAsync = function () {
        var self = this;
        var token = readAntiforgery(this.form);
        if (!token) {
            // No token — fall back to the native form submit so the redirect path still
            // runs. The page will reload with jobId=… and pick up on the next paint.
            this.form.submit();
            return;
        }

        this.setSubmittingUi(true);
        this.resetModalContent();
        this.openModal();

        var fd = new FormData(this.form);
        fetch(this.form.action, {
            method: 'POST',
            body: fd,
            credentials: 'same-origin',
            headers: { 'X-XSRF-TOKEN': token },
            redirect: 'follow',
        }).then(function (resp) {
            var location = resp.url || resp.headers.get('Location');
            if (location) {
                try {
                    var u = new URL(location, window.location.origin);
                    var id = u.searchParams.get('jobId');
                    if (id) self.jobId = id;
                } catch (e) { /* ignore */ }
            }
            if (!self.jobId) {
                // Response didn't include a jobId — maybe an error page. Fall back to
                // native submit so the user at least sees the server response.
                self.setSubmittingUi(false);
                self.closeModal();
                self.form.submit();
                return;
            }
            self.startPolling();
        }).catch(function (err) {
            self.setSubmittingUi(false);
            self.showFailure('Could not start the seed: ' + (err && err.message ? err.message : 'network error'));
        });
    };

    State.prototype.openModal = function () {
        if (this.modal && typeof this.modal.showModal === 'function' && !this.modal.open) {
            this.modal.showModal();
        }
        this.startedAtMs = Date.now();
    };

    State.prototype.closeModal = function () {
        if (this.modal && this.modal.open) this.modal.close();
    };

    State.prototype.resetModalContent = function () {
        var setText = function (role, txt) {
            var e = el(role, this.modal);
            if (e) e.textContent = txt;
        }.bind(this);
        setText('events-written', '0');
        setText('events-total', '0');
        setText('current-cursor', '—');
        setText('preset-label', '…');
        setText('progress-percent', '0');
        var fill = el('progress-fill', this.modal);
        if (fill) fill.style.width = '0%';
        var bar = el('progress-bar', this.modal);
        if (bar) bar.setAttribute('aria-valuenow', '0');
        // ETA line now visible from the start — populated with either an initial
        // estimate from the persisted seconds-per-event rate (once we know the
        // preset's event total) or with "…" as a loading placeholder. Never
        // rendered as a raw "0 seconds" line.
        setText('eta-seconds', '…');
        var eta = el('eta-line', this.modal);
        if (eta) eta.hidden = false;
        var cursor = el('cursor-line', this.modal);
        if (cursor) cursor.hidden = true;
        var log = el('progress-log', this.modal);
        if (log) log.innerHTML = '<li class="seed-progress-log__empty">Waiting for the first tick…</li>';
        this.logEntries = [];
        this.lastEventsWritten = -1;

        var completed = el('result-completed', this.modal); if (completed) completed.hidden = true;
        var failed = el('result-failed', this.modal);       if (failed) failed.hidden = true;
        var cancelled = el('result-cancelled', this.modal); if (cancelled) cancelled.hidden = true;

        var cancelBtn = el('cancel-button', this.modal);
        if (cancelBtn) {
            cancelBtn.disabled = false;
            cancelBtn.hidden = false;
            cancelBtn.style.display = '';
        }
        // Reset the Close-button copy to the running-state variant. On terminal
        // transitions we swap in the terminal label ("Close").
        var closeBtn = el('close-button', this.modal);
        if (closeBtn) {
            var runningLabel = closeBtn.getAttribute('data-running-label');
            if (runningLabel) closeBtn.textContent = runningLabel;
        }
    };

    // Called on every terminal transition (Completed / Failed / Cancelled).
    // Removes the Cancel button (no longer meaningful) and relabels the Close
    // button so "Close (keep seeding in background)" isn't misleading — the
    // seed is either done or over.
    //
    // Setting both the `hidden` attribute AND `display: none` inline — the
    // .govuk-button-group flex container leaves child buttons unaffected by
    // display rules by default, but belt-and-braces here so any theme override
    // in the future can't re-surface the button on a terminal state.
    State.prototype.enterTerminalState = function () {
        var cancelBtn = el('cancel-button', this.modal);
        if (cancelBtn) {
            cancelBtn.hidden = true;
            cancelBtn.style.display = 'none';
        }
        var closeBtn = el('close-button', this.modal);
        if (closeBtn) {
            var terminalLabel = closeBtn.getAttribute('data-terminal-label') || 'Close';
            closeBtn.textContent = terminalLabel;
        }
    };

    State.prototype.startPolling = function () {
        if (this.polling || !this.jobId) return;
        this.polling = true;
        this.pollOnce();
    };

    State.prototype.stopPolling = function () {
        this.polling = false;
        if (this.pollTimer) {
            clearTimeout(this.pollTimer);
            this.pollTimer = null;
        }
    };

    State.prototype.pollOnce = function () {
        var self = this;
        if (!this.polling || !this.jobId) return;
        var url = this.form.getAttribute('data-progress-url') + '?jobId=' + encodeURIComponent(this.jobId);
        fetch(url, { credentials: 'same-origin' })
            .then(function (r) {
                if (r.status === 404) {
                    self.stopPolling();
                    self.showFailure('The seed job could not be found (it may have expired). Please refresh.');
                    return null;
                }
                return r.ok ? r.json() : null;
            })
            .then(function (payload) {
                if (!payload) return;
                self.applyPayload(payload);
                if (payload.state === 'Completed' || payload.state === 'Failed') {
                    self.stopPolling();
                } else if (self.polling) {
                    self.pollTimer = setTimeout(function () { self.pollOnce(); }, POLL_MS);
                }
            })
            .catch(function () {
                // Network hiccup: back off but keep polling.
                if (self.polling) {
                    self.pollTimer = setTimeout(function () { self.pollOnce(); }, POLL_MS * 4);
                }
            });
    };

    State.prototype.applyPayload = function (p) {
        var setText = function (role, txt) {
            var e = el(role, this.modal);
            if (e) e.textContent = txt;
        }.bind(this);

        setText('preset-label', p.presetLabel || '…');
        setText('events-written', (p.eventsWritten || 0).toLocaleString());
        setText('events-total', (p.eventsTotal || 0).toLocaleString());

        var percent = 0;
        if (p.eventsTotal > 0) {
            percent = Math.min(100, Math.round(100 * (p.eventsWritten || 0) / p.eventsTotal));
        }
        var fill = el('progress-fill', this.modal);
        if (fill) fill.style.width = percent + '%';
        var bar = el('progress-bar', this.modal);
        if (bar) bar.setAttribute('aria-valuenow', String(percent));
        setText('progress-percent', String(percent));

        if (p.currentCursorUtc) {
            setText('current-cursor', formatDate(p.currentCursorUtc));
            var cursorLine = el('cursor-line', this.modal);
            if (cursorLine) cursorLine.hidden = false;
        }

        // ETA. Cumulative rate blended with the persisted per-event baseline (see
        // computeEtaSeconds for the reasoning). Rendered as a human-readable duration
        // — `47 seconds`, `1 minute 33 seconds`, `1 hour 12 minutes 15 seconds` — so a
        // long-running quarter/year seed doesn't force the reader to convert a raw
        // 4-digit second count in their head. Never render "0 seconds" while the
        // seeder is still going: below the floor we show the loading marker.
        var nowMs = Date.now();
        var etaLine = el('eta-line', this.modal);
        if (etaLine && (p.eventsTotal || 0) > 0) {
            var etaSec = computeEtaSeconds(
                p.eventsWritten || 0,
                p.eventsTotal || 0,
                this.startedAtMs,
                nowMs,
                this.secondsPerEvent);

            if (etaSec !== null && etaSec >= ETA_FLOOR_SECONDS) {
                setText('eta-seconds', formatDuration(etaSec));
                etaLine.hidden = false;
            } else if (etaSec !== null && etaSec < ETA_FLOOR_SECONDS
                       && p.state !== 'Completed' && p.state !== 'Failed') {
                setText('eta-seconds', '…');
                etaLine.hidden = false;
            }
        }

        if (p.eventsWritten !== this.lastEventsWritten) {
            this.lastEventsWritten = p.eventsWritten;
            var line = formatTime(new Date().toISOString())
                + ' UTC — seeded ' + (p.eventsWritten || 0).toLocaleString() + ' events'
                + (p.currentCursorUtc ? ' (up to ' + formatDate(p.currentCursorUtc) + ')' : '');
            this.pushLog(line);
        }

        if (p.state === 'Cancelling') {
            // The Cancel click has landed and the seeder is unwinding; the
            // controller is about to invoke rollback. Disable Cancel and swap
            // its label so a repeat click doesn't fire another rollback.
            var cancelBtnInProg = el('cancel-button', this.modal);
            if (cancelBtnInProg) {
                cancelBtnInProg.disabled = true;
                cancelBtnInProg.textContent = 'Cancelling…';
            }
        }

        if (p.state === 'Completed') {
            var isCancelled = p.note && p.note.toLowerCase().indexOf('cancel') !== -1;
            if (isCancelled) {
                var cancelBlock = el('result-cancelled', this.modal);
                var cancelNote = el('cancelled-note', this.modal);
                if (cancelNote) {
                    // Prefer the server-supplied note when it carries the "rolled back
                    // N rows" phrasing from the DELETE endpoint; otherwise fall back
                    // to a formatted "N of M events" line so the user sees how far
                    // along the seed got before the cancel landed.
                    var written = (p.eventsWritten || 0).toLocaleString();
                    var total = (p.eventsTotal || 0).toLocaleString();
                    var noteLower = (p.note || '').toLowerCase();
                    if (p.note && noteLower.indexOf('rolled back') !== -1) {
                        cancelNote.textContent = p.note;
                    } else if (p.eventsTotal && p.eventsTotal > 0) {
                        cancelNote.textContent = 'Seeding cancelled at ' + written +
                            ' of ' + total + ' events.';
                    } else if (p.note) {
                        cancelNote.textContent = p.note;
                    } else {
                        cancelNote.textContent = 'Seeding cancelled.';
                    }
                }
                if (cancelBlock) cancelBlock.hidden = false;
            } else {
                var block = el('result-completed', this.modal);
                var fe = el('final-events', this.modal);
                var fm = el('final-messages', this.modal);
                var fp = el('final-preset', this.modal);
                if (fe) fe.textContent = (p.eventsWritten || 0).toLocaleString();
                if (fm) fm.textContent = (p.messagesWritten || 0).toLocaleString();
                if (fp) fp.textContent = p.presetLabel || '…';
                if (block) block.hidden = false;
            }
            this.enterTerminalState();
            this.setSubmittingUi(false);
        } else if (p.state === 'Failed') {
            this.showFailure(p.errorMessage || 'Seeding failed.');
            this.setSubmittingUi(false);
        }
    };

    State.prototype.pushLog = function (line) {
        this.logEntries.push(line);
        while (this.logEntries.length > LOG_MAX) this.logEntries.shift();
        var list = el('progress-log', this.modal);
        if (!list) return;
        list.innerHTML = '';
        for (var i = 0; i < this.logEntries.length; i++) {
            var li = document.createElement('li');
            li.className = 'seed-progress-log__entry';
            li.textContent = this.logEntries[i];
            list.appendChild(li);
        }
    };

    State.prototype.showFailure = function (msg) {
        var block = el('result-failed', this.modal);
        var msgEl = el('error-message', this.modal);
        if (msgEl) msgEl.textContent = msg;
        if (block) block.hidden = false;
        this.enterTerminalState();
    };

    State.prototype.wireCancel = function () {
        var self = this;
        var btn = el('cancel-button', this.modal);
        if (!btn) return;
        btn.addEventListener('click', function () {
            if (!self.jobId) return;
            btn.disabled = true;
            btn.textContent = 'Cancelling…';
            var token = readAntiforgery(self.form);
            var url = self.form.getAttribute('data-cancel-url-base') + encodeURIComponent(self.jobId);
            fetch(url, {
                method: 'DELETE',
                credentials: 'same-origin',
                headers: token ? { 'X-XSRF-TOKEN': token } : {},
            }).then(function (resp) {
                if (!resp.ok) return null;
                // Server replies with a JSON body carrying the rollback counts +
                // the note the poll response will echo. The next poll tick will
                // observe Completed-with-cancelled-note anyway, but reading the
                // body here means the modal can render the rollback outcome
                // instantly without waiting a poll interval.
                return resp.json().catch(function () { return null; });
            }).then(function (body) {
                if (!body) return;
                // Best-effort: patch the cancelled note the poll will echo so the
                // modal shows the rollback count immediately.
                if (body.note && typeof body.note === 'string') {
                    var cancelNote = el('cancelled-note', self.modal);
                    if (cancelNote) cancelNote.textContent = body.note;
                }
            }).catch(function () {
                btn.disabled = false;
                var idleLabel = btn.getAttribute('data-idle-label') || 'Cancel seeding';
                btn.textContent = idleLabel;
            });
        });
    };

    State.prototype.wireClose = function () {
        var self = this;
        var btn = el('close-button', this.modal);
        if (!btn) return;
        btn.addEventListener('click', function () {
            self.closeModal();
            // Keep polling in the background so the modal shows fresh state if reopened.
        });
        // The X in the header dispatches [data-modal-close], handled by confirm-modal.js.
    };

    State.prototype.wireRetry = function () {
        var self = this;
        var btn = el('retry-button', this.modal);
        if (!btn) return;
        btn.addEventListener('click', function () {
            self.jobId = null;
            self.submitAsync();
        });
    };

    document.addEventListener('DOMContentLoaded', function () {
        var form = document.getElementById('seed-sample-search-data-form');
        var modal = document.getElementById('seed-progress-modal');
        if (!form || !modal) return;
        var state = new State();
        state.init(form, modal);
    });
})();
