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

    // Every heading with an anchor becomes a section; the text between it and the next heading
    // becomes that section's body, so a match on a paragraph still points somewhere landable.
    function buildPageIndex() {
        var root = document.querySelector('.cpb-content');
        if (!root) return null;

        var sections = [];
        var current = null;
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, null);
        var node;

        while ((node = walker.nextNode())) {
            if (node.nodeType === 1) {
                if (/^H[1-6]$/.test(node.tagName) && node.id && !node.closest(NON_CONTENT)) {
                    current = { anchor: node.id, label: node.textContent.trim(), text: '', el: node };
                    if (current.label) sections.push(current);
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

    function goToSection(section) {
        var target = document.getElementById(section.anchor);
        if (!target) return;
        // Focus, not just scroll: a screen-reader or keyboard user needs the reading position
        // to move, not only the viewport.
        window.location.hash = section.anchor;
        target.setAttribute('tabindex', '-1');
        target.focus();
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
        var source = searchIn === 'page' ? pageSource(sections) : remoteSource(scope);

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
                source(query, populateResults);
            },
            templates: {
                inputValue: function (result) { return result ? result.label : ''; },
                suggestion: function (result) {
                    return result ? highlight(result.label, lastQuery) : '';
                }
            },
            onConfirm: function (result) {
                if (!result) return;
                if (searchIn === 'page') goToSection(result);
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
                    goToSection(match);
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
