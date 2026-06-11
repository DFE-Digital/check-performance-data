(function () {
    'use strict';

    // Workflow board animation engine. One renderer, two feeds:
    //   - the live feed subscribes to the server-sent snapshot stream and pushes each snapshot in;
    //   - a recorded feed (added later) replays a fetched events array on a clock.
    // Both call the same single entry point (start) with a feed object exposing subscribe(onSnapshot),
    // so slow-mo / single-step / replay become clock manipulations over this renderer rather than a
    // re-architecture. All token motion is gated behind prefers-reduced-motion: under reduce the
    // tokens snap (no transition) and state is conveyed by position + colour + shape + text.

    var STAGE_ORDER = ['submit', 'rules-queue', 'rules-engine', 'zendesk-queue', 'ticket'];

    // Map a queue depth / age health-ish signal to the three-state node class. Kept deliberately
    // simple: any waiting work backs the stage up; the health strip owns the authoritative red.
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

        // Position a token at a stage lane. Under reduced motion we snap (no transition); otherwise
        // the CSS transition (gated in observability.css behind no-preference) eases the transform.
        function placeToken(token, stageKey) {
            var lane = lanes[stageKey];
            if (!lane) { return; }
            var laneRect = lane.getBoundingClientRect();
            var rootRect = root.getBoundingClientRect();
            var x = (laneRect.left - rootRect.left) + (laneRect.width / 2) - 10;
            var y = (laneRect.top - rootRect.top);
            if (reduce) {
                token.style.transition = 'none';
            }
            token.style.transform = 'translate(' + x + 'px, ' + y + 'px)';
            token.setAttribute('data-stage', stageKey);
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

        // Fetch and show the inspect panel for one token. Non-destructive; keyboard- and click-driven.
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

        function makeToken(transition) {
            var token = document.createElement('span');
            token.className = 'obs-board__token';
            token.setAttribute('tabindex', '0');
            token.setAttribute('role', 'button');
            token.setAttribute('aria-label', accessibleName(transition) + ' — inspect this message');
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
        // newer than it. Without the watermark every heartbeat would re-spawn tokens for events
        // that may be hours old and rewrite the aria-live list, making screen readers re-announce
        // all of them every tick. Synthetic transitions (single-step, demo trickle, replay) carry
        // no watermark obligation: they pass forceAnimate so deliberate re-presentation still runs.
        var lastSeenUtc = null;
        var listRendered = false;

        function timestampOf(transition) {
            var raw = transition.recordedAtUtc || transition.RecordedAtUtc;
            if (!raw) { return null; }
            var ms = Date.parse(raw);
            return isNaN(ms) ? null : ms;
        }

        // Render the most recent transitions into the accessible live region, and animate a token
        // for each NEW one to its stage so the visual and textual views stay in step. When nothing
        // is new, the live region is left untouched so nothing is re-announced.
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

            // Only touch the live region when there is something new to say (or on first render,
            // so the empty state appears once). An unchanged list stays silent for screen readers.
            if (transitionsList && (fresh.length > 0 || !listRendered)) {
                transitionsList.innerHTML = '';
                if (list.length === 0) {
                    var empty = document.createElement('li');
                    empty.className = 'obs-board__transitions-empty';
                    empty.textContent = 'No transitions yet.';
                    transitionsList.appendChild(empty);
                } else {
                    list.forEach(function (t) {
                        var li = document.createElement('li');
                        li.textContent = accessibleName(t);
                        transitionsList.appendChild(li);
                    });
                }
                listRendered = true;
            }

            // Animate a single token per NEW transition to its stage; reduced-motion snaps it there.
            fresh.forEach(function (t) {
                var token = makeToken(t);
                placeToken(token, 'submit');
                // Move on the next frame so the transition has a from-state to ease from.
                window.requestAnimationFrame(function () {
                    placeToken(token, stageKeyFor(t));
                });
                // Tokens are ephemeral animation marks; clear them once they have arrived. The
                // lifetime is scaled by the slow-mo clock so a demo can linger on each move.
                window.setTimeout(function () {
                    if (token.parentNode) { token.parentNode.removeChild(token); }
                }, reduce ? 0 : 1200 * moveSpeed);
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
        // synthetic tokens through it, and replay drives it from a scrubber — all over this one
        // renderer, no re-architecture. moveSpeed scales how long a token lingers; demoTimer is
        // the auto-trickle interval handle.
        var moveSpeed = 1;
        var demoTimer = null;

        function setSlowMo(factor) {
            // A factor > 1 slows motion down (tokens linger longer) for a demo; 1 is normal.
            moveSpeed = (factor && factor > 0) ? factor : 1;
        }

        // Single-step: advance one synthetic token through the pipeline by hand, so a presenter
        // can walk an audience stage by stage. Reuses the same token renderer.
        function singleStep() {
            onSnapshot({
                recentTransitions: [{ referenceNumber: 'STEP-' + Date.now(), stage: 'submit' }],
                depths: [],
                forceAnimate: true,
            });
        }

        // Demo-mode auto-trickle: inject one synthetic token on an interval so the board stays
        // alive during a demo even with no real traffic. Returns whether it is now running.
        function toggleDemoMode() {
            if (demoTimer) {
                window.clearInterval(demoTimer);
                demoTimer = null;
                return false;
            }
            demoTimer = window.setInterval(function () {
                var stages = ['submit', 'rules-engine', 'zendesk-queue', 'ticket'];
                var stage = stages[Math.floor(Math.random() * stages.length)];
                onSnapshot({
                    recentTransitions: [{ referenceNumber: 'DEMO-' + Date.now(), stage: stage }],
                    depths: [],
                    forceAnimate: true,
                });
            }, 1500);
            return true;
        }

        return {
            onSnapshot: onSnapshot,
            onError: onError,
            setSlowMo: setSlowMo,
            singleStep: singleStep,
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
    // replay endpoint, then plays them into the same renderer on a scrubber-controlled clock —
    // the engine never knows the difference between live and recorded. Exposes load(from, to),
    // seek(index) and the count so a scrubber UI can drive it. Same subscribe shape as the live
    // feed so it plugs into start(...) identically.
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
            // Play the recorded events up to and including the scrubber position so dragging the
            // scrubber re-animates the window; each step feeds one recorded transition. Replay is
            // deliberate re-presentation of old events, so it bypasses the live-feed novelty
            // watermark via forceAnimate.
            seek: function (index) {
                if (!sink) { return; }
                var e = events[index];
                if (!e) { return; }
                sink({ recentTransitions: [e], depths: [], forceAnimate: true });
            },
        };
    }

    // The single entry point. Given a board root and a feed, wire the feed to the renderer. The
    // recorded-replay feed is any object with the same subscribe(onSnapshot, onError) shape.
    function start(root, feed) {
        var engine = BoardEngine(root);
        feed.subscribe(engine.onSnapshot, engine.onError);
        return engine;
    }

    // Wire the always-on replay scrubber (present in every environment) and the dev-only control
    // group (slow-mo / single-step / demo-mode), which the server-side Razor conditional only
    // renders when Dev:ToolsEnabled. Each control is keyboard-operable; the scrubber announces
    // its position via aria-valuetext and a live region.
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
                feed.seek(idx);
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
        var step = root.querySelector('[data-obs-step]');
        if (step) {
            step.addEventListener('click', function () { engine.singleStep(); });
        }
        var demo = root.querySelector('[data-obs-demo]');
        if (demo) {
            demo.addEventListener('click', function () {
                var on = engine.toggleDemoMode();
                demo.setAttribute('aria-pressed', on ? 'true' : 'false');
                demo.textContent = on ? 'Stop demo trickle' : 'Start demo trickle';
            });
        }
    }

    function init() {
        var root = document.querySelector('[data-obs-board]');
        if (!root) { return; }
        var streamUrl = root.getAttribute('data-stream-url') || '/admin/observability/stream';
        if (!('EventSource' in window)) { return; }
        var engine = start(root, liveFeed(streamUrl));
        // The replay scrubber (always-on) and the dev-only controls share the live engine, so
        // slow-mo / single-step / demo / replay are all clock+feed manipulations over one renderer.
        wireControls(root, engine);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Expose the engine factory and both feeds so tests and the recorded-replay scrubber reuse
    // the same renderer.
    window.ObservabilityBoard = { start: start, liveFeed: liveFeed, recordedFeed: recordedFeed };
})();
