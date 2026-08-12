// AB#296081: warn about a duplicate evidence file name at selection time, before the
// user clicks "Upload file". The server (JourneyController → ValidateDuplicateFileName)
// re-checks on POST and is the authority — this script is a courtesy warning only, so it
// never disables the button and never clears the input. Site-wide by the same delegation
// pattern as analytics-events.js: it self-gates on input[name=fileUpload].
(function () {
    'use strict';

    var MESSAGE = 'The file name has already been used. Upload a file with a different name.';
    var ERROR_ID = 'fileUpload-duplicate-error';

    function existingNames(input) {
        var raw = input.getAttribute('data-existing-file-names');
        if (!raw) { return []; }
        try { return JSON.parse(raw); } catch (err) { return []; }
    }

    function clearWarning(input) {
        var warning = document.getElementById(ERROR_ID);
        if (warning) { warning.remove(); }
        // Only lift error styling this script added — a server-rendered upload error
        // (id="fileUpload-error") keeps the group in its error state.
        if (!document.getElementById('fileUpload-error')) {
            var group = input.closest('.govuk-form-group');
            if (group) { group.classList.remove('govuk-form-group--error'); }
            input.classList.remove('govuk-file-upload--error');
        }
        var ids = (input.getAttribute('aria-describedby') || '')
            .split(/\s+/).filter(function (id) { return id && id !== ERROR_ID; });
        if (ids.length) { input.setAttribute('aria-describedby', ids.join(' ')); }
        else { input.removeAttribute('aria-describedby'); }
    }

    function showWarning(input) {
        clearWarning(input);
        var p = document.createElement('p');
        p.className = 'govuk-error-message';
        p.id = ERROR_ID;
        // Injected after page load, so role=alert is needed for screen readers to hear it —
        // the static GDS pattern relies on a page reload instead.
        p.setAttribute('role', 'alert');
        var prefix = document.createElement('span');
        prefix.className = 'govuk-visually-hidden';
        prefix.textContent = 'Error:';
        p.appendChild(prefix);
        p.appendChild(document.createTextNode(' ' + MESSAGE));
        input.parentNode.insertBefore(p, input);
        var group = input.closest('.govuk-form-group');
        if (group) { group.classList.add('govuk-form-group--error'); }
        input.classList.add('govuk-file-upload--error');
        var ids = (input.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
        ids.push(ERROR_ID);
        input.setAttribute('aria-describedby', ids.join(' '));
    }

    document.addEventListener('change', function (e) {
        var input = e.target;
        if (!input || input.type !== 'file' || input.name !== 'fileUpload') { return; }
        if (!input.files || input.files.length === 0) { clearWarning(input); return; }
        var selected = input.files[0].name.toLowerCase();
        var duplicate = existingNames(input).some(function (name) {
            return typeof name === 'string' && name.toLowerCase() === selected;
        });
        if (duplicate) { showWarning(input); } else { clearWarning(input); }
    }, true);
})();
