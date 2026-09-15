// Instant search for the content-page search widget.
//
// The widget always renders a working GET form. This script layers a suggestion menu over it
// when the author has ticked instant search, and does nothing at all otherwise — so a visitor
// without JavaScript, or one whose fetch fails, still has the search box and button that were
// there before.
//
// Two sources behind one menu:
//   this page  - sections of the page being read, built from the heading anchors already in the
//                DOM. Choosing one moves to that section.
//   site/path  - /search/suggestions, scoped to whatever the widget is configured for. Choosing
//                one opens that page.
(function () {
    'use strict';

    var SUGGESTIONS_URL = '/search/suggestions';
    var MIN_LENGTH = 2;
    var MAX_RESULTS = 10;
    var DEBOUNCE_MS = 200;
    var ASSISTIVE_HINT =
        'When results are available use up and down arrows to review and enter to select. ' +
        'Touch device users, explore by touch or with swipe gestures.';

    // Subtrees whose text is not page content: the widget's own chrome, and any nav — the page
    // navigation widget repeats every heading as a link, which would otherwise make each
    // heading look like a body match on every other heading.
    var NON_CONTENT = 'form.cypmd-search, nav, script, style, template';

    var REPORT_URL = '/search/instant-analytics';

    // Reporting an instant search
    // ---------------------------
    // A typeahead fires on every keystroke, and almost none of those keystrokes are an event
    // anyone wants to read. What matters is the query a person settled on: what they were
    // shown for it, and whether they took any of it. So nothing is sent while they are still
    // typing. One report goes out when the query is settled, which is any of:
    //
    //   * they chose something from the menu;
    //   * they changed the query to something that is not a continuation of it — which is a
    //     person looking at the results and deciding none of them will do;
    //   * they left the box, hid the tab, or closed the page.
    //
    // Continuations supersede rather than accumulate, so typing "e-v-i-d-e-n-c-e" reports
    // once, as "evidence". A report with no selection is a real finding, not missing data.
    function antiforgeryToken() {
        var meta = document.querySelector('meta[name="request-verification-token"]');
        return meta ? meta.getAttribute('content') : null;
    }

    function send(fields) {
        var token = antiforgeryToken();
        if (!token) return;

        var body = new URLSearchParams();
        Object.keys(fields).forEach(function (k) {
            if (fields[k] !== null && fields[k] !== undefined) body.set(k, fields[k]);
        });
        body.set('__RequestVerificationToken', token);
        var text = body.toString();

        // sendBeacon is the only transport that reliably survives the page being closed, and
        // it cannot set headers — so the token travels in a form-encoded body rather than the
        // usual header. Fetch with keepalive is the fallback where sendBeacon is refused.
        if (navigator.sendBeacon) {
            var blob = new Blob([text], { type: 'application/x-www-form-urlencoded' });
            if (navigator.sendBeacon(REPORT_URL, blob)) return;
        }
        if (window.fetch) {
            window.fetch(REPORT_URL, {
                method: 'POST',
                body: text,
                keepalive: true,
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' }
            }).catch(function () { /* analytics must never break the page */ });
        }
    }

    // One is a continuation of the other when either is a prefix of it — the shape of someone
    // typing a word out, or backspacing within it. Anything else is a fresh attempt.
    function isContinuation(a, b) {
        var x = (a || '').toLowerCase();
        var y = (b || '').toLowerCase();
        return x.indexOf(y) === 0 || y.indexOf(x) === 0;
    }

    function tracker(config) {
        var pending = null;

        function flush(selectedKey, selectedPosition) {
            if (!pending || pending.sent) return;
            if (pending.query.length < MIN_LENGTH) { pending = null; return; }
            pending.sent = true;

            send({
                Surface: config.surface,
                Q: pending.query,
                Scope: config.scope || null,
                HostPath: config.hostPath || null,
                Shown: JSON.stringify(pending.shown),
                SelectedKey: selectedKey || null,
                SelectedPosition: selectedPosition || null,
                LatencyMs: pending.latencyMs
            });
        }

        return {
            // Called each time the menu is filled. Supersedes a continuation of the pending
            // query; anything else settles the pending one first.
            observe: function (query, shown, latencyMs) {
                var q = (query || '').trim();

                if (pending && !isContinuation(pending.query, q)) {
                    flush(null, null);
                    pending = null;
                }

                // Emptying the box ends the attempt rather than becoming a shorter one.
                if (q.length < MIN_LENGTH) {
                    flush(null, null);
                    pending = null;
                    return;
                }

                if (pending && pending.sent) pending = null;
                pending = {
                    query: q,
                    shown: shown,
                    latencyMs: latencyMs,
                    sent: false
                };
            },
            selected: function (key, position) { flush(key, position); },
            settle: function () { flush(null, null); }
        };
    }

    function escapeHtml(text) {
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // Bold the matched run. The label comes from page content or from the server, so it is
    // escaped first and the markup added afterwards.
    function highlight(label, query) {
        var safe = escapeHtml(label);
        if (!query) return safe;
        var at = safe.toLowerCase().indexOf(escapeHtml(query).toLowerCase());
        if (at < 0) return safe;
        var end = at + query.length;
        return safe.slice(0, at) + '<strong>' + safe.slice(at, end) + '</strong>' + safe.slice(end);
    }

    // Every heading becomes a section; the text between it and the next heading becomes that
    // section's body, so a match on a paragraph still points somewhere landable.
    //
    // Headings without an id are indexed too, and given one. Only heading WIDGETS are anchored
    // by the CMS — a heading an author typed inside a rich-text block is just markup, and on a
    // long page most headings are those. Skipping them did not merely lose them: their text ran
    // on into the previous anchored heading, so searching for a word under an unanchored
    // heading offered the wrong section, some distance up the page.
    var generatedIdSeq = 0;

    function buildPageIndex() {
        var root = document.querySelector('.cpb-content');
        if (!root) return null;

        var sections = [];
        var current = null;
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, null);
        var node;

        while ((node = walker.nextNode())) {
            if (node.nodeType === 1) {
                if (/^H[1-6]$/.test(node.tagName) && !node.closest(NON_CONTENT)) {
                    var label = node.textContent.trim();
                    if (!label) continue;

                    if (!node.id) {
                        do { generatedIdSeq++; }
                        while (document.getElementById('cypmd-section-' + generatedIdSeq));
                        node.id = 'cypmd-section-' + generatedIdSeq;
                    }

                    current = { anchor: node.id, label: label, text: '', el: node };
                    sections.push(current);
                }
                continue;
            }

            if (!current || !node.nodeValue.trim()) continue;
            if (current.el.contains(node)) continue;
            if (node.parentElement && node.parentElement.closest(NON_CONTENT)) continue;
            current.text += ' ' + node.nodeValue;
        }

        sections.forEach(function (s) { s.haystack = (s.label + ' ' + s.text).toLowerCase(); });
        return sections;
    }

    // Marking the term on the page
    // ---------------------------
    // Jumping to a section answers "where", but not "where exactly" — on a long section the word
    // can still be several paragraphs down. Every occurrence is wrapped in <mark>, the same
    // element the search results page uses for its snippets, so the yellow means the same thing
    // in both places.
    var MARK_CLASS = 'cypmd-onpage-mark';

    function clearMarks(root) {
        var marks = root.querySelectorAll('mark.' + MARK_CLASS);
        for (var i = 0; i < marks.length; i++) {
            var mark = marks[i];
            var parent = mark.parentNode;
            while (mark.firstChild) parent.insertBefore(mark.firstChild, mark);
            parent.removeChild(mark);
            // Re-join the text nodes the unwrap left adjacent, so a later pass sees whole words.
            parent.normalize();
        }
    }

    function markTerm(root, term) {
        clearMarks(root);
        var needle = (term || '').trim().toLowerCase();
        if (needle.length < MIN_LENGTH) return;

        // Collected first: wrapping a node while walking would have the walker step into the
        // <mark> just inserted.
        var targets = [];
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null);
        var node;
        while ((node = walker.nextNode())) {
            if (!node.nodeValue || node.nodeValue.toLowerCase().indexOf(needle) < 0) continue;
            if (node.parentElement && node.parentElement.closest(NON_CONTENT)) continue;
            targets.push(node);
        }

        targets.forEach(function (textNode) {
            var value = textNode.nodeValue;
            var lower = value.toLowerCase();
            var fragment = document.createDocumentFragment();
            var at = 0;
            var found;
            while ((found = lower.indexOf(needle, at)) >= 0) {
                if (found > at) fragment.appendChild(document.createTextNode(value.slice(at, found)));
                var mark = document.createElement('mark');
                mark.className = MARK_CLASS;
                mark.appendChild(document.createTextNode(value.slice(found, found + needle.length)));
                fragment.appendChild(mark);
                at = found + needle.length;
            }
            if (at < value.length) fragment.appendChild(document.createTextNode(value.slice(at)));
            textNode.parentNode.replaceChild(fragment, textNode);
        });
    }

    function pageSource(sections) {
        return function (query, populateResults) {
            var q = (query || '').trim().toLowerCase();
            if (q.length < MIN_LENGTH) { populateResults([]); return; }

            var headingHits = [];
            var bodyHits = [];
            sections.forEach(function (s) {
                if (s.label.toLowerCase().indexOf(q) >= 0) headingHits.push(s);
                else if (s.haystack.indexOf(q) >= 0) bodyHits.push(s);
            });

            populateResults(headingHits.concat(bodyHits).slice(0, MAX_RESULTS));
        };
    }

    function remoteSource(scope) {
        var timer = null;
        var latest = 0;

        return function (query, populateResults) {
            var q = (query || '').trim();
            if (q.length < MIN_LENGTH) { populateResults([]); return; }

            if (timer) window.clearTimeout(timer);
            // A sequence number as well as the debounce: a slow early response must not
            // overwrite the menu a later keystroke has already filled.
            var mine = ++latest;
            timer = window.setTimeout(function () {
                var url = SUGGESTIONS_URL + '?q=' + encodeURIComponent(q)
                    + (scope ? '&scope=' + encodeURIComponent(scope) : '');
                window.fetch(url)
                    .then(function (r) { return r.ok ? r.json() : []; })
                    .then(function (data) {
                        if (mine === latest) populateResults((data || []).slice(0, MAX_RESULTS));
                    })
                    .catch(function () {
                        // An empty menu is the honest degradation — the form below still works.
                        if (mine === latest) populateResults([]);
                    });
            }, DEBOUNCE_MS);
        };
    }

    function goToSection(section, term) {
        var target = document.getElementById(section.anchor);
        if (!target) return;

        var root = document.querySelector('.cpb-content');
        if (root) markTerm(root, term);

        // Focus, not just scroll: a screen-reader or keyboard user needs the reading position
        // to move, not only the viewport.
        window.location.hash = section.anchor;
        target.setAttribute('tabindex', '-1');
        // Deferred: after a keyboard confirm the component puts focus back on its own input as
        // part of closing the menu, which would undo this. Moving focus in a later task lands
        // after that, so arrow-and-enter takes the reading position to the heading the same way
        // a click does.
        window.setTimeout(function () { target.focus(); }, 0);
    }

    function enhance(form) {
        if (form.getAttribute('data-cypmd-instant-ready')) return;

        var input = form.querySelector('input[name="q"]');
        if (!input) return;

        var searchIn = form.getAttribute('data-search-in') || 'site';
        var scope = form.getAttribute('data-scope') || '';
        var noResults = form.getAttribute('data-no-results') || 'No results found';

        var sections = null;
        if (searchIn === 'page') {
            sections = buildPageIndex();
            // No content region — the widget is being previewed in the editor. Leave the plain
            // form exactly as it is.
            if (!sections || !sections.length) return;
        }

        var lastQuery = '';
        var lastShown = [];
        var source = searchIn === 'page' ? pageSource(sections) : remoteSource(scope);

        var report = tracker({
            surface: searchIn === 'page' ? 'instant-page' : 'instant',
            scope: searchIn === 'page' ? '' : scope,
            hostPath: searchIn === 'page' ? window.location.pathname : ''
        });

        // What the person was actually shown, in the order they saw it — the browser is the
        // only place that knows this, which is why the report comes from here at all.
        function describe(results) {
            return results.map(function (r, i) {
                return searchIn === 'page'
                    ? { position: i + 1, kind: 'section', key: '#' + r.anchor, label: r.label }
                    : { position: i + 1, kind: 'page', key: r.url, label: r.label };
            });
        }

        function keyOf(result) {
            return searchIn === 'page' ? '#' + result.anchor : result.url;
        }

        var container = document.createElement('div');
        container.className = 'cypmd-search__autocomplete';
        input.parentNode.insertBefore(container, input);

        var options = {
            element: container,
            id: input.id,
            name: input.name,
            placeholder: input.getAttribute('placeholder') || '',
            defaultValue: input.value || '',
            displayMenu: 'overlay',
            minLength: MIN_LENGTH,
            confirmOnBlur: false,
            autoselect: false,
            inputClasses: 'govuk-input cypmd-search__input',
            // accessible-autocomplete owns the input's aria-describedby and rewrites it on every
            // re-render, so hint text has to arrive through tAssistiveHint to survive.
            tAssistiveHint: function () { return ASSISTIVE_HINT; },
            tNoResults: function () { return noResults; },
            source: function (query, populateResults) {
                lastQuery = (query || '').trim();
                var startedAt = (window.performance && window.performance.now)
                    ? window.performance.now() : Date.now();
                source(query, function (results) {
                    var now = (window.performance && window.performance.now)
                        ? window.performance.now() : Date.now();
                    lastShown = results || [];
                    report.observe(query, describe(lastShown), Math.round(now - startedAt));
                    populateResults(lastShown);
                });
            },
            templates: {
                inputValue: function (result) { return result ? result.label : ''; },
                suggestion: function (result) {
                    return result ? highlight(result.label, lastQuery) : '';
                }
            },
            onConfirm: function (result) {
                if (!result) return;

                var position = 0;
                for (var i = 0; i < lastShown.length; i++) {
                    if (lastShown[i] === result) { position = i + 1; break; }
                }
                report.selected(keyOf(result), position);

                if (searchIn === 'page') goToSection(result, lastQuery);
                else if (result.url) window.location.assign(result.url);
            }
        };

        input.parentNode.removeChild(input);
        accessibleAutocomplete(options);

        // The library marks its input role="combobox" and points at the menu with aria-owns,
        // which was the ARIA 1.0 spelling. A combobox now has to name the popup it controls
        // with aria-controls, and without it the control is a critical failure for anyone on a
        // screen reader. Adding it here rather than forking the library; it survives the
        // component's re-renders because the library does not manage this attribute.
        var enhanced = container.querySelector('input.autocomplete__input');
        if (enhanced && enhanced.getAttribute('aria-owns') && !enhanced.getAttribute('aria-controls')) {
            enhanced.setAttribute('aria-controls', enhanced.getAttribute('aria-owns'));
        }

        form.setAttribute('data-cypmd-instant-ready', 'true');

        // Settling points. Leaving the box, hiding the tab and closing the page all mean the
        // person has stopped working on this query, whether or not they took anything from it.
        if (enhanced) {
            enhanced.addEventListener('blur', function () { report.settle(); });
        }
        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'hidden') report.settle();
        });
        window.addEventListener('pagehide', function () { report.settle(); });
        form.addEventListener('submit', function () { report.settle(); });

        if (searchIn === 'page') {
            // Pressing the button on a page search has nowhere to navigate to, so it jumps to
            // the best match instead. With no match it falls through and submits, which lands
            // on a full search of this page — the same place a visitor without JavaScript goes.
            form.addEventListener('submit', function (event) {
                var field = form.querySelector('input[name="' + options.name + '"]')
                    || form.querySelector('input[type="text"]');
                var q = field ? field.value.trim().toLowerCase() : '';
                if (q.length < MIN_LENGTH) return;

                var match = null;
                sections.some(function (s) {
                    if (s.label.toLowerCase().indexOf(q) >= 0) { match = s; return true; }
                    return false;
                });
                if (!match) {
                    sections.some(function (s) {
                        if (s.haystack.indexOf(q) >= 0) { match = s; return true; }
                        return false;
                    });
                }

                if (match) {
                    event.preventDefault();
                    goToSection(match, q);
                }
            });
        }
    }

    function init() {
        if (typeof accessibleAutocomplete === 'undefined') return;
        Array.prototype.forEach.call(
            document.querySelectorAll('form[data-cypmd-instant-search]'),
            enhance);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
