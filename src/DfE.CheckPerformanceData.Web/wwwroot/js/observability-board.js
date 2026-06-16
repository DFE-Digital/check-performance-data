(function () {
    'use strict';

    // Workflow board animation engine. One renderer, two feeds:
    //   - the live feed subscribes to the server-sent snapshot stream and pushes each snapshot in;
    //   - a recorded feed replays a fetched events array on a clock.
    // Both call the same single entry point (start) with a feed object exposing subscribe(onSnapshot).
    // A message is rendered as a small blue ENVELOPE that travels along the REAL pipeline path:
    // it enters at Submit and walks box to box — Submit → Rules-queue → Rules-engine → Zendesk-queue
    // → Ticket — pausing at each stage. Positions are measured from the rendered stage nodes
    // (getBoundingClientRect), so the envelope is always anchored to the actual boxes, never a fixed
    // corner. A failed/injected message diverts to the dead-letter marker instead of reaching Ticket.
    // All motion is gated behind prefers-reduced-motion: under reduce the envelope snaps to its final
    // resting stage (no traversal, no transition) and state is conveyed by position + colour + text.

    var STAGE_ORDER = ['submit', 'rules-queue', 'rules-engine', 'zendesk-queue', 'ticket'];

    // The dashboard's "Recent transitions" panel is an at-a-glance summary, not the full log: it
    // shows at most this many of the latest transitions and links to the paged transactions page
    // for the complete history.
    var MAX_RECENT_TRANSITIONS = 10;

    // Base dwell at each stage in ms (scaled by the slow-mo clock). Kept short so a live board feels
    // lively; slow motion multiplies it for a demo.
    var STAGE_DWELL_MS = 380;

    // Map a queue depth to the three-state node class. Kept deliberately simple: any waiting work
    // backs the stage up; the health strip owns the authoritative red.
    function stateForCount(count) {
        if (!count || count <= 0) { return 'flowing'; }
        if (count < 25) { return 'backingup'; }
        return 'needsattention';
    }

    function ReducedMotion() {
        return window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    function BoardEngine(root) {
        var reduce = ReducedMotion();
        var inspectUrl = root.getAttribute('data-inspect-url') || '/admin/observability/inspect';
        var lanes = {};
        STAGE_ORDER.forEach(function (key) {
            lanes[key] = root.querySelector('.obs-board__lane[data-stage="' + key + '"]');
        });
        var transitionsList = root.querySelector('[data-obs-transitions]');
        var reconnectNotice = root.querySelector('[data-obs-reconnect]');
        var dlqMarker = root.querySelector('[data-obs-dlq]');

        // Geometry: the centre of a stage's node relative to the board root, so an absolutely
        // positioned envelope can be placed dead-centre on the box. Measured live each time so the
        // animation survives layout changes (responsive reflow, nav collapse). Falls back to an
        // even split of the board width if a node has not laid out yet.
        function anchorFor(stageKey) {
            var lane = lanes[stageKey];
            var rootRect = root.getBoundingClientRect();
            if (lane) {
                var node = lane.querySelector('.obs-board__node') || lane;
                var r = node.getBoundingClientRect();
                if (r.width > 0 || r.height > 0) {
                    return {
                        x: (r.left - rootRect.left) + (r.width / 2),
                        y: (r.top - rootRect.top) + (r.height / 2)
                    };
                }
            }
            var idx = STAGE_ORDER.indexOf(stageKey);
            var slot = rootRect.width / STAGE_ORDER.length;
            return { x: slot * (idx + 0.5), y: 40 };
        }

        function dlqAnchor() {
            var rootRect = root.getBoundingClientRect();
            if (dlqMarker) {
                var node = dlqMarker.querySelector('.obs-board__dlq-node') || dlqMarker;
                var r = node.getBoundingClientRect();
                if (r.width > 0 || r.height > 0) {
                    return {
                        x: (r.left - rootRect.left) + (r.width / 2),
                        y: (r.top - rootRect.top) + (r.height / 2)
                    };
                }
            }
            // Below the rules-engine box if the marker hasn't laid out.
            var a = anchorFor('rules-engine');
            return { x: a.x, y: a.y + 80 };
        }

        // Position an envelope at an anchor point (centre of a stage box, or the DLQ marker).
        // Under reduced motion we strip the transition so it snaps; otherwise the CSS transition
        // (gated behind no-preference) eases the transform.
        function placeAt(token, anchor) {
            if (reduce) { token.style.transition = 'none'; }
            // -50% offset is baked into the element's own translate via CSS transform-origin; here
            // we translate the top-left so the element centre lands on the anchor.
            token.style.transform = 'translate(' + (anchor.x) + 'px, ' + (anchor.y) + 'px) translate(-50%, -50%)';
        }

        function accessibleName(transition) {
            var ref = transition.referenceNumber || transition.ReferenceNumber || 'unknown';
            var stage = transition.stage || transition.Stage || 'pipeline';
            var decision = transition.decisionStatus || transition.DecisionStatus;
            var name = 'Request ' + ref + ', at ' + stage;
            if (decision) { name += ', decision ' + decision; }
            return name;
        }

        function stageKeyFor(transition) {
            var stage = (transition.stage || transition.Stage || '').toLowerCase();
            if (stage.indexOf('submit') >= 0) { return 'submit'; }
            if (stage.indexOf('ticket') >= 0) { return 'ticket'; }
            if (stage.indexOf('zendesk') >= 0) { return 'zendesk-queue'; }
            if (stage.indexOf('rules') >= 0) { return 'rules-engine'; }
            return 'rules-queue';
        }

        // Does this transition represent a failure that should divert to the dead-letter marker
        // rather than completing to Ticket? Recognised from an explicit flag or a failed/dead-letter
        // stage or decision.
        function isFailure(transition) {
            if (transition.failed === true || transition.Failed === true) { return true; }
            var stage = (transition.stage || transition.Stage || '').toLowerCase();
            var decision = (transition.decisionStatus || transition.DecisionStatus || '').toLowerCase();
            return stage.indexOf('dead') >= 0 || stage.indexOf('fail') >= 0
                || decision.indexOf('fail') >= 0;
        }

        // Fetch and show the inspect panel for one envelope. Non-destructive; keyboard- and click-driven.
        function inspect(reference) {
            fetch(inspectUrl + '/' + encodeURIComponent(reference), { credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.text() : ''; })
                .then(function (html) {
                    var panel = root.querySelector('[data-obs-inspect-panel]');
                    if (!panel) {
                        panel = document.createElement('div');
                        panel.setAttribute('data-obs-inspect-panel', '');
                        root.appendChild(panel);
                    }
                    panel.innerHTML = html;
                })
                .catch(function () { /* inspect is best-effort; a failed fetch leaves the board intact */ });
        }

        // The envelope element: an inline SVG envelope in GDS blue, keyboard-focusable and labelled,
        // positioned absolutely so it can be driven across the board by transform.
        function makeToken(transition) {
            var token = document.createElement('span');
            token.className = 'obs-board__token';
            token.setAttribute('tabindex', '0');
            token.setAttribute('role', 'button');
            token.setAttribute('aria-label', accessibleName(transition) + ' — inspect this message');
            token.innerHTML =
                '<svg viewBox="0 0 24 18" width="20" height="15" aria-hidden="true" focusable="false">' +
                '<rect x="1" y="1" width="22" height="16" rx="2" fill="#1d70b8" stroke="#0b0c0c" stroke-width="1.5"/>' +
                '<path d="M2 3 L12 11 L22 3" fill="none" stroke="#ffffff" stroke-width="1.5"/>' +
                '</svg>';
            var reference = transition.referenceNumber || transition.ReferenceNumber || '';
            token.addEventListener('click', function () { inspect(reference); });
            token.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    inspect(reference);
                }
            });
            root.appendChild(token);
            return token;
        }

        // Walk an envelope from Submit along the stage path to its destination, pausing at each box.
        // A failed message stops at Rules-engine then diverts to the dead-letter marker; a normal one
        // runs to Ticket (or wherever its reported stage is). Under reduced motion the envelope is
        // placed once at the destination with no traversal, then removed.
        function flyEnvelope(token, destKey, failed) {
            if (reduce) {
                placeAt(token, failed ? dlqAnchor() : anchorFor(destKey));
                window.setTimeout(function () {
                    if (token.parentNode) { token.parentNode.removeChild(token); }
                }, 0);
                return;
            }

            // Build the path of stage keys to walk. We always enter at submit; the destination is the
            // reported stage. For a failure we walk as far as rules-engine then divert to the DLQ.
            var destIdx = STAGE_ORDER.indexOf(destKey);
            if (destIdx < 0) { destIdx = STAGE_ORDER.length - 1; }
            var path = [];
            var stopIdx = failed ? Math.min(STAGE_ORDER.indexOf('rules-engine'), destIdx) : destIdx;
            for (var i = 0; i <= stopIdx; i++) { path.push(STAGE_ORDER[i]); }

            var dwell = STAGE_DWELL_MS * moveSpeed;

            // Place at submit immediately, then step along the path on the clock so the CSS transition
            // eases each hop. requestAnimationFrame gives the first placement a from-state.
            placeAt(token, anchorFor('submit'));
            var hop = 0;
            window.requestAnimationFrame(function () {
                function next() {
                    hop += 1;
                    if (hop < path.length) {
                        placeAt(token, anchorFor(path[hop]));
                        window.setTimeout(next, dwell);
                    } else if (failed) {
                        // Divert to the dead-letter marker and reveal it.
                        if (dlqMarker) { dlqMarker.setAttribute('data-obs-dlq-active', 'true'); }
                        placeAt(token, dlqAnchor());
                        window.setTimeout(function () {
                            if (token.parentNode) { token.parentNode.removeChild(token); }
                        }, dwell * 2);
                    } else {
                        // Arrived; let it rest a beat at its final box then clear it.
                        window.setTimeout(function () {
                            if (token.parentNode) { token.parentNode.removeChild(token); }
                        }, dwell * 2);
                    }
                }
                window.setTimeout(next, dwell);
            });
        }

        // Update the per-stage counts (board + accessible parallel) from the snapshot depths.
        function updateCounts(depths) {
            (depths || []).forEach(function (d) {
                var queue = d.queueName || d.QueueName;
                var depth = (d.depth !== undefined ? d.depth : d.Depth) || 0;
                var key = queue === 'zendesk' ? 'zendesk-queue' : (queue === 'rules-engine' ? 'rules-queue' : null);
                if (!key) { return; }
                var lane = lanes[key];
                if (lane) {
                    lane.setAttribute('data-state', stateForCount(depth));
                    var count = lane.querySelector('[data-stage-count="' + key + '"]');
                    if (count) { count.textContent = depth + ' waiting'; }
                }
                var parallel = root.querySelector('[data-stage-parallel="' + key + '"]');
                if (parallel) { parallel.textContent = depth + ' waiting'; }
            });
        }

        // The snapshot stream returns the latest transitions regardless of novelty, so the engine
        // tracks the newest RecordedAtUtc it has rendered and only animates/announces transitions
        // newer than it. Without the watermark every heartbeat would re-spawn envelopes for events
        // that may be hours old and rewrite the aria-live list. Synthetic transitions (single-step,
        // demo trickle, replay) carry no watermark obligation: they pass forceAnimate so deliberate
        // re-presentation still runs.
        var lastSeenUtc = null;
        var listRendered = false;

        function timestampOf(transition) {
            var raw = transition.recordedAtUtc || transition.RecordedAtUtc;
            if (!raw) { return null; }
            var ms = Date.parse(raw);
            return isNaN(ms) ? null : ms;
        }

        function updateTransitions(transitions, forceAnimate) {
            var list = transitions || [];
            var fresh = list;
            if (!forceAnimate) {
                fresh = list.filter(function (t) {
                    var ts = timestampOf(t);
                    return ts === null || lastSeenUtc === null || ts > lastSeenUtc;
                });
                list.forEach(function (t) {
                    var ts = timestampOf(t);
                    if (ts !== null && (lastSeenUtc === null || ts > lastSeenUtc)) { lastSeenUtc = ts; }
                });
            }

            // Only touch the live region when there is something new to say (or on first render).
            if (transitionsList && (fresh.length > 0 || !listRendered)) {
                transitionsList.innerHTML = '';
                if (list.length === 0) {
                    var empty = document.createElement('li');
                    empty.className = 'obs-board__transitions-empty';
                    empty.textContent = 'No transitions yet.';
                    transitionsList.appendChild(empty);
                } else {
                    // The dashboard shows only the most recent handful; the full, paged history
                    // lives on the transactions page (the "more" link beneath this list). Capping
                    // here keeps the at-a-glance panel short without dropping any data — it is all
                    // on the transactions page.
                    list.slice(0, MAX_RECENT_TRANSITIONS).forEach(function (t) {
                        var li = document.createElement('li');
                        li.textContent = accessibleName(t);
                        transitionsList.appendChild(li);
                    });
                }
                listRendered = true;
            }

            // Fly one envelope per NEW transition along the stage path to its destination.
            fresh.forEach(function (t) {
                var token = makeToken(t);
                flyEnvelope(token, stageKeyFor(t), isFailure(t));
            });
        }

        function onSnapshot(snapshot) {
            if (reconnectNotice) { reconnectNotice.hidden = true; }
            if (!snapshot) { return; }
            updateCounts(snapshot.depths || snapshot.Depths);
            updateTransitions(
                snapshot.recentTransitions || snapshot.RecentTransitions,
                !!snapshot.forceAnimate);
        }

        function onError() {
            if (reconnectNotice) { reconnectNotice.hidden = false; }
        }

        // The clock seam. slow-mo scales it, single-step advances it by hand, demo-mode trickles
        // synthetic envelopes through it, and replay drives it from a scrubber — all over this one
        // renderer. moveSpeed scales how long an envelope dwells at each box; demoTimer is the
        // auto-trickle interval handle.
        var moveSpeed = 1;
        var demoTimer = null;

        function setSlowMo(factor) {
            moveSpeed = (factor && factor > 0) ? factor : 1;
        }

        // Single-step: send one synthetic envelope all the way through the pipeline so a presenter
        // can walk an audience Submit → Ticket. forceAnimate bypasses the novelty watermark.
        function singleStep() {
            onSnapshot({
                recentTransitions: [{ referenceNumber: 'STEP-' + Date.now(), stage: 'ticket' }],
                depths: [],
                forceAnimate: true,
            });
        }

        // Inject a failing envelope: it walks to Rules-engine then diverts to the dead-letter marker.
        function injectFailure() {
            onSnapshot({
                recentTransitions: [{ referenceNumber: 'FAIL-' + Date.now(), stage: 'rules-engine', failed: true }],
                depths: [],
                forceAnimate: true,
            });
        }

        // Demo-mode auto-trickle: inject one synthetic envelope (running the full path) on an
        // interval so the board stays alive during a demo even with no real traffic. Returns whether
        // it is now running.
        function toggleDemoMode() {
            if (demoTimer) {
                window.clearInterval(demoTimer);
                demoTimer = null;
                return false;
            }
            demoTimer = window.setInterval(function () {
                onSnapshot({
                    recentTransitions: [{ referenceNumber: 'DEMO-' + Date.now(), stage: 'ticket' }],
                    depths: [],
                    forceAnimate: true,
                });
            }, Math.max(900, 1500 * moveSpeed));
            return true;
        }

        return {
            onSnapshot: onSnapshot,
            onError: onError,
            setSlowMo: setSlowMo,
            singleStep: singleStep,
            injectFailure: injectFailure,
            toggleDemoMode: toggleDemoMode,
        };
    }

    // The live feed: an EventSource over the role-gated SSE stream. EventSource reconnects on its
    // own; on error we surface the reconnect notice and let it recover.
    function liveFeed(streamUrl) {
        return {
            subscribe: function (onSnapshot, onError) {
                var es = new EventSource(streamUrl);
                es.addEventListener('snapshot', function (e) {
                    try { onSnapshot(JSON.parse(e.data)); } catch (err) { /* ignore a malformed frame */ }
                });
                es.onerror = function () { onError(); };
                return es;
            }
        };
    }

    // The recorded/replay feed. It fetches the recorded events for a window from the always-on
    // replay endpoint, then plays them into the same renderer on a scrubber-controlled clock — the
    // engine never knows the difference between live and recorded. Exposes load(from, to),
    // seek(index) and the count so a scrubber UI can drive it.
    function recordedFeed(replayUrl) {
        var events = [];
        var sink = null;
        return {
            subscribe: function (onSnapshot) {
                sink = onSnapshot;
            },
            load: function (fromIso, toIso) {
                var url = replayUrl + '?from=' + encodeURIComponent(fromIso) + '&to=' + encodeURIComponent(toIso);
                return fetch(url, { credentials: 'same-origin' })
                    .then(function (r) { return r.ok ? r.json() : []; })
                    .then(function (data) { events = data || []; return events.length; })
                    .catch(function () { events = []; return 0; });
            },
            count: function () { return events.length; },
            // Animate the recorded event at the scrubber position through the board; each step flies
            // one envelope along the stage path. Replay is deliberate re-presentation of old events,
            // so it bypasses the live-feed novelty watermark via forceAnimate.
            seek: function (index) {
                if (!sink) { return; }
                var e = events[index];
                if (!e) { return; }
                sink({ recentTransitions: [e], depths: [], forceAnimate: true });
            },
        };
    }

    // The single entry point. Given a board root and a feed, wire the feed to the renderer.
    function start(root, feed) {
        var engine = BoardEngine(root);
        feed.subscribe(engine.onSnapshot, engine.onError);
        return engine;
    }

    // Wire the always-on replay scrubber and the dev-only control group (slow-mo / single-step /
    // demo-mode / inject-failure), which the server-side Razor conditional only renders when
    // Dev:ToolsEnabled. Single step and demo trickle are checkboxes (like slow motion). Each control
    // is keyboard-operable; the scrubber announces its position via aria-valuetext and a live region.
    function wireControls(root, engine) {
        // --- Always-on replay scrubber ---
        var replayUrl = root.getAttribute('data-replay-url') || '/admin/observability/replay';
        var scrubber = root.querySelector('[data-obs-scrubber]');
        var playBtn = root.querySelector('[data-obs-replay-play]');
        var status = root.querySelector('[data-obs-replay-status]');
        if (scrubber) {
            var feed = recordedFeed(replayUrl);
            feed.subscribe(engine.onSnapshot);
            var playing = false;
            var playTimer = null;

            function announce(index, total) {
                var text = total > 0
                    ? ('Event ' + (index + 1) + ' of ' + total)
                    : 'No recorded events in this window';
                scrubber.setAttribute('aria-valuetext', text);
                if (status) { status.textContent = text; }
            }

            function loadWindow() {
                var to = new Date();
                var from = new Date(to.getTime() - 24 * 60 * 60 * 1000);
                return feed.load(from.toISOString(), to.toISOString()).then(function (count) {
                    scrubber.max = count > 0 ? (count - 1) : 0;
                    scrubber.value = 0;
                    announce(0, count);
                });
            }

            scrubber.addEventListener('input', function () {
                var idx = parseInt(scrubber.value, 10) || 0;
                feed.seek(idx); // animates an envelope through the stages for this recorded event
                announce(idx, feed.count());
            });

            if (playBtn) {
                playBtn.addEventListener('click', function () {
                    playing = !playing;
                    playBtn.setAttribute('aria-pressed', playing ? 'true' : 'false');
                    playBtn.textContent = playing ? 'Pause' : 'Play';
                    if (playing) {
                        playTimer = window.setInterval(function () {
                            var idx = parseInt(scrubber.value, 10) || 0;
                            if (idx >= feed.count() - 1) {
                                playing = false;
                                playBtn.setAttribute('aria-pressed', 'false');
                                playBtn.textContent = 'Play';
                                window.clearInterval(playTimer);
                                return;
                            }
                            scrubber.value = idx + 1;
                            feed.seek(idx + 1);
                            announce(idx + 1, feed.count());
                        }, 800);
                    } else if (playTimer) {
                        window.clearInterval(playTimer);
                    }
                });
            }

            loadWindow();
        }

        // --- Dev-only controls (only in the DOM when Dev:ToolsEnabled rendered them) ---
        var slowMo = root.querySelector('[data-obs-slowmo]');
        if (slowMo) {
            slowMo.addEventListener('change', function () {
                engine.setSlowMo(slowMo.checked ? 4 : 1);
            });
        }

        // Single step is now a checkbox: each time it is ticked it sends one envelope all the way
        // through, then resets itself so it can be ticked again.
        var step = root.querySelector('[data-obs-step]');
        if (step) {
            step.addEventListener('change', function () {
                if (step.checked) {
                    engine.singleStep();
                    step.checked = false;
                }
            });
        }

        // Demo trickle is a checkbox: ticking it starts the auto-trickle, unticking it stops.
        var demo = root.querySelector('[data-obs-demo]');
        if (demo) {
            demo.addEventListener('change', function () {
                var on = engine.toggleDemoMode();
                demo.checked = on;
            });
        }

        // The inject-failure trigger also drives the board so the diversion is visible even before
        // the server-side seed round-trips. The confirm-modal still posts to the seed endpoint; this
        // gives immediate on-board feedback.
        var inject = root.querySelector('[data-obs-inject]');
        if (inject) {
            inject.addEventListener('click', function () { engine.injectFailure(); });
        }
    }

    var sharedEngine = null;

    function init() {
        var root = document.querySelector('[data-obs-board]');
        if (!root) { return; }
        var streamUrl = root.getAttribute('data-stream-url') || '/admin/observability/stream';
        if (!('EventSource' in window)) { return; }
        var engine = start(root, liveFeed(streamUrl));
        sharedEngine = engine;
        wireControls(root, engine);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Expose the engine factory and both feeds so tests and the recorded-replay scrubber reuse the
    // same renderer. refresh() lets a host page (the Debug Pipeline AJAX drives) nudge the board to
    // show immediate movement after a drive, without a full-page reload.
    window.ObservabilityBoard = {
        start: start,
        liveFeed: liveFeed,
        recordedFeed: recordedFeed,
        refresh: function () { if (sharedEngine) { sharedEngine.singleStep(); } }
    };
})();
