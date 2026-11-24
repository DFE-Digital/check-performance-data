// Media library image page: the "Copy link" button.
//
// Without JavaScript the public link sits in a read-only field that the editor can select and
// copy. This script shows the button and copies the link with it. The Clipboard API exists only
// on secure (HTTPS or localhost) pages, so on a plain HTTP test stack the script selects the field
// and uses the older copy command instead. The status region announces the result.
(function () {
    'use strict';

    var field = document.getElementById('public-url');
    var button = document.querySelector('[data-media-copy-link]');
    var status = document.querySelector('[data-media-copy-status]');
    if (!field || !button || !status) { return; }

    var COPIED = 'Link copied.';
    var NOT_COPIED = 'The link could not be copied. Select it and copy it yourself.';

    function legacyCopy() {
        field.focus();
        field.select();
        try {
            return document.execCommand('copy');
        } catch (e) {
            return false;
        }
    }

    function report(copied) {
        status.textContent = copied ? COPIED : NOT_COPIED;
    }

    button.addEventListener('click', function () {
        status.textContent = '';
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(field.value).then(
                function () { report(true); },
                function () { report(legacyCopy()); });
        } else {
            report(legacyCopy());
        }
    });

    button.hidden = false;
    document.documentElement.setAttribute('data-media-detail-init', 'true');
})();
