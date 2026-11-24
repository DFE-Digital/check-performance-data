// Media library images in the rich text editor (TinyMCE). Every rich text editor (page widgets,
// wiki pages and content blocks) starts through init, so they all get the same image settings.
//
//   RichTextMedia.init(textarea, config) // tinymce.init with config plus the settings below
//   RichTextMedia.options(textarea)      // tinymce.init options for library images
//   RichTextMedia.setup(editor)          // call from the tinymce.init setup function
//
// init adds the image plugin, and runs config.setup before the library setup. textarea must be
// the element itself, because the stored data: images are read from its value.
//
// When the page has no picker (the editor may not use the library), the editor takes no pasted
// or dropped image, so it never stores a new data: copy.
//
// - The image dialog's browse button opens the media picker. The chosen image fills the
//   source with its stored file, and the alt text with the library's default alt text.
// - Pasted and dropped images upload to the library (MediaPicker.upload), and the editor
//   then points them at the library file. An upload error shows the server's message.
// - Each library image is saved as <img data-media-id="{id}" src="/media/{id}/...">. The id
//   comes from the src, so it is always right after the editor changes the source.
// - A library image with a width and a height also gets data-media-box: "follow" when the box
//   has the shape of the file the editor shows, else "fixed" (the editor changed the shape on
//   purpose). A page shows a replacement of another shape in the box's width with the new shape
//   for "follow", and in the editor's exact box for "fixed".
// - A data: image that the page already had stays as it is. The editor does not upload it.
// - The image dialog can mark an image decorative (an empty alt and role="presentation").
// - A library image with no alt text, and not marked decorative, shows a warning.
// - A form submit waits for the uploads, so the saved text never holds a pasted copy.
(function () {
    'use strict';

    var MEDIA_SRC = /^(?:https?:\/\/[^\/]+)?\/media\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\//i;
    var NO_ALT = 'This image has no alt text. Select the image, then use Insert/edit image to describe it, or mark it as decorative.';
    var UPLOAD_FAILED = 'The image could not be uploaded. Try again.';
    var BOX_MARKER = 'data-media-box';
    // The image dialog rounds the side it works out, so the shape is compared within a pixel.
    var BOX_TOLERANCE = 1.5;

    function picker() { return window.MediaPicker && window.MediaPicker.isAvailable() ? window.MediaPicker : null; }

    // The data: images the page had before this editor opened, from the stored text, by their
    // base64 data. The editor turns every data: image into a pending upload when it loads; these
    // are given back their own address instead, so opening the editor never moves them.
    function existingDataImages(textarea) {
        var found = new Map();
        // A <template> parses without running scripts or loading images.
        var holder = document.createElement('template');
        holder.innerHTML = textarea.value || '';
        holder.content.querySelectorAll('img[src^="data:"]').forEach(function (img) {
            var src = img.getAttribute('src');
            var comma = src.indexOf(',');
            if (comma > 0) { found.set(src.slice(comma + 1), src); }
        });
        return found;
    }

    function options(textarea) {
        var legacy = existingDataImages(textarea);
        var library = picker();
        var result = {
            // Addresses stay as written. Otherwise the editor rewrites /media/... and every link
            // relative to the editor's own page, which breaks them on the published page.
            convert_urls: false,
            // The image dialog's "Image is decorative" option: an empty alt and role="presentation".
            a11y_advanced_options: true,
            paste_data_images: !!library,
            automatic_uploads: !!library,
            images_reuse_filename: false,
            images_file_types: 'png,jpg,jpeg,gif,webp'
        };
        if (!library) { return result; }

        result.file_picker_types = 'image';
        result.file_picker_callback = function (callback) {
            library.open({
                opener: document.activeElement,
                onSelect: function (item) {
                    var meta = { alt: item.alt || '' };
                    if (item.width && item.height) { meta.width = String(item.width); meta.height = String(item.height); }
                    callback(item.src, meta);
                }
            });
        };
        result.images_upload_handler = function (blobInfo) {
            var kept = legacy.get(blobInfo.base64());
            if (kept) { return Promise.resolve(kept); }
            return library.upload(blobInfo.blob() instanceof File
                ? blobInfo.blob()
                : new File([blobInfo.blob()], blobInfo.filename(), { type: blobInfo.blob().type }))
                .then(function (item) {
                    if (item.error) { return Promise.reject({ message: item.error || UPLOAD_FAILED, remove: true }); }
                    return item.src;
                });
        };
        return result;
    }

    // The image dialog's "decorative" option writes an empty alt and role="presentation".
    function isDecorative(img) {
        var role = (img.getAttribute('role') || '').trim().toLowerCase();
        return img.getAttribute('alt') !== null && (role === 'presentation' || role === 'none');
    }

    // A whole number above 0, or null.
    function pixels(value) {
        return /^[0-9]+$/.test(value || '') && Number(value) > 0 ? Number(value) : null;
    }

    // True when a width x height box has the shape of a file of that size: within a pixel of the
    // side that the other side gives, because the image dialog rounds the side it works out. The
    // server uses the same rule (RichTextImageBox).
    function hasShapeOf(width, height, fileWidth, fileHeight) {
        return Math.abs(height - width * fileHeight / fileWidth) <= BOX_TOLERANCE
            || Math.abs(width - height * fileWidth / fileHeight) <= BOX_TOLERANCE;
    }

    // The size of each loaded library image in the editor, by its src.
    function loadedSizes(editor) {
        var sizes = {};
        var body = editor.getBody();
        if (!body) { return sizes; }
        body.querySelectorAll('img').forEach(function (img) {
            if (img.complete && img.naturalWidth > 0 && img.naturalHeight > 0) {
                sizes[img.getAttribute('src')] = { width: img.naturalWidth, height: img.naturalHeight };
            }
        });
        return sizes;
    }

    // The image dialog always saves a width and a height, so the box marker says whether the
    // editor gave the image its own shape ("fixed") or kept the shape of the file it shows
    // ("follow"). The page then shows a replacement of another shape in the right box. When the
    // image has not loaded, its size is not known, and the marker stays as it was.
    function markBox(node, sizes) {
        var width = pixels(node.attr('width'));
        var height = pixels(node.attr('height'));
        if (width === null || height === null) { node.attr(BOX_MARKER, null); return; }
        var file = sizes[node.attr('src')];
        if (!file) { return; }
        node.attr(BOX_MARKER, hasShapeOf(width, height, file.width, file.height) ? 'follow' : 'fixed');
    }

    function setup(editor) {
        // Write the library id from the src on every save, and drop a stale id. Write the box
        // marker of each library image too.
        editor.on('PreInit', function () {
            editor.serializer.addNodeFilter('img', function (nodes) {
                var sizes = null;
                nodes.forEach(function (node) {
                    var match = MEDIA_SRC.exec(node.attr('src') || '');
                    node.attr('data-media-id', match ? match[1].toLowerCase() : null);
                    if (!match) { node.attr(BOX_MARKER, null); return; }
                    sizes = sizes || loadedSizes(editor);
                    markBox(node, sizes);
                });
            });
        });

        var warned = new WeakSet();
        editor.on('NodeChange', function (e) {
            var img = e.element;
            if (!img || img.nodeName !== 'IMG' || warned.has(img)) { return; }
            if (!MEDIA_SRC.test(img.getAttribute('src') || '')) { return; }
            if ((img.getAttribute('alt') || '').trim() !== '') { return; }
            if (isDecorative(img)) { return; }
            warned.add(img);
            editor.notificationManager.open({ text: NO_ALT, type: 'warning', timeout: 8000 });
        });
    }

    // Waits for pasted and dropped images to reach the library before a form posts the text.
    function hasPendingUploads(editor) {
        return !!editor.getBody() && editor.getBody().querySelector('img[src^="blob:"]') !== null;
    }

    document.addEventListener('submit', function (e) {
        // Without the library nothing uploads, so there is nothing to wait for. The editor keeps
        // its stored data: images as blob: copies, and waiting would stop the save for good.
        if (typeof tinymce === 'undefined' || e.defaultPrevented || !picker()) { return; }
        var form = e.target;
        var pending = tinymce.get().filter(function (editor) {
            return form.contains(editor.getElement()) && hasPendingUploads(editor);
        });
        if (pending.length === 0) { return; }
        e.preventDefault();
        var submitter = e.submitter || null;
        Promise.all(pending.map(function (editor) { return editor.uploadImages(); }))
            .then(function () {
                tinymce.triggerSave();
                if (pending.some(hasPendingUploads)) { return; }
                if (form.requestSubmit) { form.requestSubmit(submitter); } else { form.submit(); }
            });
    }, true);

    function init(textarea, config) {
        var own = config.setup;
        var plugins = (config.plugins || '').split(/\s+/).filter(Boolean);
        if (plugins.indexOf('image') < 0) { plugins.push('image'); }
        return tinymce.init(Object.assign({}, config, options(textarea), {
            target: textarea,
            plugins: plugins.join(' '),
            setup: function (editor) {
                if (own) { own(editor); }
                setup(editor);
            }
        }));
    }

    window.RichTextMedia = { options: options, setup: setup, init: init };
})();
