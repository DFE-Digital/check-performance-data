// Media library images in the rich text editor (TinyMCE).
//
//   RichTextMedia.options(textarea) // tinymce.init options for library images
//   RichTextMedia.setup(editor)     // call from the tinymce.init setup function
//
// - The image dialog's browse button opens the media picker. The chosen image fills the
//   source with its stored file, and the alt text with the library's default alt text.
// - Pasted and dropped images upload to the library (MediaPicker.upload), and the editor
//   then points them at the library file. An upload error shows the server's message.
// - Each library image is saved as <img data-media-id="{id}" src="/media/{id}/...">. The id
//   comes from the src, so it is always right after the editor changes the source.
// - A data: image that the page already had stays as it is. The editor does not upload it.
// - A library image with no alt text, and not marked decorative, shows a warning.
// - A form submit waits for the uploads, so the saved text never holds a pasted copy.
(function () {
    'use strict';

    var MEDIA_SRC = /^(?:https?:\/\/[^\/]+)?\/media\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\//i;
    var NO_ALT = 'This image has no alt text. Select the image, then use Insert/edit image to describe it, or mark it as decorative.';
    var UPLOAD_FAILED = 'The image could not be uploaded. Try again.';

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
            // Root-relative addresses (/media/...), never rewritten relative to the editor page.
            relative_urls: false,
            remove_script_host: true,
            paste_data_images: true,
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
                    callback(item.src, { alt: item.alt || '' });
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

    function setup(editor) {
        // Write the library id from the src on every save, and drop a stale id.
        editor.on('PreInit', function () {
            editor.serializer.addNodeFilter('img', function (nodes) {
                nodes.forEach(function (node) {
                    var match = MEDIA_SRC.exec(node.attr('src') || '');
                    node.attr('data-media-id', match ? match[1].toLowerCase() : null);
                });
            });
        });

        var warned = new WeakSet();
        editor.on('NodeChange', function (e) {
            var img = e.element;
            if (!img || img.nodeName !== 'IMG' || warned.has(img)) { return; }
            if (!MEDIA_SRC.test(img.getAttribute('src') || '')) { return; }
            if (img.getAttribute('role') === 'presentation') { return; }
            if ((img.getAttribute('alt') || '').trim() !== '') { return; }
            warned.add(img);
            editor.notificationManager.open({ text: NO_ALT, type: 'warning', timeout: 8000 });
        });
    }

    // Waits for pasted and dropped images to reach the library before a form posts the text.
    function hasPendingUploads(editor) {
        return !!editor.getBody() && editor.getBody().querySelector('img[src^="blob:"]') !== null;
    }

    document.addEventListener('submit', function (e) {
        if (typeof tinymce === 'undefined' || e.defaultPrevented) { return; }
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

    window.RichTextMedia = { options: options, setup: setup };
})();
