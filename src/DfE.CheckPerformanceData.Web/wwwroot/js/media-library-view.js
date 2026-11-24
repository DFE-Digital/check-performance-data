// Remembers each viewer's media library view (grid, details or large) in this browser.
//
// The page loads this script before the list, without defer, so a switch happens before the
// list shows. The view in the address always wins, and is remembered. With no view in the
// address, the page goes to the remembered view. The server decides the view, so the page works
// the same with no script or no storage: it shows the view in the address, else the grid.
//
// The script tag carries data-media-library-view-apply only on a GET of the list. A page that
// answers a form post (an upload, a new folder) shows its own results and is never left.
(function () {
    'use strict';

    var STORAGE_KEY = 'media-library-view';
    var VIEWS = ['grid', 'details', 'large'];
    var DEFAULT_VIEW = 'grid';

    function known(view) {
        return typeof view === 'string' && VIEWS.indexOf(view) >= 0 ? view : null;
    }

    function read() {
        try {
            return known(window.localStorage.getItem(STORAGE_KEY));
        } catch (e) {
            return null; // storage unavailable (private mode, blocked): the page still works
        }
    }

    function save(view) {
        try {
            window.localStorage.setItem(STORAGE_KEY, view);
        } catch (e) {
            /* storage unavailable: nothing is remembered */
        }
    }

    var script = document.currentScript;
    var params = new URLSearchParams(window.location.search);
    var asked = known((params.get('view') || '').toLowerCase());

    if (asked) {
        save(asked);
        return;
    }
    if (!script || !script.hasAttribute('data-media-library-view-apply')) { return; }

    var saved = read();
    if (!saved || saved === DEFAULT_VIEW) { return; }
    params.set('view', saved);
    window.location.replace(window.location.pathname + '?' + params.toString() + window.location.hash);
})();
