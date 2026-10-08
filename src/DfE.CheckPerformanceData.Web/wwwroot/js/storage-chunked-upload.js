// #568: upload large files to the storage browser in parts. Progressive enhancement over the
// plain multipart form in Views/StorageAdmin/Container.cshtml: the server is the authority on
// every bound (part size, part count, maximum size, names), this script only keeps each
// request small enough to pass the ingress body cap and reports progress. It takes over only
// when the browser can slice a file and fetch; otherwise the form submits as it always did.
// On in every environment, production included: it is an admin function (#568).
// Unlike the other scripts in wwwroot/js (ES5), this one uses async/await: admin pages are used
// from current browsers only, and the feature already requires fetch and AbortController.
(function () {
    'use strict';

    var form = document.getElementById('storage-upload-form');
    if (!form || !form.getAttribute('data-block-url')) { return; }
    if (!window.fetch || !window.FormData || !window.File || !File.prototype.slice || !window.AbortController) { return; }

    var CHUNK_BYTES = parseInt(form.getAttribute('data-chunk-bytes'), 10);
    var MAX_LABEL = form.getAttribute('data-max-label');
    var BLOCK_URL = form.getAttribute('data-block-url');
    var COMMIT_URL = form.getAttribute('data-commit-url');
    var CONTAINER_URL = form.getAttribute('data-container-url');
    var RETRIES = 3;

    var filesInput = document.getElementById('files');
    var folderInput = document.getElementById('folder');
    var prefixInput = form.querySelector('input[name="prefix"]');
    var tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
    var status = document.getElementById('upload-status');
    var progress = document.getElementById('upload-progress');
    var submit = document.getElementById('upload-submit');
    var cancel = document.getElementById('upload-cancel');
    var hint = document.getElementById('files-hint');
    var jsHint = document.getElementById('files-js-hint');
    if (!filesInput || !status || !progress || !submit || !cancel || !tokenInput) { return; }

    // JavaScript is on, so the bigger limit applies: swap the hint text in place so the input's
    // aria-describedby still points at the same element.
    if (hint && jsHint) { hint.textContent = jsHint.textContent; }

    var inFlight = false;
    var controller = null;

    function setStatus(text) { status.textContent = text; }

    function uuid() {
        if (window.crypto && window.crypto.randomUUID) { return window.crypto.randomUUID(); }
        var bytes = new Uint8Array(16);
        window.crypto.getRandomValues(bytes);
        bytes[6] = (bytes[6] & 0x0f) | 0x40;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;
        var hex = Array.prototype.map.call(bytes, function (b) { return ('0' + b.toString(16)).slice(-2); }).join('');
        return hex.slice(0, 8) + '-' + hex.slice(8, 12) + '-' + hex.slice(12, 16) + '-' + hex.slice(16, 20) + '-' + hex.slice(20);
    }

    function removeErrorSummary() {
        var existing = document.getElementById('upload-error-summary');
        if (existing) { existing.remove(); }
        var group = filesInput.closest('.govuk-form-group');
        if (group) { group.classList.remove('govuk-form-group--error'); }
        filesInput.classList.remove('govuk-file-upload--error');
        var fieldError = document.getElementById('files-error');
        if (fieldError) { fieldError.remove(); }
        var ids = (filesInput.getAttribute('aria-describedby') || '').split(/\s+/).filter(function (id) { return id && id !== 'files-error'; });
        filesInput.setAttribute('aria-describedby', ids.join(' '));
    }

    // GOV.UK error summary + field error, built empty then filled so the alert is announced.
    function showError(message) {
        removeErrorSummary();
        var summary = document.createElement('div');
        summary.className = 'govuk-error-summary';
        summary.id = 'upload-error-summary';
        summary.setAttribute('role', 'alert');
        summary.setAttribute('tabindex', '-1');
        summary.setAttribute('data-module', 'govuk-error-summary');
        var heading = form.querySelector('h2');
        form.insertBefore(summary, heading);
        summary.innerHTML =
            '<div class="govuk-error-summary__body">' +
            '<h2 class="govuk-error-summary__title">There is a problem</h2>' +
            '<ul class="govuk-list govuk-error-summary__list"><li><a href="#files"></a></li></ul>' +
            '</div>';
        summary.querySelector('a').textContent = message;

        var fieldError = document.createElement('p');
        fieldError.className = 'govuk-error-message';
        fieldError.id = 'files-error';
        filesInput.parentNode.insertBefore(fieldError, filesInput);
        var prefix = document.createElement('span');
        prefix.className = 'govuk-visually-hidden';
        prefix.textContent = 'Error:';
        fieldError.appendChild(prefix);
        fieldError.appendChild(document.createTextNode(' ' + message));
        var group = filesInput.closest('.govuk-form-group');
        if (group) { group.classList.add('govuk-form-group--error'); }
        filesInput.classList.add('govuk-file-upload--error');
        filesInput.setAttribute('aria-describedby', (filesInput.getAttribute('aria-describedby') || '').trim() + ' files-error');

        summary.focus();
    }

    function setBusy(busy) {
        inFlight = busy;
        submit.disabled = busy;
        submit.textContent = busy ? 'Uploading…' : 'Upload';
        cancel.hidden = !busy;
        filesInput.disabled = busy;
        if (folderInput) { folderInput.disabled = busy; }
        progress.hidden = !busy;
        if (!busy) { progress.value = 0; }
    }

    function headers() {
        return { 'X-XSRF-TOKEN': tokenInput.value };
    }

    // One part, with bounded retries. A redirect means the sign-in session has gone: fetch
    // follows it silently, so the response is a sign-in page, not our 204.
    async function putPart(file, uploadId, index, blob) {
        var url = BLOCK_URL +
            '?prefix=' + encodeURIComponent(prefixInput ? prefixInput.value : '') +
            '&folder=' + encodeURIComponent(folderInput ? folderInput.value : '') +
            '&fileName=' + encodeURIComponent(file.name) +
            '&uploadId=' + encodeURIComponent(uploadId) +
            '&index=' + index;
        var attempt = 0;
        for (;;) {
            attempt++;
            var response;
            try {
                response = await fetch(url, { method: 'PUT', body: blob, headers: headers(), credentials: 'same-origin', signal: controller.signal });
            } catch (err) {
                if (controller.signal.aborted) { throw err; }
                response = null;
            }
            if (response && response.redirected) { throw new Error('Your session has ended. Sign in again and upload the file again.'); }
            if (response && response.ok) { return; }
            if (response && response.status === 400) {
                var body = await response.json().catch(function () { return {}; });
                throw new Error(body.error || 'The upload was refused.');
            }
            if (response && response.status === 404) { throw new Error('The folder or container could not be found.'); }
            if (attempt >= RETRIES) {
                throw new Error(response && response.status === 413
                    ? 'The upload was refused because a part was too large. Try again later.'
                    : 'The upload failed. Check your connection and upload the file again.');
            }
            setStatus('Retrying part ' + (index + 1) + ' of ' + file.name + ', attempt ' + (attempt + 1) + ' of ' + RETRIES + '.');
            await new Promise(function (resolve) { setTimeout(resolve, 1000 * attempt); });
        }
    }

    async function commit(file, uploadId, blockCount) {
        var body = new URLSearchParams();
        body.set('prefix', prefixInput ? prefixInput.value : '');
        body.set('folder', folderInput ? folderInput.value : '');
        body.set('fileName', file.name);
        body.set('uploadId', uploadId);
        body.set('blockCount', String(blockCount));
        body.set('contentType', file.type || '');
        body.set('__RequestVerificationToken', tokenInput.value);
        var response = await fetch(COMMIT_URL, { method: 'POST', body: body, headers: headers(), credentials: 'same-origin', signal: controller.signal });
        if (response.redirected) { throw new Error('Your session has ended. Sign in again and upload the file again.'); }
        if (response.ok) { return; }
        var json = await response.json().catch(function () { return {}; });
        throw new Error(json.error || 'The upload could not be completed.');
    }

    async function uploadFile(file, fileNumber, fileCount) {
        var uploadId = uuid();
        var blockCount = Math.max(1, Math.ceil(file.size / CHUNK_BYTES));
        var label = fileCount > 1 ? file.name + ' (file ' + fileNumber + ' of ' + fileCount + ')' : file.name;
        var announcedQuarter = 0;
        setStatus('Uploading ' + label + ': 0%');
        for (var index = 0; index < blockCount; index++) {
            var start = index * CHUNK_BYTES;
            var part = file.slice(start, Math.min(file.size, start + CHUNK_BYTES));
            await putPart(file, uploadId, index, part);
            var percent = Math.floor(((index + 1) / blockCount) * 100);
            progress.value = percent;
            // Announce at 25% steps only: a screen reader fed every part would never stop talking.
            var quarter = Math.floor(percent / 25);
            if (quarter > announcedQuarter && percent < 100) {
                announcedQuarter = quarter;
                setStatus('Uploading ' + label + ': ' + percent + '%');
            }
        }
        await commit(file, uploadId, blockCount);
        setStatus('Uploaded ' + label + '.');
    }

    form.addEventListener('submit', async function (event) {
        if (inFlight) { event.preventDefault(); return; }
        var files = Array.prototype.slice.call(filesInput.files || []);
        if (files.length === 0) { return; }           // let the server answer the empty submit
        event.preventDefault();
        removeErrorSummary();

        // Read at submit time, not at load: the E2E test shrinks it to avoid a 2 GB file.
        var maxBytes = parseInt(form.getAttribute('data-max-bytes'), 10);
        var tooBig = files.filter(function (f) { return f.size > maxBytes; });
        if (tooBig.length) {
            showError('The selected file must be smaller than ' + MAX_LABEL + '. ' + tooBig[0].name + ' is too large.');
            return;
        }

        controller = new AbortController();
        setBusy(true);
        try {
            for (var i = 0; i < files.length; i++) {
                await uploadFile(files[i], i + 1, files.length);
            }
            setStatus(files.length === 1 ? 'Upload complete. Reloading the folder.' : 'All ' + files.length + ' files uploaded. Reloading the folder.');
            inFlight = false;
            window.location.assign(CONTAINER_URL);
        } catch (err) {
            setBusy(false);
            if (controller.signal.aborted) {
                setStatus('Upload cancelled.');
                status.setAttribute('tabindex', '-1');
                status.focus();
                return;
            }
            setStatus('');
            showError(err && err.message ? err.message : 'The upload failed. Upload the file again.');
        }
    });

    cancel.addEventListener('click', function () {
        if (controller) { controller.abort(); }
    });

    window.addEventListener('beforeunload', function (event) {
        if (!inFlight) { return; }
        event.preventDefault();
        event.returnValue = '';
    });
})();
