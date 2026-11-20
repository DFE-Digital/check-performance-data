// Media library upload enhancement.
//
// Without JavaScript the upload form is a plain multipart post and the server shows the results.
// With it, this script takes over submit and sends each chosen file in its own request, at most
// two at a time, because the server decodes only a few images at once. Each file gets a row with
// a progress bar. The live region announces when the uploads start and when each file ends, not
// every percent. The antiforgery token goes in the X-XSRF-TOKEN header. When the uploads end, the
// script reloads the library grid so new images appear, and fills the error summary for any file
// that was refused. Text from file names and server replies is set with textContent only.
(function () {
    'use strict';

    var form = document.getElementById('media-upload-form');
    if (!form || !window.XMLHttpRequest || !window.FormData || !window.DOMParser) { return; }

    var input = form.querySelector('input[name="files"]');
    var tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
    var submit = form.querySelector('[data-media-upload-submit]');
    var status = document.querySelector('[data-media-upload-status]');
    var results = document.querySelector('[data-media-upload-results]');
    var summary = document.querySelector('[data-media-upload-errors]');
    var summaryList = document.querySelector('[data-media-upload-error-list]');
    if (!input || !tokenInput || !submit || !status || !results || !summary || !summaryList) { return; }

    var MAX_BYTES = parseInt(form.getAttribute('data-max-bytes'), 10) || 0;
    var TOO_LARGE = form.getAttribute('data-too-large-message') || 'The selected file is too large.';
    var FAILED = 'The file could not be uploaded. Reload the page and try again.';
    var PARALLEL = 2;

    document.documentElement.setAttribute('data-media-upload-init', 'true');

    function element(tag, className, text) {
        var node = document.createElement(tag);
        if (className) { node.className = className; }
        if (text !== undefined) { node.textContent = text; }
        return node;
    }

    function tag(text, colour) {
        return element('strong', 'govuk-tag govuk-tag--' + colour, text);
    }

    function link(href, text, hiddenSuffix) {
        var a = element('a', 'govuk-link', text);
        a.href = href;
        if (hiddenSuffix) { a.appendChild(element('span', 'govuk-visually-hidden', hiddenSuffix)); }
        return a;
    }

    function announce(text) {
        status.textContent = text;
    }

    function addRow(file) {
        var row = element('li', 'app-media-upload-result');
        row.setAttribute('data-media-upload-result', 'uploading');
        var name = element('span', null, file.name);
        var progress = element('progress');
        progress.max = 100;
        progress.value = 0;
        progress.setAttribute('aria-label', 'Upload progress for ' + file.name);
        row.appendChild(name);
        row.appendChild(progress);
        results.appendChild(row);
        return { row: row, progress: progress };
    }

    // Replaces the row's content with the outcome, as the server renders it after a plain post.
    function finishRow(view, fileName, result) {
        var row = view.row;
        row.textContent = '';
        row.setAttribute('data-media-upload-result', result.outcomeName);
        if (result.outcomeName === 'created') {
            row.appendChild(tag('Uploaded', 'green'));
            row.appendChild(link(result.detailUrl, result.fileName));
        } else if (result.outcomeName === 'duplicate') {
            row.appendChild(tag('Already in the library', 'yellow'));
            row.appendChild(element('span', null, 'This image is already in the library as ' + result.existingFileName + '.'));
            row.appendChild(link(result.detailUrl, 'Use the existing image', ' for ' + fileName));
        } else {
            row.appendChild(tag('Not uploaded', 'red'));
            row.appendChild(element('span', null, fileName + ': ' + result.rejection));
        }
    }

    function rejected(fileName, reason) {
        return { outcomeName: 'rejected', fileName: fileName, rejection: reason };
    }

    function send(file, view) {
        return new Promise(function (resolve) {
            if (MAX_BYTES && file.size > MAX_BYTES) { resolve(rejected(file.name, TOO_LARGE)); return; }

            var body = new FormData();
            body.append('files', file, file.name);
            var xhr = new XMLHttpRequest();
            xhr.open('POST', form.action);
            xhr.setRequestHeader('Accept', 'application/json');
            xhr.setRequestHeader('X-XSRF-TOKEN', tokenInput.value);
            xhr.upload.addEventListener('progress', function (event) {
                if (event.lengthComputable) { view.progress.value = Math.round(event.loaded / event.total * 100); }
            });
            xhr.addEventListener('load', function () {
                var type = xhr.getResponseHeader('Content-Type') || '';
                if (xhr.status === 413) { resolve(rejected(file.name, TOO_LARGE)); return; }
                if (xhr.status !== 200 || type.indexOf('application/json') === -1) { resolve(rejected(file.name, FAILED)); return; }
                try {
                    var list = JSON.parse(xhr.responseText);
                    resolve(list && list.length ? list[0] : rejected(file.name, FAILED));
                } catch (e) {
                    resolve(rejected(file.name, FAILED));
                }
            });
            xhr.addEventListener('error', function () { resolve(rejected(file.name, FAILED)); });
            xhr.addEventListener('abort', function () { resolve(rejected(file.name, FAILED)); });
            xhr.send(body);
        });
    }

    // Fetches this page again and swaps in its grid, so the new images show without a reload.
    function refreshLibrary() {
        var region = document.querySelector('[data-media-library-region]');
        if (!region || !window.fetch) { return Promise.resolve(); }
        return fetch(window.location.href, { credentials: 'same-origin', redirect: 'manual' })
            .then(function (response) { return response.ok ? response.text() : null; })
            .then(function (html) {
                if (!html) { return; }
                var fresh = new DOMParser().parseFromString(html, 'text/html').querySelector('[data-media-library-region]');
                if (fresh) { region.replaceWith(document.importNode(fresh, true)); }
            })
            .catch(function () { /* The grid stays as it was; the result rows still link to each image. */ });
    }

    function showErrors(failures) {
        summaryList.textContent = '';
        failures.forEach(function (failure) {
            var item = element('li');
            item.appendChild(link('#files', failure.fileName + ': ' + failure.rejection));
            summaryList.appendChild(item);
        });
        summary.hidden = failures.length === 0;
        if (failures.length) {
            summary.setAttribute('tabindex', '-1');
            summary.focus();
        }
    }

    form.addEventListener('submit', function (event) {
        var files = Array.prototype.slice.call(input.files || []);
        // No file chosen: the plain post shows the server's error message.
        if (files.length === 0) { return; }
        event.preventDefault();
        if (submit.disabled) { return; }

        submit.disabled = true;
        results.textContent = '';
        showErrors([]);
        announce(files.length === 1 ? 'Uploading 1 file.' : 'Uploading ' + files.length + ' files.');

        var outcomes = [];
        var next = 0;
        function worker() {
            if (next >= files.length) { return Promise.resolve(); }
            var file = files[next++];
            var view = addRow(file);
            return send(file, view).then(function (result) {
                finishRow(view, file.name, result);
                outcomes.push(result);
                announce(result.outcomeName === 'created' ? file.name + ' uploaded.'
                    : result.outcomeName === 'duplicate' ? file.name + ' is already in the library.'
                    : file.name + ' was not uploaded.');
                return worker();
            });
        }

        var workers = [];
        for (var i = 0; i < Math.min(PARALLEL, files.length); i++) { workers.push(worker()); }

        Promise.all(workers)
            .then(function () {
                var count = function (name) { return outcomes.filter(function (o) { return o.outcomeName === name; }).length; };
                var created = count('created');
                return (created ? refreshLibrary() : Promise.resolve()).then(function () {
                    announce('Finished. ' + created + ' uploaded, ' + count('duplicate') + ' already in the library, '
                        + count('rejected') + ' not uploaded.');
                    showErrors(outcomes.filter(function (o) { return o.outcomeName === 'rejected'; }));
                });
            })
            .then(function () { submit.disabled = false; });
    });
})();
