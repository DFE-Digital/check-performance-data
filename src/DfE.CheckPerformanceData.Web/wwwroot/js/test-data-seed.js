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
    var ETA_MIN_ELAPSED_S = 5;
    var ETA_MIN_EVENTS = 100;
    var LOG_MAX = 5;
    // Rolling-window rate for ETA: keep the last N ticks so the estimate reflects
    // recent throughput rather than the cumulative rate since kickoff (which lags
    // heavily in the middle when batches are consistent-sized and only becomes
    // accurate near the end). N=10 at a 500 ms poll cadence gives a five-second
    // rolling window — smooth enough that the seconds-remaining number stops
    // jittering, short enough to reflect a genuine throughput change quickly.
    var ETA_WINDOW = 10;
    var ETA_MIN_SAMPLES = 3;

    function el(role, root) {
        return (root || document).querySelector('[data-role="' + role + '"]');
    }

    // Read the anti-forgery token — first from the form's hidden field (Html.AntiForgeryToken
    // renders <input name="__RequestVerificationToken" ...>), fall back to a <meta> tag if
    // present. Returns null when neither exists; the caller can then decide to fall back to
    // a full form submit rather than trying an unauthenticated fetch.
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
        // Rolling ETA samples: array of {tMs, events} objects, oldest first, max
        // ETA_WINDOW entries. See computeEtaSeconds for the rate math.
        this.etaSamples = [];
    }

    // Pure helper: given the running array of {tMs, events} samples and the total
    // event count, return the estimated seconds remaining. Returns null when there
    // is not enough data to estimate yet. Extracted for easy manual reasoning /
    // unit-test friendliness. Time input is milliseconds since the epoch.
    function computeEtaSeconds(samples, eventsTotal, startedAtMs, nowMs) {
        if (!eventsTotal || eventsTotal <= 0 || !samples || samples.length === 0) {
            return null;
        }
        var latest = samples[samples.length - 1];
        var written = latest.events || 0;
        if (written >= eventsTotal) return 0;
        var remaining = eventsTotal - written;
        var rate = 0; // events per second
        if (samples.length >= ETA_MIN_SAMPLES) {
            var oldest = samples[0];
            var spanS = (latest.tMs - oldest.tMs) / 1000;
            var deltaEvents = written - (oldest.events || 0);
            if (spanS > 0 && deltaEvents > 0) rate = deltaEvents / spanS;
        }
        if (rate <= 0) {
            // Fallback: cumulative rate since kickoff. Preserves the "nothing at
            // first is fine" behaviour when only 1-2 ticks are in.
            var elapsedS = (nowMs - startedAtMs) / 1000;
            if (elapsedS <= 0 || written <= 0) return null;
            rate = written / elapsedS;
        }
        if (rate <= 0) return null;
        var rawSeconds = remaining / rate;
        if (rawSeconds < 0) rawSeconds = 0;
        // Round to the nearest 5 s while the ETA is comfortably above 30 s so the
        // number stops jittering "25, 26, 25, 26" as consistent-size batches land
        // at ~1 s intervals. Below 30 s, switch to 1 s precision — the numbers
        // there are small enough that the perceived "twitch" is just accurate
        // countdown behaviour.
        if (rawSeconds >= 30) {
            return Math.round(rawSeconds / 5) * 5;
        }
        return Math.max(0, Math.round(rawSeconds));
    }
    // Expose for ad-hoc manual verification from DevTools; harmless in production.
    if (typeof window !== 'undefined') {
        window.__seedComputeEtaSeconds = computeEtaSeconds;
    }

    State.prototype.init = function (form, modal) {
        this.form = form;
        this.modal = modal;

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
            headers: { 'RequestVerificationToken': token },
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
        var eta = el('eta-line', this.modal);
        if (eta) eta.hidden = true;
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
        this.etaSamples = [];
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

        // Push the current tick onto the rolling ETA sample buffer. Only push
        // when eventsWritten actually changes so pure "still ticking" polls
        // don't dilute the rate calculation.
        var nowMs = Date.now();
        if ((p.eventsWritten || 0) !== (this.etaSamples.length > 0
                ? this.etaSamples[this.etaSamples.length - 1].events
                : -1)) {
            this.etaSamples.push({ tMs: nowMs, events: p.eventsWritten || 0 });
            while (this.etaSamples.length > ETA_WINDOW) this.etaSamples.shift();
        }

        // ETA — only show once we have enough runtime and enough rows for the
        // rolling-window rate to be meaningful (the same "nothing at first is
        // fine" behaviour the old cumulative-rate branch had).
        var elapsedS = (nowMs - this.startedAtMs) / 1000;
        var etaLine = el('eta-line', this.modal);
        if (etaLine) {
            if (elapsedS >= ETA_MIN_ELAPSED_S && (p.eventsWritten || 0) >= ETA_MIN_EVENTS
                && (p.eventsTotal || 0) > 0) {
                var etaSec = computeEtaSeconds(this.etaSamples, p.eventsTotal,
                    this.startedAtMs, nowMs);
                if (etaSec !== null) {
                    setText('eta-seconds', String(etaSec));
                    etaLine.hidden = false;
                }
            }
        }

        if (p.eventsWritten !== this.lastEventsWritten) {
            this.lastEventsWritten = p.eventsWritten;
            var line = formatTime(new Date().toISOString())
                + ' UTC — seeded ' + (p.eventsWritten || 0).toLocaleString() + ' events'
                + (p.currentCursorUtc ? ' (up to ' + formatDate(p.currentCursorUtc) + ')' : '');
            this.pushLog(line);
        }

        if (p.state === 'Completed') {
            var isCancelled = p.note && p.note.toLowerCase().indexOf('cancel') !== -1;
            if (isCancelled) {
                var cancelBlock = el('result-cancelled', this.modal);
                var cancelNote = el('cancelled-note', this.modal);
                if (cancelNote) {
                    // Prefer a formatted "N of M events" line so the user sees how
                    // far along the seed got before the cancel landed; fall back to
                    // the raw server note when the totals aren't populated.
                    var written = (p.eventsWritten || 0).toLocaleString();
                    var total = (p.eventsTotal || 0).toLocaleString();
                    if (p.eventsTotal && p.eventsTotal > 0) {
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
            var token = readAntiforgery(self.form);
            var url = self.form.getAttribute('data-cancel-url-base') + encodeURIComponent(self.jobId);
            fetch(url, {
                method: 'DELETE',
                credentials: 'same-origin',
                headers: token ? { 'RequestVerificationToken': token } : {},
            }).then(function () {
                // The next poll tick will observe the Completed-with-cancelled-note state.
            }).catch(function () {
                btn.disabled = false;
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
