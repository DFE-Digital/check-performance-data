// Client-side analytics beacons (AB#286387 R18/R19/R23).
// Fire-and-forget: failures must never affect the page.
(function () {
    'use strict';

    function getToken() {
        var meta = document.querySelector('meta[name="request-verification-token"]');
        if (meta && meta.content) { return meta.content; }
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function postEvent(eventName, props) {
        var token = getToken();
        if (!token) { return; }
        var body = Object.assign({ eventName: eventName }, props || {});
        try {
            fetch('/client-events', {
                method: 'POST',
                keepalive: true,
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'X-XSRF-TOKEN': token
                },
                body: JSON.stringify(body)
            }).catch(function () { /* swallow */ });
        } catch (e) { /* swallow */ }
    }

    // R18: help/details expanders — fire on open only.
    // 'toggle' does not bubble, so listen in the capture phase.
    document.addEventListener('toggle', function (e) {
        var details = e.target;
        if (!details || details.tagName !== 'DETAILS' || !details.open) { return; }
        if (!details.classList.contains('govuk-details')) { return; }
        var summary = details.querySelector('.govuk-details__summary-text');
        postEvent('help_details_expanded', {
            expandText: summary ? summary.textContent.trim() : ''
        });
    }, true);

    // R19: external link clicks — send hostname only; the server maps GIAS.
    document.addEventListener('click', function (e) {
        var anchor = e.target && e.target.closest ? e.target.closest('a[href]') : null;
        if (!anchor) { return; }
        var url;
        try { url = new URL(anchor.href, window.location.href); } catch (err) { return; }
        if (url.protocol !== 'http:' && url.protocol !== 'https:') { return; }
        if (url.origin === window.location.origin) { return; }
        postEvent('external_link_clicked', { destination: url.hostname });
    }, true);

    // R23: evidence file selected (before Upload is clicked).
    document.addEventListener('change', function (e) {
        var input = e.target;
        if (!input || input.type !== 'file' || input.name !== 'fileUpload') { return; }
        if (!input.files || input.files.length === 0) { return; }
        postEvent('evidence_file_selected', {});
    }, true);
})();
