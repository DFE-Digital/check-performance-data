// Image widget editor enhancement.
//
// Without JavaScript the Image widget form is a set of plain fields: the editor pastes an image
// id and types the crop and focal point. With it, this script:
//
// - shows "Choose image" / "Change image", which opens the media picker (media-picker.js), and
//   shows the chosen image's thumbnail and file name. Choosing an image fills in empty alt text
//   from the library and clears the crop and focal point of the image before it.
// - makes "This image is decorative" clear the alt text and make it read-only.
// - adds a crop tool (Cropper.js) with shape presets, and a focal point tool that works with a
//   click or the arrow keys. Both write into the same props[crop], props[aspect] and
//   props[focal] fields that the form posts. An SVG, GIF or animated image has neither tool.
// - disables the focal point button while a preset shape and a crop are both set. That crop is
//   already in the shape, so the focal point cannot change the picture. A hint says why, and a
//   status message announces the change. The typed focal field stays, so its value still posts.
// - shows a live preview: the server renders the widget's own view from the unsaved values, so
//   the preview matches the page.
//
// Crop values are fractions of the image, x,y,width,height, rounded to 3 decimal places. The
// focal point is x,y as fractions of the whole image.
(function () {
    'use strict';

    var editors = document.querySelectorAll('[data-image-editor]');
    if (!editors.length) { return; }

    var PLACES = 1000;
    var PRESETS = { '': NaN, '16:9': 16 / 9, '4:3': 4 / 3, '1:1': 1, '3:2': 3 / 2 };

    function round(value) { return Math.round(value * PLACES) / PLACES; }

    function clamp(value, low, high) { return Math.min(high, Math.max(low, value)); }

    // "0.25" not "0.250", the same text the server writes in the image URL.
    function number(value) { return String(round(value)); }

    function parseFractions(text, count) {
        var parts = (text || '').split(',');
        if (parts.length !== count) { return null; }
        var numbers = parts.map(function (p) { return parseFloat(p); });
        return numbers.every(function (n) { return isFinite(n) && n >= 0 && n <= 1; }) ? numbers : null;
    }

    function setup(root) {
        var form = root.closest('form');
        var q = function (selector) { return root.querySelector(selector); };
        var mediaId = q('[data-image-editor-media-id]');
        var fallback = q('[data-image-editor-fallback]');
        var chosen = q('[data-image-editor-chosen]');
        var thumb = q('[data-image-editor-thumb]');
        var name = q('[data-image-editor-name]');
        var chooseButton = q('[data-image-editor-choose]');
        var alt = q('[data-image-editor-alt]');
        var decorative = q('[data-image-editor-decorative]');
        var tools = q('[data-image-editor-tools]');
        var noCrop = q('[data-image-editor-nocrop]');
        var cropTools = q('[data-image-editor-croptools]');
        var cropToggle = q('[data-image-editor-crop-toggle]');
        var cropPanel = q('[data-image-editor-crop-panel]');
        var cropImg = q('[data-image-editor-crop-img]');
        var cropReset = q('[data-image-editor-crop-reset]');
        var shapes = root.querySelectorAll('[data-image-editor-shape]');
        var focalToggle = q('[data-image-editor-focal-toggle]');
        var focalPanel = q('[data-image-editor-focal-panel]');
        var focalArea = q('[data-image-editor-focal-area]');
        var focalImg = q('[data-image-editor-focal-img]');
        var focalMarker = q('[data-image-editor-focal-marker]');
        var focalStatus = q('[data-image-editor-focal-status]');
        var focalOff = q('[data-image-editor-focal-off]');
        var focalAnnounce = q('[data-image-editor-focal-announce]');
        var aspectField = q('[data-image-editor-aspect]');
        var cropField = q('[data-image-editor-crop]');
        var focalField = q('[data-image-editor-focal]');
        var preview = q('[data-image-editor-preview]');
        var previewBody = q('[data-image-editor-preview-body]');
        var previewUrl = root.getAttribute('data-preview-url');
        if (!form || !mediaId || !chooseButton || !alt || !cropField || !window.MediaPicker) { return; }

        var item = null;
        var cropper = null;
        var previewTimer = null;
        var previewRequest = 0;

        if (root.getAttribute('data-media-file-name') && mediaId.value) {
            item = {
                id: mediaId.value.trim(),
                fileName: root.getAttribute('data-media-file-name'),
                alt: root.getAttribute('data-media-alt') || '',
                thumbUrl: root.getAttribute('data-media-thumb-url'),
                editUrl: root.getAttribute('data-media-edit-url'),
                canCrop: root.getAttribute('data-media-can-crop') === 'true'
            };
        }

        // An id field with an error stays in view, so the error summary link can reach it. The
        // typed crop and focal point stay in their details section, for people who cannot drag.
        var mediaIdHasError = !!fallback.querySelector('.govuk-error-message');
        fallback.hidden = !mediaIdHasError;
        chooseButton.hidden = false;

        function showChosen() {
            chooseButton.textContent = item ? 'Change image' : 'Choose image';
            chosen.hidden = !item;
            tools.hidden = !item;
            if (!item) { return; }
            thumb.src = item.thumbUrl;
            name.textContent = item.fileName;
            noCrop.hidden = item.canCrop;
            cropTools.hidden = !item.canCrop;
        }

        // A preset shape with a crop: the crop is in that shape, so no part of it is cut and the
        // focal point does nothing. A shape with no crop still cuts the image, so focal applies.
        var focalWasOff = null;
        function syncFocalAvailability() {
            var off = !!aspectField.value && !!cropField.value.trim();
            focalToggle.disabled = off;
            if (off) {
                focalToggle.setAttribute('aria-describedby', focalOff.id);
                togglePanel(focalToggle, focalPanel, false);
            } else {
                focalToggle.removeAttribute('aria-describedby');
            }
            focalOff.hidden = !off;
            if (focalWasOff !== null && focalWasOff !== off) {
                focalAnnounce.textContent = off
                    ? 'Focal point is not available while the crop has a fixed shape.'
                    : 'Focal point is available.';
            }
            focalWasOff = off;
        }

        function syncShapeRadios() {
            Array.prototype.forEach.call(shapes, function (radio) { radio.checked = radio.value === aspectField.value; });
        }

        // Writes the crop box as fractions. A box that covers the whole image is no crop at all.
        function writeCrop() {
            if (!cropper) { return; }
            var data = cropper.getData();
            var image = cropper.getImageData();
            if (!image.naturalWidth || !image.naturalHeight) { return; }
            var x = clamp(round(data.x / image.naturalWidth), 0, 1);
            var y = clamp(round(data.y / image.naturalHeight), 0, 1);
            var w = clamp(round(data.width / image.naturalWidth), 0, 1 - x);
            var h = clamp(round(data.height / image.naturalHeight), 0, 1 - y);
            w = round(w);
            h = round(h);
            var whole = x === 0 && y === 0 && w === 1 && h === 1;
            cropField.value = whole || w <= 0 || h <= 0 ? '' : [x, y, w, h].map(number).join(',');
            syncFocalAvailability();
            schedulePreview();
        }

        function startCropper() {
            if (cropper || typeof window.Cropper !== 'function') { return; }
            var existing = parseFractions(cropField.value, 4);
            cropper = new window.Cropper(cropImg, {
                viewMode: 1,
                autoCropArea: 1,
                aspectRatio: PRESETS[aspectField.value] || NaN,
                rotatable: false,
                scalable: false,
                zoomable: false,
                movable: false,
                checkOrientation: false,
                checkCrossOrigin: false,
                background: false,
                ready: function () {
                    var image = cropper.getImageData();
                    if (existing) {
                        cropper.setData({
                            x: existing[0] * image.naturalWidth,
                            y: existing[1] * image.naturalHeight,
                            width: existing[2] * image.naturalWidth,
                            height: existing[3] * image.naturalHeight
                        });
                    }
                    root.setAttribute('data-image-editor-cropper', 'ready');
                },
                cropend: writeCrop
            });
        }

        function stopCropper() {
            if (cropper) { cropper.destroy(); cropper = null; }
            root.removeAttribute('data-image-editor-cropper');
        }

        function togglePanel(toggle, panel, open) {
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
            panel.hidden = !open;
        }

        cropToggle.addEventListener('click', function () {
            var open = cropPanel.hidden;
            togglePanel(cropToggle, cropPanel, open);
            if (!open) { stopCropper(); return; }
            togglePanel(focalToggle, focalPanel, false);
            syncShapeRadios();
            if (cropImg.getAttribute('src') !== item.editUrl) { cropImg.src = item.editUrl; }
            startCropper();
        });

        Array.prototype.forEach.call(shapes, function (radio) {
            radio.addEventListener('change', function () {
                if (!radio.checked) { return; }
                aspectField.value = radio.value;
                if (cropper) {
                    cropper.setAspectRatio(PRESETS[radio.value] || NaN);
                    writeCrop();
                } else {
                    syncFocalAvailability();
                    schedulePreview();
                }
            });
        });

        cropReset.addEventListener('click', function () {
            aspectField.value = '';
            cropField.value = '';
            syncShapeRadios();
            if (cropper) {
                cropper.setAspectRatio(NaN);
                cropper.reset();
            }
            syncFocalAvailability();
            schedulePreview();
        });

        // A typed crop or aspect ratio moves the crop box, and a typed focal point moves the marker.
        function applyTypedCrop() {
            syncShapeRadios();
            syncFocalAvailability();
            if (!cropper) { return; }
            cropper.setAspectRatio(PRESETS[aspectField.value] || NaN);
            var typed = parseFractions(cropField.value, 4);
            var image = cropper.getImageData();
            if (typed && image.naturalWidth) {
                cropper.setData({
                    x: typed[0] * image.naturalWidth,
                    y: typed[1] * image.naturalHeight,
                    width: typed[2] * image.naturalWidth,
                    height: typed[3] * image.naturalHeight
                });
            } else if (!cropField.value.trim()) {
                cropper.reset();
            }
        }
        cropField.addEventListener('change', applyTypedCrop);
        aspectField.addEventListener('change', applyTypedCrop);
        focalField.addEventListener('change', function () { if (!focalPanel.hidden) { showFocal(readFocal()); } });

        function readFocal() {
            var focal = parseFractions(focalField.value, 2);
            return focal ? { x: focal[0], y: focal[1] } : null;
        }

        function showFocal(focal) {
            var shown = focal || { x: 0.5, y: 0.5 };
            focalMarker.style.left = (shown.x * 100) + '%';
            focalMarker.style.top = (shown.y * 100) + '%';
            focalMarker.hidden = !focal;
            focalStatus.textContent = focal
                ? 'Focal point: ' + Math.round(focal.x * 100) + '% across, ' + Math.round(focal.y * 100) + '% down.'
                : 'No focal point set. The middle of the image is kept in view.';
        }

        function writeFocal(x, y) {
            var focal = { x: round(clamp(x, 0, 1)), y: round(clamp(y, 0, 1)) };
            focalField.value = number(focal.x) + ',' + number(focal.y);
            showFocal(focal);
            schedulePreview();
        }

        focalToggle.addEventListener('click', function () {
            var open = focalPanel.hidden;
            togglePanel(focalToggle, focalPanel, open);
            if (!open) { return; }
            togglePanel(cropToggle, cropPanel, false);
            stopCropper();
            if (focalImg.getAttribute('src') !== item.editUrl) { focalImg.src = item.editUrl; }
            showFocal(readFocal());
        });

        focalArea.addEventListener('click', function (event) {
            var box = focalImg.getBoundingClientRect();
            if (!box.width || !box.height) { return; }
            writeFocal((event.clientX - box.left) / box.width, (event.clientY - box.top) / box.height);
        });

        focalArea.addEventListener('keydown', function (event) {
            var moves = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
            var move = moves[event.key];
            if (!move) { return; }
            event.preventDefault();
            var step = event.shiftKey ? 0.1 : 0.01;
            var focal = readFocal() || { x: 0.5, y: 0.5 };
            writeFocal(focal.x + move[0] * step, focal.y + move[1] * step);
        });

        function applyDecorative() {
            if (decorative.checked) {
                alt.value = '';
                alt.readOnly = true;
                alt.setAttribute('aria-disabled', 'true');
                alt.classList.add('app-input--read-only');
            } else {
                alt.readOnly = false;
                alt.removeAttribute('aria-disabled');
                alt.classList.remove('app-input--read-only');
            }
        }
        decorative.addEventListener('change', function () { applyDecorative(); schedulePreview(); });
        applyDecorative();

        function choose(next) {
            var changed = !item || item.id !== next.id;
            item = next;
            mediaId.value = next.id;
            if (!alt.value && !decorative.checked) { alt.value = next.alt || ''; }
            if (changed) {
                cropField.value = '';
                focalField.value = '';
                stopCropper();
                cropImg.removeAttribute('src');
                focalImg.removeAttribute('src');
                togglePanel(cropToggle, cropPanel, false);
                togglePanel(focalToggle, focalPanel, false);
            }
            showChosen();
            syncFocalAvailability();
            schedulePreview();
        }

        chooseButton.addEventListener('click', function () {
            window.MediaPicker.open({ opener: chooseButton, onSelect: choose });
        });

        // The preview is the widget's own view, rendered by the server from the unsaved values.
        function schedulePreview() {
            if (previewTimer) { clearTimeout(previewTimer); }
            previewTimer = setTimeout(refreshPreview, 300);
        }

        function refreshPreview() {
            previewTimer = null;
            if (!previewUrl || !mediaId.value.trim()) { preview.hidden = true; return; }
            var params = new URLSearchParams();
            var data = new FormData(form);
            ['mediaId', 'alt', 'decorative', 'caption', 'credit', 'width', 'align', 'href', 'crop', 'aspect', 'focal'].forEach(function (key) {
                var value = data.get('props[' + key + ']');
                params.set(key, value === null ? '' : value);
            });
            var request = ++previewRequest;
            fetch(previewUrl + '?' + params.toString(), { credentials: 'same-origin', headers: { 'Accept': 'text/html' } })
                // A redirect means the session ended: do not show the sign-in page as a preview.
                .then(function (response) { return response.ok && !response.redirected ? response.text() : null; })
                .then(function (html) {
                    if (request !== previewRequest || html === null) { return; }
                    // Server-rendered markup from the widget's own Razor view, which encodes every value.
                    previewBody.innerHTML = html;
                    preview.hidden = false;
                    root.setAttribute('data-image-editor-preview-ready', String(request));
                })
                .catch(function () { /* The saved preview above the form still shows. */ });
        }

        form.addEventListener('input', schedulePreview);
        form.addEventListener('change', schedulePreview);

        showChosen();
        syncFocalAvailability();
        if (item) { schedulePreview(); }
        root.setAttribute('data-image-editor-init', 'true');
    }

    Array.prototype.forEach.call(editors, setup);
})();
