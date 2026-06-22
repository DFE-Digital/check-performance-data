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

    // The pipeline-state matrix shows at most this many of the latest messages (newest at the top);
    // older ones scroll off the visible area and the full, paged history lives on the transactions
    // page. A generous cap so the scroll area has depth without unbounded DOM growth.
    var GRID_MAX_ROWS = 50;

    // Base dwell at each stage in ms (scaled by the slow-mo clock). Kept short so a live board feels
    // lively; slow motion multiplies it for a demo.
    var STAGE_DWELL_MS = 380;

    // Per-stage dwell so envelopes DESYNC instead of marching in lockstep: each box holds an
    // envelope for a duration roughly tracking that stage's typical work. The queue stages dwell
    // longer (messages wait), the engine longest (it does the evaluation), Submit and the terminal
    // Zendesk ticket are quick hand-offs. Any stage without an explicit entry falls back to the base.
    var STAGE_DWELL_BY_KEY = {
        'submit': 220,
        'rules-queue': 460,
        'rules-engine': 620,
        'zendesk-queue': 400,
        'ticket': 260,
    };

    // The dwell an envelope spends AT a given stage before hopping on, scaled by the slow-mo clock.
    // A per-call random jitter (±~35%) desyncs envelopes PER MESSAGE as well as per stage, so two
    // envelopes that entered a stage together don't march out in lockstep.
    function dwellFor(stageKey, speed) {
        var base = STAGE_DWELL_BY_KEY[stageKey];
        if (!base) { base = STAGE_DWELL_MS; }
        var jitter = 0.65 + Math.random() * 0.7;
        return base * (speed || 1) * jitter;
    }

    // When several envelopes occupy the same box at once they offset diagonally by this many px per
    // envelope so the pile is visible rather than overlapping exactly. Beyond STACK_MAX_VISIBLE the
    // engine stops rendering individual envelopes and shows a "+N" count overlay instead, so a busy
    // box stays legible.
    var STACK_OFFSET = 6;
    var STACK_MAX_VISIBLE = 4;

    // The three decision outcomes a decided message routes into. The values match the destination
    // boxes' data-obs-decision keys and the transition's decisionStatus, so an envelope can be sent
    // to the anchor of the box matching its decision.
    var DECISION_KEYS = ['AutoApproved', 'AutoRejected', 'Scrutiny'];

    // Every envelope ENTERS blue and stays blue through Submit → Rules-queue → Rules-engine; it only
    // takes its outcome colour once it lands in a status box — approved GDS green, rejected GDS red,
    // scrutiny GDS yellow — or GDS orange for the dead-letter queue. The canonical green/red/yellow
    // match the charts. In-flight (and any undecided) envelope is blue.
    var IN_FLIGHT_COLOUR = '#1d70b8';   // GDS blue
    var DEADLETTER_COLOUR = '#f47738';  // GDS orange
    var DECISION_COLOURS = {
        'AutoApproved': '#00703c',      // GDS green
        'AutoRejected': '#d4351c',      // GDS red
        'Scrutiny': '#ffdd00',          // GDS yellow
    };

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
        var gridBody = root.querySelector('[data-obs-grid]');
        var reconnectNotice = root.querySelector('[data-obs-reconnect]');
        var dlqMarker = root.querySelector('[data-obs-dlq]');

        // The headline tiles live above the board (siblings, not under the board root), so resolve
        // them at document level. The engine keeps them live from the SSE snapshot: the depth tile
        // tracks the summed queue depths, and the processed-today tile ticks up as messages complete.
        var processedTile = document.querySelector('[data-obs-tile-processed]');
        var depthTile = document.querySelector('[data-obs-tile-depth]');

        // The 24-hour decision counters (auto-approved / auto-rejected / scrutiny / dead-letter),
        // kept live alongside the processed-today tile: as each decided or failed submission
        // completes, the matching counter ticks up by one so the headline breakdown stays current
        // without a page refresh.
        var decisionTiles = {
            'AutoApproved': document.querySelector('[data-obs-tile-approved]'),
            'AutoRejected': document.querySelector('[data-obs-tile-rejected]'),
            'Scrutiny': document.querySelector('[data-obs-tile-scrutiny]'),
        };
        var deadletterTile = document.querySelector('[data-obs-tile-deadletter]');

        // Increment a counter element's integer text by one, tolerating thousands separators and a
        // blank/em-dash starting value.
        function bumpCounter(el) {
            if (!el) { return; }
            var n = parseInt((el.textContent || '').replace(/[^0-9-]/g, ''), 10);
            if (isNaN(n)) { n = 0; }
            el.textContent = (n + 1);
        }

        function bumpProcessed() {
            bumpCounter(processedTile);
        }

        // Tick the counter matching a completed submission's outcome: its decision box, or the
        // dead-letter counter for a failure. A decided submission also counts toward "processed".
        function bumpDecision(decisionKey, failed) {
            if (failed) { bumpCounter(deadletterTile); return; }
            if (decisionKey && decisionTiles[decisionKey]) { bumpCounter(decisionTiles[decisionKey]); }
        }

        function updateDepthTile(depths) {
            if (!depthTile) { return; }
            var sum = 0;
            (depths || []).forEach(function (d) {
                sum += (d.depth !== undefined ? d.depth : d.Depth) || 0;
            });
            depthTile.textContent = sum;
        }

        // The decision-destination boxes, keyed by decisionStatus, so a decided envelope can anchor
        // to its box and light it up as it lands.
        var decisionMarkers = {};
        DECISION_KEYS.forEach(function (key) {
            decisionMarkers[key] = root.querySelector('[data-obs-decision="' + key + '"]');
        });

        // Per-anchor occupancy: how many envelopes currently rest at each box, keyed by an anchor id
        // (a stage key, a decision key, or 'dlq'). Drives the diagonal stack offset and the "+N"
        // count overlay. The overlay elements are kept per anchor so they can be updated/removed.
        var occupancy = {};
        var stackOverlays = {};

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

        // The centre of a decision-destination box, so a decided envelope routes into the box
        // matching its decisionStatus. Falls back to the dead-letter row height under the rules
        // engine if the box has not laid out yet.
        function decisionAnchor(decisionKey) {
            var rootRect = root.getBoundingClientRect();
            var marker = decisionMarkers[decisionKey];
            if (marker) {
                var node = marker.querySelector('.obs-board__dlq-node') || marker;
                var r = node.getBoundingClientRect();
                if (r.width > 0 || r.height > 0) {
                    return {
                        x: (r.left - rootRect.left) + (r.width / 2),
                        y: (r.top - rootRect.top) + (r.height / 2)
                    };
                }
            }
            var a = anchorFor('rules-engine');
            return { x: a.x, y: a.y + 80 };
        }

        // Clear the dead-letter box's active (lit) state once no envelope is resting in it. Without
        // this the DLQ marker stayed lit forever after a failed message left — it read as a stuck
        // half-coloured circle on the board. Mirrors the decision boxes, which already self-clear.
        function clearDlqIfEmpty() {
            if (dlqMarker && (occupancy['dlq'] || 0) <= 0) {
                dlqMarker.removeAttribute('data-obs-dlq-active');
            }
        }

        // Light up a decision box (or clear it) as envelopes land/leave, mirroring the DLQ marker's
        // active attribute. Conveyed by the box's fill AND its always-present label.
        function setDecisionActive(decisionKey, active) {
            var marker = decisionMarkers[decisionKey];
            if (marker) {
                if (active) { marker.setAttribute('data-obs-decision-active', 'true'); }
                else { marker.removeAttribute('data-obs-decision-active'); }
            }
        }

        // --- Per-anchor occupancy: diagonal stacking + a "+N" overlay when crowded ---

        // Claim a slot at an anchor; returns the zero-based index of this envelope in the pile so it
        // can be offset diagonally. Refreshes the overlay if the pile crosses the visible threshold.
        function enterAnchor(anchorId, anchor) {
            var count = (occupancy[anchorId] || 0) + 1;
            occupancy[anchorId] = count;
            refreshStackOverlay(anchorId, anchor);
            return count - 1;
        }

        // Release a slot at an anchor when an envelope leaves/clears.
        function leaveAnchor(anchorId, anchor) {
            var count = (occupancy[anchorId] || 0) - 1;
            occupancy[anchorId] = count > 0 ? count : 0;
            refreshStackOverlay(anchorId, anchor);
        }

        // The diagonal offset for the Nth envelope at a box, so a pile fans out rather than
        // overlapping exactly. Capped at the visible threshold so the overlay (not more envelopes)
        // represents the rest.
        function stackOffset(index) {
            var i = Math.min(index, STACK_MAX_VISIBLE);
            return { x: i * STACK_OFFSET, y: i * STACK_OFFSET };
        }

        // Show/update/remove the "+N" overlay for a box: once more than STACK_MAX_VISIBLE envelopes
        // pile up, a small badge reading the surplus count replaces rendering every envelope.
        function refreshStackOverlay(anchorId, anchor) {
            var count = occupancy[anchorId] || 0;
            var overlay = stackOverlays[anchorId];
            if (count > STACK_MAX_VISIBLE) {
                if (!overlay) {
                    overlay = document.createElement('span');
                    overlay.className = 'obs-board__stack-count';
                    overlay.setAttribute('aria-hidden', 'true');
                    root.appendChild(overlay);
                    stackOverlays[anchorId] = overlay;
                }
                overlay.textContent = '+' + (count - STACK_MAX_VISIBLE);
                if (anchor) {
                    overlay.style.transform =
                        'translate(' + anchor.x + 'px, ' + anchor.y + 'px) translate(6px, -18px)';
                }
            } else if (overlay) {
                if (overlay.parentNode) { overlay.parentNode.removeChild(overlay); }
                delete stackOverlays[anchorId];
            }
        }

        // --- Live per-box in-flight counts ---
        // Every box shows how many envelopes are currently in it, so a cluster is seen entering
        // Submit, the rules queue, the engine and so on as it flows. Tracked independently of the
        // stacking occupancy above: moveToken decrements the box an envelope leaves and increments
        // the one it enters; clearToken releases its box when the envelope is removed. Box ids are
        // the stage keys, 'decision:<key>' and 'dlq'. The two real queues also fold in their snapshot
        // depth (baseDepth), so a queue box shows waiting + in-flight on the one badge.
        var liveCounts = {};
        var baseDepth = {};
        var tokenBoxId = new WeakMap();

        function liveBadge(boxId) {
            if (boxId === 'dlq') { return root.querySelector('[data-live-count="dlq"]'); }
            if (boxId.indexOf('decision:') === 0) {
                return root.querySelector('[data-live-count="' + boxId.slice('decision:'.length) + '"]');
            }
            var lane = lanes[boxId];
            return lane ? lane.querySelector('[data-live-count="' + boxId + '"]') : null;
        }

        function renderLiveCount(boxId) {
            var badge = liveBadge(boxId);
            if (!badge) { return; }
            var n = (liveCounts[boxId] || 0) + (baseDepth[boxId] || 0);
            badge.textContent = n;
            if (n > 0) { badge.removeAttribute('hidden'); }
            else { badge.setAttribute('hidden', ''); }
        }

        function moveToken(token, boxId) {
            var prev = tokenBoxId.get(token);
            if (prev === boxId) { return; }
            if (prev) {
                liveCounts[prev] = Math.max(0, (liveCounts[prev] || 0) - 1);
                renderLiveCount(prev);
            }
            tokenBoxId.set(token, boxId);
            liveCounts[boxId] = (liveCounts[boxId] || 0) + 1;
            renderLiveCount(boxId);
        }

        function clearToken(token) {
            var prev = tokenBoxId.get(token);
            if (!prev) { return; }
            liveCounts[prev] = Math.max(0, (liveCounts[prev] || 0) - 1);
            renderLiveCount(prev);
            tokenBoxId['delete'](token);
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
            var latency = transition.latencyMs !== undefined ? transition.latencyMs
                : transition.LatencyMs;
            var name = 'Request ' + ref + ', at ' + stage;
            if (decision) { name += ', decision ' + decision; }
            if (latency !== undefined && latency !== null && latency !== '') {
                name += ', ' + latency + 'ms';
            }
            return name;
        }

        // The decision-destination box this transition routes into, or null if it carries no decided
        // status. Matched case-insensitively against the three decision keys.
        function decisionKeyFor(transition) {
            var decision = (transition.decisionStatus || transition.DecisionStatus || '').toLowerCase();
            for (var i = 0; i < DECISION_KEYS.length; i++) {
                if (decision === DECISION_KEYS[i].toLowerCase()) { return DECISION_KEYS[i]; }
            }
            return null;
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

        // Repaint an envelope to its outcome colour AND modifier class (never colour alone), called
        // only once it lands in a status box: green approved, red rejected, yellow scrutiny, orange
        // dead-letter. Until then the envelope stays its created blue.
        function paintToken(token, decisionKey, failed) {
            var fill = failed ? DEADLETTER_COLOUR : (DECISION_COLOURS[decisionKey] || IN_FLIGHT_COLOUR);
            var flap = (decisionKey === 'Scrutiny') ? '#0b0c0c' : '#ffffff';
            var rect = token.querySelector('rect');
            var flapPath = token.querySelector('path');
            if (rect) { rect.setAttribute('fill', fill); }
            if (flapPath) { flapPath.setAttribute('stroke', flap); }
            token.classList.remove(
                'obs-board__token--autoapproved', 'obs-board__token--autorejected',
                'obs-board__token--scrutiny', 'obs-board__token--failed');
            if (failed) { token.classList.add('obs-board__token--failed'); }
            else if (decisionKey) { token.classList.add('obs-board__token--' + decisionKey.toLowerCase()); }
        }

        // The envelope element: an inline SVG envelope, keyboard-focusable and labelled, positioned
        // absolutely so it can be driven across the board by transform. EVERY envelope is created GDS
        // blue and stays blue through Submit → Rules-queue → Rules-engine; paintToken gives it its
        // outcome colour only when it lands in a status box (or the dead-letter queue), since the
        // outcome is not known in-world until the rules engine has run.
        function makeToken(transition, failed) {
            var token = document.createElement('span');
            token.className = 'obs-board__token';
            token.setAttribute('tabindex', '0');
            token.setAttribute('role', 'button');
            token.setAttribute('aria-label', accessibleName(transition) + ' — inspect this message');
            token.innerHTML =
                '<svg viewBox="0 0 24 18" width="20" height="15" aria-hidden="true" focusable="false">' +
                '<rect x="1" y="1" width="22" height="16" rx="2" fill="' + IN_FLIGHT_COLOUR + '" stroke="#0b0c0c" stroke-width="1.5"/>' +
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

            // Hover/focus surfaces the message details visually — a positioned popover carrying the
            // same reference, stage, decision and latency the aria-label already encodes — so a
            // (paused) board can be read message by message. Hidden again on leave/blur. A native
            // title is kept as a no-CSS/no-JS fallback.
            token.setAttribute('title', accessibleName(transition));
            token.addEventListener('mouseenter', function () { showDetail(token, transition); });
            token.addEventListener('mouseleave', function () { hideDetail(); });
            token.addEventListener('focus', function () { showDetail(token, transition); });
            token.addEventListener('blur', function () { hideDetail(); });

            root.appendChild(token);
            return token;
        }

        // A single shared detail popover reused across envelopes; positioned beside the hovered/
        // focused token. Reads the envelope's transition to list reference, stage, decision and
        // latency/time.
        var detailPopover = null;

        function showDetail(token, transition) {
            if (!detailPopover) {
                detailPopover = document.createElement('div');
                detailPopover.className = 'obs-board__token-detail';
                detailPopover.setAttribute('role', 'status');
                root.appendChild(detailPopover);
            }
            var ref = transition.referenceNumber || transition.ReferenceNumber || 'unknown';
            var stage = transition.stage || transition.Stage || 'pipeline';
            var decision = transition.decisionStatus || transition.DecisionStatus || '—';
            var latency = transition.latencyMs !== undefined ? transition.latencyMs
                : transition.LatencyMs;
            var when = transition.recordedAtUtc || transition.RecordedAtUtc || '';
            var rows =
                '<strong>' + ref + '</strong><br>' +
                'Stage: ' + stage + '<br>' +
                'Decision: ' + decision + '<br>' +
                'Latency: ' + ((latency !== undefined && latency !== null && latency !== '')
                    ? (latency + 'ms') : '—');
            if (when) { rows += '<br>Time: ' + when; }
            detailPopover.innerHTML = rows;

            // Anchor the popover to the token's current on-board position (read its transform-derived
            // rect) so it tracks where the envelope rests.
            var rootRect = root.getBoundingClientRect();
            var r = token.getBoundingClientRect();
            var x = (r.left - rootRect.left) + r.width + 8;
            var y = (r.top - rootRect.top);
            detailPopover.style.transform = 'translate(' + x + 'px, ' + y + 'px)';
            detailPopover.style.display = 'block';
        }

        function hideDetail() {
            if (detailPopover) { detailPopover.style.display = 'none'; }
        }

        // Place an envelope to rest AT an anchor, claiming a slot in that box's pile so multiple
        // envelopes fan out diagonally (and a "+N" overlay appears beyond the visible threshold).
        // Returns a release function the caller calls when the envelope leaves/clears the box.
        //
        // Every envelope stays VISIBLE as it travels — an in-flight message must be seen touching
        // each box on its path. The diagonal offset is clamped at STACK_MAX_VISIBLE (in stackOffset),
        // so once a box is crowded the overflow envelopes pile at the last visible slot (slightly
        // overlapping) rather than vanishing, while the "+N" overlay still conveys the true count.
        // Previously the overflow was hidden outright, which made a busy slow trickle look like
        // envelopes teleported from Submit to a decision box, skipping the rules queue and engine.
        function restAt(token, anchorId, anchor) {
            var index = enterAnchor(anchorId, anchor);
            token.style.visibility = '';
            var off = stackOffset(index);
            placeAt(token, { x: anchor.x + off.x, y: anchor.y + off.y });
            var released = false;
            return function release() {
                if (released) { return; }
                released = true;
                leaveAnchor(anchorId, anchor);
            };
        }

        // Walk an envelope from Submit along the stage path to its destination, pausing at each box.
        // A failed message stops at Rules-engine then parks (terminal) at the dead-letter box; a
        // decided message routes through its decision box (Auto-approved / Auto-rejected / Scrutiny)
        // then continues to the Zendesk ticket. Hops are scheduled through pausableTimeout so the
        // Pause control freezes every envelope mid-flight. Under reduced motion the envelope is
        // placed once at its resting box with no traversal, then removed.
        function flyEnvelope(token, destKey, failed, transition) {
            var decisionKey = failed ? null : decisionKeyFor(transition || {});

            if (reduce) {
                var restAnchor, restId;
                if (failed) { restAnchor = dlqAnchor(); restId = 'dlq'; }
                else if (decisionKey) { restAnchor = decisionAnchor(decisionKey); restId = 'decision:' + decisionKey; }
                else { restAnchor = anchorFor(destKey); restId = 'stage:' + destKey; }
                var releaseR = restAt(token, restId, restAnchor);
                moveToken(token, failed ? 'dlq' : (decisionKey ? 'decision:' + decisionKey : destKey));
                // No traversal under reduced motion: it lands at its resting box, so colour it now.
                if (failed || decisionKey) { paintToken(token, decisionKey, failed); }
                if (failed && dlqMarker) { dlqMarker.setAttribute('data-obs-dlq-active', 'true'); }
                if (decisionKey) { setDecisionActive(decisionKey, true); }
                window.setTimeout(function () {
                    releaseR();
                    clearToken(token);
                    if (decisionKey && (occupancy['decision:' + decisionKey] || 0) <= 0) {
                        setDecisionActive(decisionKey, false);
                    }
                    if (failed) { clearDlqIfEmpty(); }
                    if (token.parentNode) { token.parentNode.removeChild(token); }
                }, 0);
                return;
            }

            // Build the path of stage keys to walk. We always enter at submit; for a decided message
            // the lane portion runs to rules-engine (then we hop to the decision box and on to
            // ticket); for a failure we run to rules-engine and park at the DLQ; a plain (no-decision)
            // message runs straight to its reported stage.
            var destIdx = STAGE_ORDER.indexOf(destKey);
            if (destIdx < 0) { destIdx = STAGE_ORDER.length - 1; }
            var engineIdx = STAGE_ORDER.indexOf('rules-engine');
            var path = [];
            var stopIdx;
            if (failed) { stopIdx = Math.min(engineIdx, destIdx); }
            else if (decisionKey) { stopIdx = engineIdx; }
            else { stopIdx = destIdx; }
            for (var i = 0; i <= stopIdx; i++) { path.push(STAGE_ORDER[i]); }

            placeAt(token, anchorFor('submit'));
            moveToken(token, 'submit');
            // Stack at every lane stage too (not just the terminal boxes), so a burst piles up
            // diagonally and shows the "+N" overlay as it passes through Submit, the queues and the
            // engine. laneRelease frees the box this envelope is resting in before it hops to the next.
            var laneRelease = restAt(token, 'stage:submit', anchorFor('submit'));
            var hop = 0;
            window.requestAnimationFrame(function () {
                function next() {
                    if (laneRelease) { laneRelease(); laneRelease = null; }
                    hop += 1;
                    if (hop < path.length) {
                        moveToken(token, path[hop]);
                        laneRelease = restAt(token, 'stage:' + path[hop], anchorFor(path[hop]));
                        pausableTimeout(next, dwellFor(path[hop], moveSpeed));
                    } else if (failed) {
                        // Terminal at the dead-letter box; reveal it and rest there. The envelope
                        // travelled blue to here and now takes the dead-letter orange.
                        if (dlqMarker) { dlqMarker.setAttribute('data-obs-dlq-active', 'true'); }
                        var releaseDlq = restAt(token, 'dlq', dlqAnchor());
                        moveToken(token, 'dlq');
                        paintToken(token, null, true);
                        pausableTimeout(function () {
                            releaseDlq();
                            clearToken(token);
                            clearDlqIfEmpty(); // don't leave the DLQ box lit after the message leaves
                            if (token.parentNode) { token.parentNode.removeChild(token); }
                        }, dwellFor('rules-engine', moveSpeed) * 2);
                    } else if (decisionKey) {
                        // Canonical flow: Rules engine → its decision box (Auto-approved /
                        // Auto-rejected / Scrutiny) → Zendesk-queue → Zendesk ticket. The engine
                        // never feeds the Zendesk queue directly; the decision routes through its
                        // status box, the status feeds the queue, the queue produces the ticket.
                        var decId = 'decision:' + decisionKey;
                        var releaseDec = restAt(token, decId, decisionAnchor(decisionKey));
                        moveToken(token, decId);
                        // Reached its status box: the outcome is now known in-world, so colour it.
                        paintToken(token, decisionKey, false);
                        setDecisionActive(decisionKey, true);
                        pausableTimeout(function () {
                            releaseDec();
                            if ((occupancy[decId] || 0) <= 0) { setDecisionActive(decisionKey, false); }
                            token.style.visibility = '';
                            // status → Zendesk-queue
                            placeAt(token, anchorFor('zendesk-queue'));
                            moveToken(token, 'zendesk-queue');
                            var releaseZq = restAt(token, 'stage:zendesk-queue', anchorFor('zendesk-queue'));
                            pausableTimeout(function () {
                                releaseZq();
                                // Zendesk-queue → Zendesk ticket
                                placeAt(token, anchorFor('ticket'));
                                moveToken(token, 'ticket');
                                var releaseTicket = restAt(token, 'stage:ticket', anchorFor('ticket'));
                                pausableTimeout(function () {
                                    releaseTicket();
                                    clearToken(token);
                                    if (token.parentNode) { token.parentNode.removeChild(token); }
                                }, dwellFor('ticket', moveSpeed) * 2);
                            }, dwellFor('zendesk-queue', moveSpeed));
                        }, dwellFor('rules-engine', moveSpeed));
                    } else {
                        // No decision reported: rest at the final reported box then clear.
                        var releaseStage = restAt(token, 'stage:' + destKey, anchorFor(destKey));
                        moveToken(token, destKey);
                        pausableTimeout(function () {
                            releaseStage();
                            clearToken(token);
                            if (token.parentNode) { token.parentNode.removeChild(token); }
                        }, dwellFor(destKey, moveSpeed) * 2);
                    }
                }
                pausableTimeout(next, dwellFor('submit', moveSpeed));
            });
        }

        // The two real queues the board folds a waiting depth into. An EMPTY queue is absent from the
        // snapshot's depths — the server groups existing QueueMessages rows, so a queue with nothing
        // waiting produces no group and no depth entry — so updateCounts must reset these to empty
        // before applying whatever the snapshot carries.
        var KNOWN_QUEUE_KEYS = ['rules-queue', 'zendesk-queue'];

        // Update the per-queue depth (colour state + the live-count baseline) from the snapshot. The
        // two real queues fold their waiting depth into the same live-count badge the animation
        // drives, so a queue box shows waiting + in-flight as one number; the other boxes have no
        // depth and show in-flight only.
        function updateCounts(depths) {
            updateDepthTile(depths); // the "current depths (all queues)" tile tracks the snapshot live

            // Reset every known queue to empty FIRST. A queue that has drained to 0 drops out of the
            // snapshot entirely (no rows ⇒ no group), so the per-depth loop below would never visit
            // it and the box would keep its last non-zero badge and orange backing-up marker forever.
            // Zeroing here, then re-applying the present depths, lets a drained queue clear correctly.
            KNOWN_QUEUE_KEYS.forEach(function (key) {
                var lane = lanes[key];
                if (lane) {
                    lane.setAttribute('data-state', stateForCount(0));
                    baseDepth[key] = 0;
                    renderLiveCount(key);
                }
            });

            (depths || []).forEach(function (d) {
                var queue = d.queueName || d.QueueName;
                var depth = (d.depth !== undefined ? d.depth : d.Depth) || 0;
                var key = queue === 'zendesk' ? 'zendesk-queue' : (queue === 'rules-engine' ? 'rules-queue' : null);
                if (!key) { return; }
                var lane = lanes[key];
                if (lane) {
                    lane.setAttribute('data-state', stateForCount(depth));
                    baseDepth[key] = depth;
                    renderLiveCount(key);
                }
            });
        }

        // The board animates ONE envelope per MESSAGE, not one per stage row. A single message emits
        // several stage rows (submitted, rules-queue, rules-engine + decision, zendesk-queue,
        // ticket), each with its own timestamp, and the snapshot re-pushes recent rows every
        // heartbeat. Spawning per row made one drive look like several envelopes "dropping in" over
        // 10-20s, one splitting to a status and another going straight to the ticket. Instead the
        // engine keys by reference: it remembers which references it has already given an envelope
        // (animatedRefs), and when a NEW reference becomes "ready" (decided, failed, or reached the
        // ticket) it flies exactly one envelope along the full path, routed by that reference's
        // resolved decision. On the FIRST snapshot it primes animatedRefs from the existing history
        // WITHOUT animating, so loading the page does not replay hours of old traffic — only new
        // references that appear after load animate. Synthetic transitions (single-step, demo
        // trickle, replay, optimistic drive feedback) pass forceAnimate and animate directly.
        var animatedRefs = {};
        var primed = false;

        // The pipeline-state matrix: one accumulated row per reference, built from the recorded
        // stage events as they arrive. Keyed by reference; seq preserves first-seen order so the
        // newest message sits at the top and existing ones are pushed down as they fill in.
        var gridRows = {};
        var gridSeq = 0;
        var gridRendered = false;

        // The set of references ticked for replay, kept outside the DOM so a tick survives the
        // matrix re-rendering on each snapshot. The current reference filter text, applied after
        // each render so newly-arrived rows obey it too.
        var selectedRefs = {};
        var gridFilterText = '';

        function timestampOf(transition) {
            var raw = transition.recordedAtUtc || transition.RecordedAtUtc;
            if (!raw) { return null; }
            var ms = Date.parse(raw);
            return isNaN(ms) ? null : ms;
        }

        function refOf(transition) {
            return transition.referenceNumber || transition.ReferenceNumber || null;
        }

        // Collapse a reference's rows into a single message state: did any row fail, what decision
        // did it resolve to (the last non-empty decisionStatus), did it reach the ticket, and which
        // row is the latest (for the time/latency shown on hover).
        function resolveMessage(rows) {
            var failed = false, decision = null, latest = rows[0], latestTs = null, latency = null;
            rows.forEach(function (t) {
                if (isFailure(t)) { failed = true; }
                var d = t.decisionStatus || t.DecisionStatus;
                if (d) { decision = d; }
                var l = (t.latencyMs !== undefined ? t.latencyMs : t.LatencyMs);
                if (l !== undefined && l !== null && l !== '') { latency = l; }
                var ts = timestampOf(t);
                if (ts !== null && (latestTs === null || ts >= latestTs)) { latestTs = ts; latest = t; }
            });
            var reachedTicket = rows.some(function (t) { return stageKeyFor(t) === 'ticket'; });
            return { failed: failed, decision: decision, reachedTicket: reachedTicket, latest: latest, latency: latency };
        }

        // --- Pipeline-state matrix: accumulate per-message stage timings and render the table ---

        var DECISION_LABELS = {
            'AutoApproved': 'Auto-approved',
            'AutoRejected': 'Auto-rejected',
            'Scrutiny': 'Scrutiny',
        };

        function esc(s) {
            return String(s == null ? '' : s)
                .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        // HH:MM:SS.fff (UTC) from an epoch-ms timestamp, or a dash when the stage hasn't been reached.
        // Millisecond precision so sub-second stages are distinguishable rather than all "a second or two".
        function hms(ts) {
            if (!ts) { return '—'; }
            return new Date(ts).toISOString().substr(11, 12);
        }

        // A friendly "waited Nms / N.NNNs" between two stage timestamps, or a dash if either is missing.
        // Seconds keep millisecond resolution (3 dp) so two waits that round to the same second still read apart.
        function waited(fromTs, toTs) {
            if (!fromTs || !toTs || toTs < fromTs) { return '—'; }
            var ms = toTs - fromTs;
            return ms >= 1000 ? ('waited ' + (ms / 1000).toFixed(3) + 's') : ('waited ' + ms + 'ms');
        }

        // Fold one stage event into its message's matrix row. The recorded stages are Submitted,
        // RulesEvaluated (carries the decision + processing latency) and TicketCreated; they map onto
        // the matrix columns, from which the queue waits are derived.
        function ingestEvent(t) {
            var ref = refOf(t);
            if (!ref) { return; }
            var row = gridRows[ref];
            if (!row) {
                row = gridRows[ref] = { ref: ref, submit: null, engine: null, ticket: null,
                    decision: null, latency: null, failed: false, seq: gridSeq++ };
            }
            if (isFailure(t)) { row.failed = true; }
            var d = t.decisionStatus || t.DecisionStatus;
            if (d) { row.decision = d; }
            var l = (t.latencyMs !== undefined ? t.latencyMs : t.LatencyMs);
            if (l !== undefined && l !== null && l !== '') { row.latency = l; }
            var ts = timestampOf(t);
            var stage = (t.stage || t.Stage || '').toLowerCase();
            if (stage.indexOf('submit') >= 0) { if (ts) { row.submit = ts; } }
            else if (stage.indexOf('ticket') >= 0) { if (ts) { row.ticket = ts; } }
            else if (stage.indexOf('rules') >= 0 || stage.indexOf('evaluat') >= 0) { if (ts) { row.engine = ts; } }
        }

        // One matrix stage cell: the time the submission reached the stage on top, the time spent
        // there beneath. A blank duration ('—') renders just the time. Mirrors the server-rendered
        // cell markup in _Board.cshtml so live rows and seeded rows look identical.
        function stageCell(timeTs, dur) {
            var time = '<span class="obs-cell-time">' + esc(hms(timeTs)) + '</span>';
            if (!dur || dur === '—') {
                return '<td class="govuk-table__cell">' + time + '</td>';
            }
            return '<td class="govuk-table__cell">' + time
                + '<span class="obs-cell-dur">' + esc(dur) + '</span></td>';
        }

        // The leading replay-select cell: a checkbox carrying the reference, its checked state read
        // from the selection set so it survives a re-render.
        function pickCell(ref) {
            var checked = selectedRefs[ref] ? ' checked' : '';
            return '<td class="govuk-table__cell">'
                + '<div class="govuk-checkboxes__item govuk-checkboxes--small govuk-!-margin-bottom-0">'
                + '<input class="govuk-checkboxes__input" type="checkbox" data-obs-grid-pick value="'
                + esc(ref) + '"' + checked + ' aria-label="Select ' + esc(ref) + ' for replay" />'
                + '</div></td>';
        }

        function gridRowHtml(r) {
            var refLink = '<a class="govuk-link" target="_blank" rel="noopener" href="/admin/observability/journey/'
                + encodeURIComponent(r.ref) + '">' + esc(r.ref) + '</a>';
            var status = r.failed ? 'Dead-letter'
                : (r.decision ? (DECISION_LABELS[r.decision] || r.decision) : '—');
            // The rules-engine cell's duration is its processing latency; the queue cells' is the
            // wait (submit→engine, decision→ticket). Each cell also shows the time it reached the
            // stage, so every column answers "when did it arrive and how long did it spend".
            var engineDur = r.latency != null ? (Math.round(r.latency) + 'ms') : '—';
            var rulesQueueDur = r.submit ? (r.engine ? waited(r.submit, r.engine) : 'in queue') : '—';
            var zendeskQueueDur = (r.engine && !r.failed) ? (r.ticket ? waited(r.engine, r.ticket) : 'in queue') : '—';
            return '<tr class="govuk-table__row" data-obs-grid-row="' + esc(r.ref) + '">'
                + pickCell(r.ref)
                + '<td class="govuk-table__cell">' + refLink + '</td>'
                + '<td class="govuk-table__cell"><span class="obs-cell-time">' + esc(hms(r.submit)) + '</span></td>'
                + stageCell(r.submit, rulesQueueDur)
                + stageCell(r.engine, engineDur)
                + '<td class="govuk-table__cell">' + esc(status) + '</td>'
                + stageCell(r.failed ? null : r.engine, zendeskQueueDur)
                + '<td class="govuk-table__cell"><span class="obs-cell-time">' + esc(hms(r.ticket)) + '</span></td>'
                + '</tr>';
        }

        function renderGrid() {
            if (!gridBody) { return; }
            var rows = Object.keys(gridRows).map(function (k) { return gridRows[k]; })
                .sort(function (a, b) { return b.seq - a.seq; }) // newest first-seen at the top
                .slice(0, GRID_MAX_ROWS);
            if (rows.length === 0) {
                gridBody.innerHTML = '<tr class="govuk-table__row obs-board__grid-empty">'
                    + '<td class="govuk-table__cell" colspan="8">No submissions yet. '
                    + 'Drive some traffic to see them flow.</td></tr>';
            } else {
                gridBody.innerHTML = rows.map(gridRowHtml).join('');
            }
            applyGridFilter();
            gridRendered = true;
        }

        // Hide matrix rows whose reference does not contain the filter text (case-insensitive);
        // re-applied after every render so live-arriving rows obey the active filter.
        function applyGridFilter() {
            if (!gridBody) { return; }
            var f = (gridFilterText || '').toLowerCase();
            var rowEls = gridBody.querySelectorAll('[data-obs-grid-row]');
            for (var i = 0; i < rowEls.length; i++) {
                var ref = (rowEls[i].getAttribute('data-obs-grid-row') || '').toLowerCase();
                rowEls[i].style.display = (!f || ref.indexOf(f) >= 0) ? '' : 'none';
            }
        }

        // The references ticked for replay (from the selection set, not the DOM, so a tick survives
        // re-renders). Exposed so the matrix Play-selected control can read them.
        function selectedReferences() {
            return Object.keys(selectedRefs).filter(function (k) { return selectedRefs[k]; });
        }

        function setSelected(ref, on) {
            if (on) { selectedRefs[ref] = true; } else { delete selectedRefs[ref]; }
        }

        function setGridFilter(text) {
            gridFilterText = text || '';
            applyGridFilter();
        }

        // Fold a whole snapshot's transitions into the matrix and re-render once.
        function ingestAndRender(list) {
            (list || []).forEach(ingestEvent);
            renderGrid();
        }

        // Seed the live grid from the server-rendered recent submissions (a JSON blob beside the
        // table) so the matrix is populated on load and the engine's first re-render does not wipe
        // that history. Each seeded reference also primes animatedRefs so loading the page does not
        // replay historical submissions when the first SSE snapshot arrives. Iterated oldest-first so
        // the newest server row takes the highest seq and sorts to the top.
        function seedGridFromServer() {
            var el = document.querySelector('[data-obs-grid-seed]');
            if (!el) { return; }
            var seed;
            try { seed = JSON.parse(el.textContent || '[]'); } catch (e) { return; }
            if (!seed || !seed.length) { return; }
            for (var i = seed.length - 1; i >= 0; i--) {
                var s = seed[i];
                if (!s || !s.ref) { continue; }
                var row = gridRows[s.ref];
                if (!row) {
                    row = gridRows[s.ref] = { ref: s.ref, submit: null, engine: null, ticket: null,
                        decision: null, latency: null, failed: false, seq: gridSeq++ };
                }
                if (s.submit) { row.submit = Date.parse(s.submit); }
                if (s.rules) { row.engine = Date.parse(s.rules); }
                if (s.ticket) { row.ticket = Date.parse(s.ticket); }
                if (s.decision) { row.decision = s.decision; }
                if (s.latencyMs !== null && s.latencyMs !== undefined) { row.latency = s.latencyMs; }
                if (s.deadLettered) { row.failed = true; }
                animatedRefs[s.ref] = true;
            }
            renderGrid();
        }

        function updateTransitions(transitions, forceAnimate) {
            var list = transitions || [];

            // Synthetic re-presentation (replay, trickle, single-step): animate each row directly.
            // These do not feed the matrix — it is the record of real recorded traffic (drives add a
            // row optimistically via presentDrive, completed by the real SSE rows that follow).
            if (forceAnimate) {
                list.forEach(function (t) {
                    var failed = isFailure(t);
                    var token = makeToken(t, failed);
                    flyEnvelope(token, stageKeyFor(t), failed, t);
                });
                return;
            }

            // The matrix reflects every recent recorded event, on load and live.
            ingestAndRender(list);

            // Group the live rows by reference so each message is considered once for animation.
            var groups = {};
            var order = [];
            list.forEach(function (t) {
                var ref = refOf(t);
                if (!ref) { return; }
                if (!groups[ref]) { groups[ref] = []; order.push(ref); }
                groups[ref].push(t);
            });

            // First snapshot: establish a baseline of existing references without animating, so the
            // page does not replay history on load (the matrix still shows that history).
            if (!primed) {
                order.forEach(function (ref) { animatedRefs[ref] = true; });
                primed = true;
                return;
            }

            order.forEach(function (ref) {
                if (animatedRefs[ref]) { return; }
                var state = resolveMessage(groups[ref]);
                if (!(state.failed || state.decision || state.reachedTicket)) { return; } // still in flight
                animatedRefs[ref] = true;
                if (!state.failed) { bumpProcessed(); } // a decided message processed through to a ticket
                bumpDecision(state.decision, state.failed); // tick the matching 24h decision counter
                var synthetic = {
                    referenceNumber: ref,
                    decisionStatus: state.decision,
                    failed: state.failed,
                    latencyMs: state.latency,
                    recordedAtUtc: state.latest && (state.latest.recordedAtUtc || state.latest.RecordedAtUtc),
                };
                var token = makeToken(synthetic, state.failed);
                flyEnvelope(token, 'ticket', state.failed, synthetic);
            });
        }

        // Optimistic drive feedback: when a drive is triggered over AJAX we know the reference and
        // the intended outcome, so we can show one correctly-routed envelope immediately rather than
        // waiting up to a heartbeat for the SSE rows. The reference is marked animated so the rows
        // that later arrive on the stream do not spawn a second envelope for the same message.
        function presentDrive(reference, outcome) {
            if (!reference) { return; }
            var o = (outcome || '').toLowerCase();
            var failed = (o === 'failed' || o === 'inject-failure' || o === 'seed-dlq');
            var decision = o === 'approved' ? 'AutoApproved'
                : o === 'rejected' ? 'AutoRejected'
                : o === 'scrutiny' ? 'Scrutiny' : null;
            animatedRefs[reference] = true;
            if (!failed) { bumpProcessed(); } // an approved/rejected/scrutiny drive reaches a ticket
            bumpDecision(decision, failed); // tick the matching 24h decision counter
            // Optimistically add a matrix row so the driven message shows immediately; the real
            // recorded events fill in the queue waits and ticket time as they arrive on the stream.
            ingestEvent({ referenceNumber: reference, stage: 'Submitted',
                recordedAtUtc: new Date().toISOString(), decisionStatus: decision, failed: failed });
            renderGrid();
            var synthetic = { referenceNumber: reference, decisionStatus: decision, failed: failed };
            var token = makeToken(synthetic, failed);
            flyEnvelope(token, 'ticket', failed, synthetic);
        }

        // Replay counts per root reference, so re-replaying a submission yields -R1, -R2, ...
        var replayCounts = {};

        function nowIso() { return new Date().toISOString(); }

        // Replay ONE recorded submission as a SINGLE envelope along the full path — not one envelope
        // per stage row, which was the bug: a Submitted row flew to Submit and vanished, and a
        // TicketCreated row crossed the top boxes with no status. Group the reference's recorded
        // events, resolve its decision/outcome, mint a <root>-R{n} copy, add it to the Recent
        // submissions matrix and fly one correctly-routed envelope. The matrix row fills in
        // progressively (submit → decision → ticket) so it is seen completing as the envelope reaches
        // the Zendesk ticket. Visual only: no real queue work, metric rows or tickets are created.
        function replaySubmission(events) {
            var list = events || [];
            if (!list.length) { return; }
            var rootRef = refOf(list[0]) || 'unknown';
            var state = resolveMessage(list);
            var n = (replayCounts[rootRef] = (replayCounts[rootRef] || 0) + 1);
            var replayRef = rootRef + '-R' + n;
            animatedRefs[replayRef] = true;

            // The reference's recorded per-stage times, carried onto the replay copy's row so it
            // shows the original submission's timings.
            var times = {};
            list.forEach(function (e) {
                var stage = (e.stage || e.Stage || '').toLowerCase();
                var when = e.recordedAtUtc || e.RecordedAtUtc;
                if (!when) { return; }
                if (stage.indexOf('submit') >= 0) { times.submit = when; }
                else if (stage.indexOf('ticket') >= 0) { times.ticket = when; }
                else if (stage.indexOf('rules') >= 0 || stage.indexOf('evaluat') >= 0) { times.engine = when; }
            });

            // One correctly-routed envelope for the whole submission.
            var synthetic = { referenceNumber: replayRef, decisionStatus: state.decision,
                failed: state.failed, latencyMs: state.latency };
            var token = makeToken(synthetic, state.failed);
            flyEnvelope(token, 'ticket', state.failed, synthetic);

            // Fill the matrix row in step with the flight: submit now, the engine decision shortly
            // after, then the dead-letter or the ticket.
            ingestEvent({ referenceNumber: replayRef, stage: 'Submitted', recordedAtUtc: times.submit || nowIso() });
            renderGrid();
            var unit = STAGE_DWELL_MS * (moveSpeed || 1);
            if (state.failed) {
                pausableTimeout(function () {
                    ingestEvent({ referenceNumber: replayRef, stage: 'DeadLettered',
                        recordedAtUtc: times.engine || nowIso(), failed: true });
                    renderGrid();
                }, unit * 2);
            } else {
                pausableTimeout(function () {
                    ingestEvent({ referenceNumber: replayRef, stage: 'RulesEvaluated',
                        recordedAtUtc: times.engine || nowIso(),
                        decisionStatus: state.decision, latencyMs: state.latency });
                    renderGrid();
                }, unit * 2);
                if (times.ticket) {
                    pausableTimeout(function () {
                        ingestEvent({ referenceNumber: replayRef, stage: 'TicketCreated', recordedAtUtc: times.ticket });
                        renderGrid();
                    }, unit * 4);
                }
            }
        }

        function onSnapshot(snapshot) {
            if (reconnectNotice) { reconnectNotice.hidden = true; }
            if (!snapshot) { return; }
            updateCounts(snapshot.depths || snapshot.Depths);
            // While paused, freeze the board completely: don't spawn new envelopes (from the live
            // feed, the demo trickle or replay) so the frozen picture stays readable. The watermark
            // is left untouched so resuming doesn't replay the skipped window as a backlog.
            if (paused) { return; }
            updateTransitions(
                snapshot.recentTransitions || snapshot.RecentTransitions,
                !!snapshot.forceAnimate);
        }

        function onError() {
            if (reconnectNotice) { reconnectNotice.hidden = false; }
        }

        // The clock seam. slow-mo scales it, single-step holds it right down so a watcher can follow
        // one message a step at a time, demo-mode trickles synthetic envelopes through it, and replay
        // drives it from a scrubber — all over this one renderer. moveSpeed scales how long an
        // envelope dwells at each box; it is the product of the slow-motion and single-step factors
        // so the two checkboxes compose. demoTimer is the auto-trickle interval handle.
        var moveSpeed = 1;
        var demoCount = 1;   // messages per trickle burst (the "Messages" slider)
        var demoSpeed = 50;  // 0..100, the "Speed" slider: 0 = slow single-step crawl, 100 = racing
        var demoTimer = null;

        // The Speed slider replaces the old slow-motion + single-step checkboxes with one control:
        // 50 (the default) is normal speed so the live board is unaffected; dragging left slows the
        // dwell clock right down (≈8x, the old single-step crawl), dragging right races it (≈0.4x).
        function applyDemoSpeed(v) {
            demoSpeed = Math.max(0, Math.min(100, v | 0));
            moveSpeed = demoSpeed >= 50
                ? 1 + ((0.4 - 1) * ((demoSpeed - 50) / 50))   // 50→1 … 100→0.4 (faster)
                : 1 + ((8 - 1) * ((50 - demoSpeed) / 50));     // 50→1 … 0→8 (slower)
        }

        // The gap between trickle bursts tracks the same slider, clamped so it never stalls or floods.
        function demoIntervalMs() {
            return Math.min(3000, Math.max(250, 1200 * moveSpeed));
        }

        // Pause freezes ALL envelope motion (and the replay/trickle): each in-flight hop is scheduled
        // through a pausable timer, so flipping paused holds every envelope where it sits until
        // resumed. It is not dev-gated — it just stops animation — so it is always available.
        var paused = false;
        var pausableTimers = [];   // [{ fn, due }] pending hops, frozen while paused
        var pauseSubscribers = []; // notified on pause/resume (e.g. to halt the replay clock)

        // A pausable setTimeout: while paused the callback is held and its remaining time preserved,
        // resuming reschedules it. Returns a handle that supersedes window.setTimeout for hops.
        function pausableTimeout(fn, delay) {
            var entry = { fn: fn, due: Date.now() + delay, remaining: delay, handle: null };
            function arm(ms) {
                entry.handle = window.setTimeout(function () {
                    var idx = pausableTimers.indexOf(entry);
                    if (idx >= 0) { pausableTimers.splice(idx, 1); }
                    fn();
                }, ms);
            }
            entry.arm = arm;
            pausableTimers.push(entry);
            if (!paused) { arm(delay); }
            return entry;
        }

        function togglePause(force) {
            paused = (force === undefined) ? !paused : !!force;
            if (paused) {
                // Freeze: cancel the live OS timers, preserving how long each had left to run.
                pausableTimers.forEach(function (e) {
                    if (e.handle) { window.clearTimeout(e.handle); e.handle = null; }
                    e.remaining = Math.max(0, e.due - Date.now());
                });
            } else {
                // Resume: re-arm each pending hop for its preserved remaining time.
                pausableTimers.slice().forEach(function (e) {
                    e.due = Date.now() + e.remaining;
                    e.arm(e.remaining);
                });
            }
            pauseSubscribers.forEach(function (cb) { try { cb(paused); } catch (err) { /* noop */ } });
            return paused;
        }

        function isPaused() { return paused; }
        function onPauseChange(cb) { pauseSubscribers.push(cb); }

        // The Speed slider (0..100). Sets the dwell clock; if the trickle is running, re-arm it at the
        // new pace so a speed change takes effect immediately.
        function setDemoSpeed(v) {
            applyDemoSpeed(v);
            if (demoTimer) { startDemo(); }
        }

        // The Messages slider: how many synthetic envelopes each trickle burst sends.
        function setDemoCount(n) {
            demoCount = Math.max(1, n | 0);
        }

        // A random demo outcome so trickle/single-step exercise the whole pipeline — approved,
        // rejected, scrutiny and the dead-letter queue — rather than only the happy ticket path, so
        // the board (and the live decision mix) shows a realistic spread.
        function randomOutcome(prefix) {
            var roll = Math.floor(Math.random() * 100);
            var ev = { referenceNumber: prefix + '-' + Date.now() + '-' + roll, stage: 'ticket' };
            if (roll < 15) { ev.stage = 'rules-engine'; ev.failed = true; }      // ~15% dead-letter
            else if (roll < 40) { ev.decisionStatus = 'AutoApproved'; }
            else if (roll < 65) { ev.decisionStatus = 'AutoRejected'; }
            else { ev.decisionStatus = 'Scrutiny'; }
            return ev;
        }

        // Single-step: send one synthetic envelope all the way through the pipeline (with a real
        // decision so it routes through a status box) so a presenter can walk an audience
        // Submit → status → Zendesk ticket. forceAnimate bypasses the per-message dedupe.
        function singleStep() {
            var ev = randomOutcome('STEP');
            if (!ev.failed) { bumpProcessed(); }
            bumpDecision(ev.decisionStatus, ev.failed);
            onSnapshot({ recentTransitions: [ev], depths: [], forceAnimate: true });
        }

        // Inject a failing envelope: it walks to Rules-engine then diverts to the dead-letter marker.
        function injectFailure() {
            onSnapshot({
                recentTransitions: [{ referenceNumber: 'FAIL-' + Date.now(), stage: 'rules-engine', failed: true }],
                depths: [],
                forceAnimate: true,
            });
        }

        // One trickle burst: send demoCount synthetic envelopes, spread across all the outcome types
        // so the board (and the live decision mix) shows a realistic mix, not only the happy path.
        // The envelopes are STAGGERED by a small randomised delay each, so a burst trickles in rather
        // than dropping onto the board as one synchronised lump.
        function spawnOne() {
            var ev = randomOutcome('DEMO');
            if (!ev.failed) { bumpProcessed(); }
            bumpDecision(ev.decisionStatus, ev.failed);
            onSnapshot({ recentTransitions: [ev], depths: [], forceAnimate: true });
        }
        function spawnDemoBurst() {
            for (var i = 0; i < demoCount; i++) {
                window.setTimeout(spawnOne, i * (50 + Math.random() * 200));
            }
        }

        // Demo-mode auto-trickle: emit a burst of demoCount envelopes on an interval set by the Speed
        // slider, so the board stays alive during a demo even with no real traffic. Re-arming uses the
        // current interval, so a speed change takes effect immediately.
        function startDemo() {
            if (demoTimer) { window.clearInterval(demoTimer); }
            spawnDemoBurst();
            demoTimer = window.setInterval(spawnDemoBurst, demoIntervalMs());
        }

        function toggleDemoMode() {
            if (demoTimer) {
                window.clearInterval(demoTimer);
                demoTimer = null;
                return false;
            }
            startDemo();
            return true;
        }

        // Keep the selection set in step with the matrix checkboxes (delegated, so it works across
        // re-renders). The change fires on the live checkbox the user toggled.
        if (gridBody) {
            gridBody.addEventListener('change', function (e) {
                var t = e.target;
                if (t && t.getAttribute && t.getAttribute('data-obs-grid-pick') !== null) {
                    setSelected(t.value, t.checked);
                }
            });
        }

        // Populate the live grid from the server-rendered history before any feed connects.
        seedGridFromServer();

        return {
            onSnapshot: onSnapshot,
            onError: onError,
            selectedReferences: selectedReferences,
            setGridFilter: setGridFilter,
            setDemoSpeed: setDemoSpeed,
            setDemoCount: setDemoCount,
            singleStep: singleStep,
            injectFailure: injectFailure,
            toggleDemoMode: toggleDemoMode,
            togglePause: togglePause,
            isPaused: isPaused,
            onPauseChange: onPauseChange,
            presentDrive: presentDrive,
            replaySubmission: replaySubmission,
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

    // Group a flat list of recorded stage events into one bucket per reference, preserving
    // first-seen order (the events arrive time-ordered, so this orders messages by first activity).
    function groupByReference(events) {
        var byRef = {};
        var order = [];
        (events || []).forEach(function (e) {
            var ref = e.referenceNumber || e.ReferenceNumber;
            if (!ref) { return; }
            if (!byRef[ref]) { byRef[ref] = []; order.push(ref); }
            byRef[ref].push(e);
        });
        return order.map(function (ref) { return byRef[ref]; });
    }

    // The recorded/replay feed. It fetches the recorded events for a window from the always-on
    // replay endpoint and groups them into MESSAGES (one per reference), then plays one message at a
    // time through the engine's per-submission replay — so each scrubber step is a whole submission
    // (a <root>-R{n} copy that flows the full path and fills the matrix), never a single stage row.
    // Exposes load(from, to), seek(index) and the message count so a scrubber UI can drive it.
    function recordedFeed(replayUrl) {
        var messages = [];
        var handler = null;
        return {
            onMessage: function (cb) { handler = cb; },
            load: function (query) {
                var url = replayUrl + '?' + query;
                return fetch(url, { credentials: 'same-origin' })
                    .then(function (r) { return r.ok ? r.json() : []; })
                    .then(function (data) { messages = groupByReference(data || []); return messages.length; })
                    .catch(function () { messages = []; return 0; });
            },
            count: function () { return messages.length; },
            // Replay the message at the scrubber position as one envelope along the full path.
            seek: function (index) {
                if (!handler) { return; }
                var m = messages[index];
                if (!m) { return; }
                handler(m);
            },
        };
    }

    // The single entry point. Given a board root and a feed, wire the feed to the renderer.
    function start(root, feed) {
        var engine = BoardEngine(root);
        feed.subscribe(engine.onSnapshot, engine.onError);
        return engine;
    }

    // Wire the replay scrubber and the dev-only control group (slow-mo / single-step / demo-mode /
    // inject-failure). These controls now live in the dashboard's collapsible Demo panel, OUTSIDE
    // the board root, so they are looked up at the document level rather than under the board; the
    // server-side Razor conditional only renders them when DemoToolsEnabled. Single step and demo
    // trickle are checkboxes (like slow motion). Each control is keyboard-operable; the scrubber
    // announces its position via aria-valuetext and a live region. The board root still carries the
    // replay endpoint URL.
    function wireControls(root, engine) {
        // Controls live in the Demo panel (a sibling of the board), so resolve them from the
        // document. Falling back to the board root keeps any future in-board control working too.
        var controls = document;

        // --- Pause/Resume (always-available, on the board itself, NOT the dev-only Demo panel) ---
        // Resolved from the board root so it is reachable without opening Demo. Pausing freezes every
        // envelope's motion and also halts the replay auto-play clock via the engine's pause event.
        var pauseBtn = root.querySelector('[data-obs-pause]')
            || controls.querySelector('[data-obs-pause]');
        if (pauseBtn) {
            pauseBtn.addEventListener('click', function () {
                var nowPaused = engine.togglePause();
                pauseBtn.setAttribute('aria-pressed', nowPaused ? 'true' : 'false');
                pauseBtn.textContent = nowPaused ? 'Resume' : 'Pause';
            });
        }

        // --- Replay scrubber ---
        var replayUrl = root.getAttribute('data-replay-url') || '/admin/observability/replay';
        var scrubber = controls.querySelector('[data-obs-scrubber]');
        var playBtn = controls.querySelector('[data-obs-replay-play]');
        var status = controls.querySelector('[data-obs-replay-status]');
        var windowSelect = controls.querySelector('[data-obs-replay-window]');
        if (scrubber) {
            var feed = recordedFeed(replayUrl);
            feed.onMessage(engine.replaySubmission);
            var playing = false;
            var playTimer = null;

            function announce(index, total) {
                var text = total > 0
                    ? ('Submission ' + (index + 1) + ' of ' + total)
                    : 'No recorded submissions in this window';
                scrubber.setAttribute('aria-valuetext', text);
                if (status) { status.textContent = text; }
            }

            // The replay window. The select uses the same calendar vocabulary as the chart/seed
            // dropdowns (Today, Yesterday, This week, …), defaulting to Today; the window itself is
            // resolved server-side by the replay endpoint from the range key, so the calendar maths
            // lives in one place. Custom carries the from/to inputs. A missing select falls back to
            // Today.
            function replayQuery() {
                var val = (windowSelect && windowSelect.value) || 'today';
                if (val.toLowerCase() === 'custom') {
                    var qs = 'range=custom';
                    var f = controls.querySelector('#obs-replay-from');
                    var t = controls.querySelector('#obs-replay-to');
                    if (f && f.value) { qs += '&from=' + encodeURIComponent(f.value); }
                    if (t && t.value) { qs += '&to=' + encodeURIComponent(t.value); }
                    return qs;
                }
                return 'range=' + encodeURIComponent(val);
            }

            function loadWindow() {
                return feed.load(replayQuery()).then(function (count) {
                    scrubber.max = count > 0 ? (count - 1) : 0;
                    scrubber.value = 0;
                    announce(0, count);
                });
            }

            // Changing the window (or the custom from/to) reloads the replay and resets the scrubber.
            if (windowSelect) {
                windowSelect.addEventListener('change', function () { loadWindow(); });
            }
            ['#obs-replay-from', '#obs-replay-to'].forEach(function (sel) {
                var el = controls.querySelector(sel);
                if (el) { el.addEventListener('change', function () { loadWindow(); }); }
            });

            scrubber.addEventListener('input', function () {
                var idx = parseInt(scrubber.value, 10) || 0;
                feed.seek(idx); // animates an envelope through the stages for this recorded event
                announce(idx, feed.count());
            });

            // The replay Speed slider (0..100, default 50): how fast auto-play steps through events.
            // Slow end ≈ 2s/step, default ≈ 800ms, fast end ≈ 150ms. A change mid-play re-arms.
            var speedSlider = controls.querySelector('[data-obs-replay-speed]');
            function replayIntervalMs() {
                var v = speedSlider ? (parseInt(speedSlider.value, 10) || 50) : 50;
                return Math.round(2000 + ((150 - 2000) * (v / 100)));
            }
            function armPlay() {
                if (playTimer) { window.clearInterval(playTimer); }
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
                }, replayIntervalMs());
            }
            if (speedSlider) {
                speedSlider.addEventListener('input', function () { if (playing) { armPlay(); } });
            }

            if (playBtn) {
                playBtn.addEventListener('click', function () {
                    playing = !playing;
                    playBtn.setAttribute('aria-pressed', playing ? 'true' : 'false');
                    playBtn.textContent = playing ? 'Pause' : 'Play';
                    if (playing) {
                        armPlay();
                    } else if (playTimer) {
                        window.clearInterval(playTimer);
                    }
                });
            }

            loadWindow();
        }

        // --- Dev-only controls (only in the DOM when DemoToolsEnabled rendered them) ---
        // Demo trickle is a checkbox: ticking it starts the auto-trickle, unticking it stops. Its pace
        // and volume come from the two sliders below (replacing the old slow-motion + single-step
        // checkboxes): Speed (slow single-step crawl ↔ racing) and Messages (per burst).
        var demoCountSlider = controls.querySelector('[data-obs-demo-count]');
        if (demoCountSlider) {
            var applyCount = function () { engine.setDemoCount(parseInt(demoCountSlider.value, 10) || 1); };
            applyCount();
            demoCountSlider.addEventListener('input', applyCount);
        }

        var demoSpeedSlider = controls.querySelector('[data-obs-demo-speed]');
        if (demoSpeedSlider) {
            var applyDemoSpeedControl = function () { engine.setDemoSpeed(parseInt(demoSpeedSlider.value, 10) || 50); };
            applyDemoSpeedControl();
            demoSpeedSlider.addEventListener('input', applyDemoSpeedControl);
        }

        var demo = controls.querySelector('[data-obs-demo]');
        if (demo) {
            demo.addEventListener('change', function () {
                var on = engine.toggleDemoMode();
                demo.checked = on;
            });
        }

        // The inject-failure trigger also drives the board so the diversion is visible even before
        // the server-side seed round-trips. The confirm-modal still posts to the seed endpoint; this
        // gives immediate on-board feedback.
        var inject = controls.querySelector('[data-obs-inject]');
        if (inject) {
            inject.addEventListener('click', function () { engine.injectFailure(); });
        }

        // --- Replay from the Recent submissions matrix ---
        // The matrix rows carry a checkbox; a reference filter narrows the visible rows and "Play
        // selected" replays the ticked references through THIS board (each a non-destructive
        // <root>-R{n} copy that flows the full path and fills the matrix). This replaces the old
        // Demo-panel picker so replay lives on the always-visible matrix. The selection is read from
        // the engine (a set kept outside the DOM), so a tick survives the matrix re-rendering.
        var gridFilter = controls.querySelector('[data-obs-grid-filter]');
        if (gridFilter) {
            gridFilter.addEventListener('input', function () { engine.setGridFilter(gridFilter.value); });
        }

        // Replay the ticked references, one whole submission after another, fetching their recorded
        // events and grouping them per message (the same per-submission replay the scrubber uses).
        function playSelectedRows(refs, playBtn) {
            var qs = refs.map(function (r) { return 'reference=' + encodeURIComponent(r); }).join('&');
            if (playBtn) { playBtn.disabled = true; }
            fetch('/admin/observability/replay/selected?' + qs, { credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : []; })
                .then(function (events) {
                    var messages = groupByReference(events || []);
                    var i = 0;
                    (function stepNext() {
                        if (i >= messages.length) { return; }
                        engine.replaySubmission(messages[i]);
                        i += 1;
                        window.setTimeout(stepNext, 900);
                    })();
                })
                .catch(function () { /* best effort */ })
                .then(function () { if (playBtn) { playBtn.disabled = false; } });
        }

        var gridPlayBtn = controls.querySelector('[data-obs-grid-play]');
        if (gridPlayBtn) {
            gridPlayBtn.addEventListener('click', function () {
                var refs = engine.selectedReferences();
                if (refs.length === 0) { return; }
                playSelectedRows(refs, gridPlayBtn);
            });
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
        // Optimistic, correctly-routed single-envelope feedback for an AJAX drive: the host page
        // passes the new reference and the intended outcome, and the board flies one envelope at
        // once (deduped against the SSE rows that follow). Replaces the old refresh() that injected
        // a fake extra envelope on every drive.
        present: function (reference, outcome) { if (sharedEngine) { sharedEngine.presentDrive(reference, outcome); } },
        refresh: function () { /* no-op retained for back-compat; SSE + present() drive the board now */ }
    };
})();
