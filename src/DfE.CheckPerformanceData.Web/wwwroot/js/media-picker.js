// Media picker: a modal dialog that lists the media library and returns the image the editor
// chooses. The page renders the dialog once (Views/Shared/ContentPages/_MediaPicker.cshtml).
//
// Public API, for the Image widget editor and the rich text editor:
//
//   MediaPicker.open({
//     opener: element,          // gets focus back when the dialog closes (optional)
//     onSelect: function (item) // called with the chosen image, then the dialog closes
//   });
//   MediaPicker.close();        // closes the dialog without a choice
//   MediaPicker.isAvailable();  // false when the page has no picker dialog
//   MediaPicker.upload(file)    // a Promise of the uploaded item, or of { error: message }
//
// An item is { id, fileName, alt, width, height, thumbUrl, editUrl, src, isSvg, isAnimated,
// canCrop, duplicate }. alt is the library's default alt text ("" when none). src is the unsigned
// stored file, which rich text keeps with the id. editUrl is a signed copy, large enough to crop on. canCrop is false for an image always shown as uploaded.
//
// Keyboard: the images are radio buttons. Tab moves into them, the arrow keys move between
// them, and Enter chooses the focused image. Escape closes the dialog. Tab stays inside it.
// Uploads go one file at a time, with the antiforgery token in the X-XSRF-TOKEN header. Text
// from the server is set with textContent only.
(function () {
    'use strict';

    var dialog = document.querySelector('[data-media-picker]');
    if (!dialog || typeof dialog.showModal !== 'function' || !window.fetch) { return; }

    var listUrl = dialog.getAttribute('data-list-url');
    var uploadUrl = dialog.getAttribute('data-upload-url');
    var maxBytes = parseInt(dialog.getAttribute('data-max-bytes'), 10) || 0;
    var token = dialog.querySelector('input[name="__RequestVerificationToken"]');
    var searchForm = dialog.querySelector('[data-media-picker-search]');
    var searchInput = dialog.querySelector('#media-picker-search');
    var status = dialog.querySelector('[data-media-picker-status]');
    var chooseForm = dialog.querySelector('[data-media-picker-choose]');
    var grid = dialog.querySelector('[data-media-picker-grid]');
    var paging = dialog.querySelector('[data-media-picker-paging]');
    var prev = dialog.querySelector('[data-media-picker-prev]');
    var next = dialog.querySelector('[data-media-picker-next]');
    var pageText = dialog.querySelector('[data-media-picker-page]');
    var useButton = dialog.querySelector('[data-media-picker-use]');
    var fileInput = dialog.querySelector('#media-picker-file');
    var uploadGroup = dialog.querySelector('[data-media-picker-upload-group]');
    var uploadError = dialog.querySelector('[data-media-picker-upload-error]');
    var uploadButton = dialog.querySelector('[data-media-picker-upload]');

    var FAILED = 'The images could not be loaded. Close this window and try again.';
    var UPLOAD_FAILED = 'The file could not be uploaded. Try again.';
    var TOO_LARGE = 'The selected file must be smaller than 20 MB';

    var state = { opener: null, onSelect: null, query: '', page: 1, totalPages: 1, items: {} };

    function element(tag, className, text) {
        var node = document.createElement(tag);
        if (className) { node.className = className; }
        if (text !== undefined && text !== null) { node.textContent = text; }
        return node;
    }

    function announce(text) { status.textContent = text; }

    function setUseEnabled(enabled) {
        useButton.disabled = !enabled;
        useButton.setAttribute('aria-disabled', enabled ? 'false' : 'true');
    }

    function radios() { return grid.querySelectorAll('input[type="radio"]'); }

    function checkedItem() {
        var checked = grid.querySelector('input[type="radio"]:checked');
        return checked ? state.items[checked.value] : null;
    }

    function describe(item) {
        var parts = [];
        if (item.width && item.height) { parts.push(item.width + ' × ' + item.height + ' pixels'); }
        parts.push(item.alt ? 'Alt text: ' + item.alt : 'No alt text');
        return parts.join('. ');
    }

    function renderItem(item, prepend) {
        state.items[item.id] = item;
        var id = 'media-picker-item-' + item.id;
        var wrapper = element('div', 'app-media-picker__item');
        var radio = element('input', 'app-media-picker__radio');
        radio.type = 'radio';
        radio.name = 'media-picker-item';
        radio.id = id;
        radio.value = item.id;
        var label = element('label', 'app-media-picker__label');
        label.htmlFor = id;
        var thumb = element('span', 'app-media-grid__thumb');
        var img = element('img', 'app-media-grid__img' + (item.canCrop ? '' : ' app-media-grid__img--fixed'));
        img.src = item.thumbUrl;
        img.alt = '';
        img.loading = 'lazy';
        thumb.appendChild(img);
        label.appendChild(thumb);
        label.appendChild(element('span', 'app-media-grid__name govuk-body-s govuk-!-font-weight-bold', item.fileName));
        label.appendChild(element('span', 'app-media-grid__meta govuk-body-s', describe(item)));
        wrapper.appendChild(radio);
        wrapper.appendChild(label);
        if (prepend && grid.firstChild) { grid.insertBefore(wrapper, grid.firstChild); } else { grid.appendChild(wrapper); }
        return radio;
    }

    function load(query, page, select) {
        state.query = query;
        state.page = page;
        announce('Loading images');
        var url = listUrl + '?q=' + encodeURIComponent(query) + '&page=' + page;
        return fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (response) {
                if (!response.ok) { throw new Error(String(response.status)); }
                return response.json();
            })
            .then(function (data) {
                grid.textContent = '';
                state.items = {};
                state.totalPages = data.totalPages || 1;
                (data.items || []).forEach(function (item) { renderItem(item, false); });
                if (select && !state.items[select.id]) { renderItem(select, true); }
                paging.hidden = state.totalPages <= 1;
                prev.disabled = state.page <= 1;
                next.disabled = state.page >= state.totalPages;
                pageText.textContent = 'Page ' + state.page + ' of ' + state.totalPages;
                setUseEnabled(false);
                if (!data.totalCount && !select) {
                    announce(query ? 'No images match ‘' + query + '’' : 'The media library has no images yet. Upload one below.');
                } else {
                    announce(query
                        ? data.totalCount + (data.totalCount === 1 ? ' image matches ‘' : ' images match ‘') + query + '’'
                        : data.totalCount + (data.totalCount === 1 ? ' image in the library' : ' images in the library'));
                }
                if (select) {
                    var radio = grid.querySelector('input[value="' + select.id + '"]');
                    if (radio) { radio.checked = true; setUseEnabled(true); radio.focus(); }
                }
            })
            .catch(function () {
                grid.textContent = '';
                paging.hidden = true;
                announce(FAILED);
            });
    }

    function choose(item) {
        if (!item) { return; }
        var callback = state.onSelect;
        close();
        if (callback) { callback(item); }
    }

    function close() {
        if (dialog.open) { dialog.close(); }
    }

    function focusable() {
        var sel = 'button:not([disabled]), [href], input:not([type="hidden"]):not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
        return Array.prototype.filter.call(dialog.querySelectorAll(sel), function (el) {
            if (el.offsetParent === null && el !== document.activeElement) { return false; }
            // Only one radio of the group is a Tab stop: the checked one, or else the first.
            if (el.type === 'radio') {
                var checked = grid.querySelector('input[type="radio"]:checked');
                return checked ? el === checked : el === radios()[0];
            }
            return true;
        });
    }

    // The Tab trap is explicit: the browser's own trap can let focus reach the page behind.
    dialog.addEventListener('keydown', function (event) {
        if (event.key !== 'Tab') { return; }
        var els = focusable();
        if (!els.length) { return; }
        var first = els[0];
        var last = els[els.length - 1];
        var active = document.activeElement;
        if (event.shiftKey && (active === first || !dialog.contains(active))) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && (active === last || !dialog.contains(active))) {
            event.preventDefault();
            first.focus();
        }
    });

    // Enter on an image chooses it. A radio does not submit its form on Enter in every browser.
    grid.addEventListener('keydown', function (event) {
        if (event.key !== 'Enter' || !event.target || event.target.type !== 'radio') { return; }
        event.preventDefault();
        event.target.checked = true;
        choose(state.items[event.target.value]);
    });

    grid.addEventListener('change', function () { setUseEnabled(!!checkedItem()); });

    grid.addEventListener('dblclick', function (event) {
        var label = event.target.closest ? event.target.closest('label') : null;
        if (!label) { return; }
        var radio = document.getElementById(label.htmlFor);
        if (radio) { radio.checked = true; choose(state.items[radio.value]); }
    });

    chooseForm.addEventListener('submit', function (event) {
        event.preventDefault();
        choose(checkedItem());
    });

    searchForm.addEventListener('submit', function (event) {
        event.preventDefault();
        load(searchInput.value.trim(), 1);
    });

    prev.addEventListener('click', function () { if (state.page > 1) { load(state.query, state.page - 1); } });
    next.addEventListener('click', function () { if (state.page < state.totalPages) { load(state.query, state.page + 1); } });

    dialog.querySelector('[data-media-picker-close]').addEventListener('click', close);
    dialog.querySelector('[data-media-picker-cancel]').addEventListener('click', close);

    // A click on the backdrop lands on the dialog itself.
    dialog.addEventListener('click', function (event) { if (event.target === dialog) { close(); } });

    // Escape fires cancel, then close. Every way out ends here, so focus goes back to the opener.
    dialog.addEventListener('close', function () {
        var opener = state.opener;
        state.opener = null;
        state.onSelect = null;
        if (opener && typeof opener.focus === 'function') { opener.focus(); }
    });

    function showUploadError(message) {
        uploadError.textContent = '';
        uploadError.appendChild(element('span', 'govuk-visually-hidden', 'Error: '));
        uploadError.appendChild(document.createTextNode(message));
        uploadError.hidden = false;
        uploadGroup.classList.add('govuk-form-group--error');
        fileInput.classList.add('govuk-file-upload--error');
        fileInput.setAttribute('aria-describedby', 'media-picker-file-hint media-picker-file-error');
        announce(message);
    }

    function clearUploadError() {
        uploadError.hidden = true;
        uploadError.textContent = '';
        uploadGroup.classList.remove('govuk-form-group--error');
        fileInput.classList.remove('govuk-file-upload--error');
        fileInput.setAttribute('aria-describedby', 'media-picker-file-hint');
    }

    // Sends one file to the library. Resolves with the new (or matching) image, or with
    // { error: message } when the server or the network refuses it. Never rejects.
    function upload(file) {
        if (maxBytes && file.size > maxBytes) { return Promise.resolve({ error: TOO_LARGE }); }
        var body = new FormData();
        body.append('file', file, file.name);
        return fetch(uploadUrl, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json', 'X-XSRF-TOKEN': token ? token.value : '' },
            body: body
        })
            .then(function (response) {
                if (response.status === 413) { return { error: TOO_LARGE }; }
                var type = response.headers.get('Content-Type') || '';
                if (type.indexOf('application/json') === -1) { return { error: UPLOAD_FAILED }; }
                return response.json();
            })
            .then(function (result) {
                return result && !result.error && result.id ? result : { error: (result && result.error) || UPLOAD_FAILED };
            })
            .catch(function () { return { error: UPLOAD_FAILED }; });
    }

    uploadButton.addEventListener('click', function () {
        clearUploadError();
        var file = fileInput.files && fileInput.files[0];
        if (!file) { showUploadError('Select an image to upload'); fileInput.focus(); return; }
        if (maxBytes && file.size > maxBytes) { showUploadError(TOO_LARGE); fileInput.focus(); return; }

        uploadButton.disabled = true;
        announce('Uploading ' + file.name);
        upload(file)
            .then(function (result) {
                if (result.error) { showUploadError(result.error); fileInput.focus(); return; }
                fileInput.value = '';
                searchInput.value = '';
                return load('', 1, result).then(function () {
                    announce(result.duplicate
                        ? 'This image is already in the library as ' + result.fileName + '. It is selected.'
                        : result.fileName + ' uploaded and selected.');
                });
            })
            .catch(function () { showUploadError(UPLOAD_FAILED); })
            .then(function () { uploadButton.disabled = false; });
    });

    function open(options) {
        options = options || {};
        state.opener = options.opener || document.activeElement;
        state.onSelect = typeof options.onSelect === 'function' ? options.onSelect : null;
        clearUploadError();
        searchInput.value = '';
        if (!dialog.open) { dialog.showModal(); }
        searchInput.focus();
        load('', 1);
    }

    window.MediaPicker = {
        open: open,
        close: close,
        upload: upload,
        isAvailable: function () { return true; }
    };
    document.documentElement.setAttribute('data-media-picker-init', 'true');
})();
