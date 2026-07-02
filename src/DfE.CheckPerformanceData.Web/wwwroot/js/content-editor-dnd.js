// Vanilla HTML5 drag-and-drop reordering for the content-page inline editor.
// Grab a region or widget by its caption (.cpb-drag-handle) to move it.
// Drop onto a .cpb-node to insert before it; drop onto an empty column area
// ([data-column-path]) to append.  Submits the move via POST to /content/move-to,
// which calls ContentTreeEditor.MoveTo on the server and redirects back to /edit.
// No external library dependency.
(function () {
    'use strict';

    var draggingPath = null;
    var currentDropTarget = null;

    // ── dragstart ─────────────────────────────────────────────────────────────

    document.addEventListener('dragstart', function (e) {
        var handle = e.target.closest ? e.target.closest('.cpb-drag-handle') : null;
        if (!handle) return;
        var node = handle.closest('.cpb-node');
        if (!node) return;
        var path = node.dataset && node.dataset.nodePath;
        if (!path) return;

        draggingPath = path;
        e.dataTransfer.setData('text/plain', path);
        e.dataTransfer.effectAllowed = 'move';
        node.classList.add('cpb-dragging');
    });

    // ── dragover ──────────────────────────────────────────────────────────────

    document.addEventListener('dragover', function (e) {
        if (!draggingPath) return;
        // Prefer .cpb-node over the column container for precise drop targeting.
        var target = (e.target.closest ? e.target.closest('.cpb-node') : null)
            || (e.target.closest ? e.target.closest('[data-column-path]') : null);
        if (!target) return;

        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';

        if (currentDropTarget !== target) {
            if (currentDropTarget) currentDropTarget.classList.remove('cpb-drop-target');
            currentDropTarget = target;
            target.classList.add('cpb-drop-target');
        }
    });

    // ── dragleave ─────────────────────────────────────────────────────────────

    document.addEventListener('dragleave', function (e) {
        if (!currentDropTarget) return;
        // Only clear when the cursor truly leaves the element (not just into a child).
        if (!currentDropTarget.contains(e.relatedTarget)) {
            currentDropTarget.classList.remove('cpb-drop-target');
            currentDropTarget = null;
        }
    });

    // ── drop ─────────────────────────────────────────────────────────────────

    document.addEventListener('drop', function (e) {
        if (currentDropTarget) {
            currentDropTarget.classList.remove('cpb-drop-target');
            currentDropTarget = null;
        }

        var fromPath = e.dataTransfer.getData('text/plain');
        if (!fromPath) return;

        var toPath = null;
        var droppedOnNode = e.target.closest ? e.target.closest('.cpb-node') : null;
        var droppedOnColumn = e.target.closest ? e.target.closest('[data-column-path]') : null;

        if (droppedOnNode && droppedOnNode.dataset && droppedOnNode.dataset.nodePath) {
            toPath = droppedOnNode.dataset.nodePath;
        } else if (droppedOnColumn && droppedOnColumn.dataset && droppedOnColumn.dataset.columnPath) {
            toPath = droppedOnColumn.dataset.columnPath;
        }

        if (!toPath) return;
        if (fromPath === toPath) return;
        // Guard: block dropping a region into its own subtree (toPath starts with fromPath + '-').
        if (toPath.indexOf(fromPath + '-') === 0) return;

        e.preventDefault();

        // The antiforgery token is already on the page in existing forms.
        var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        if (!tokenInput) return;

        // The node ID is embedded by Edit.cshtml on the editor root element.
        var editorRoot = document.querySelector('[data-cpb-node-id]');
        if (!editorRoot || !editorRoot.dataset.cpbNodeId) return;
        var nodeId = editorRoot.dataset.cpbNodeId;

        var form = document.createElement('form');
        form.method = 'post';
        form.action = '/admin/pages/' + nodeId + '/content/move-to';
        form.style.display = 'none';

        var csrf = document.createElement('input');
        csrf.type = 'hidden';
        csrf.name = '__RequestVerificationToken';
        csrf.value = tokenInput.value;
        form.appendChild(csrf);

        var fp = document.createElement('input');
        fp.type = 'hidden';
        fp.name = 'fromPath';
        fp.value = fromPath;
        form.appendChild(fp);

        var tp = document.createElement('input');
        tp.type = 'hidden';
        tp.name = 'toPath';
        tp.value = toPath;
        form.appendChild(tp);

        document.body.appendChild(form);
        form.submit();
    });

    // ── dragend ───────────────────────────────────────────────────────────────

    document.addEventListener('dragend', function () {
        draggingPath = null;
        document.querySelectorAll('.cpb-dragging').forEach(function (el) {
            el.classList.remove('cpb-dragging');
        });
        if (currentDropTarget) {
            currentDropTarget.classList.remove('cpb-drop-target');
            currentDropTarget = null;
        }
    });
})();
