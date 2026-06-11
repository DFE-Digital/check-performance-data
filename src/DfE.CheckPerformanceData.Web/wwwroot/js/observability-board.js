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

        // Render the most recent transitions into the accessible live region, and animate a token
        // for each one to its stage so the visual and textual views stay in step.
        function updateTransitions(transitions) {
            var list = transitions || [];
            if (transitionsList) {
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
            }
            // Animate a single token per transition to its stage; reduced-motion snaps it there.
            list.forEach(function (t) {
                var token = makeToken(t);
                placeToken(token, 'submit');
                // Move on the next frame so the transition has a from-state to ease from.
                window.requestAnimationFrame(function () {
                    placeToken(token, stageKeyFor(t));
                });
                // Tokens are ephemeral animation marks; clear them once they have arrived.
                window.setTimeout(function () {
                    if (token.parentNode) { token.parentNode.removeChild(token); }
                }, reduce ? 0 : 1200);
            });
        }

        function onSnapshot(snapshot) {
            if (reconnectNotice) { reconnectNotice.hidden = true; }
            if (!snapshot) { return; }
            updateCounts(snapshot.depths || snapshot.Depths);
            updateTransitions(snapshot.recentTransitions || snapshot.RecentTransitions);
        }

        function onError() {
            if (reconnectNotice) { reconnectNotice.hidden = false; }
        }

        return { onSnapshot: onSnapshot, onError: onError };
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

    // The single entry point. Given a board root and a feed, wire the feed to the renderer. The
    // recorded-replay feed (later) is any object with the same subscribe(onSnapshot, onError) shape.
    function start(root, feed) {
        var engine = BoardEngine(root);
        feed.subscribe(engine.onSnapshot, engine.onError);
        return engine;
    }

    function init() {
        var root = document.querySelector('[data-obs-board]');
        if (!root) { return; }
        var streamUrl = root.getAttribute('data-stream-url') || '/admin/observability/stream';
        if (!('EventSource' in window)) { return; }
        start(root, liveFeed(streamUrl));
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Expose the engine factory for the recorded-replay feed to reuse the same renderer.
    window.ObservabilityBoard = { start: start, liveFeed: liveFeed };
})();
