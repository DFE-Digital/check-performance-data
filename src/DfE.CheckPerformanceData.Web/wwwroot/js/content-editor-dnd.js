// Vanilla HTML5 drag-and-drop reordering for the content-page inline editor.
//
// Grab a region or widget by its caption (.cpb-drag-handle) to move it.
// While dragging over a .cpb-node the cursor's Y position within that node decides
// whether the drop will land ABOVE (top half) or BELOW (bottom half) it — indicated
// visually by a coloured strip along the top or bottom edge of the target. Dropping
// on an empty column area appends to that column.
//
// Server-side ContentTreeEditor.MoveTo(fromPath, toPath) always inserts BEFORE toPath.
// To land AFTER a node we resolve to the NEXT sibling's path (if any); if the target
// is the last node in its column, we resolve to the column's append path instead.
// No external library dependency.
(function () {
    'use strict';

    var draggingPath = null;
    var currentDropTarget = null;
    // 'above', 'below', or null (column drop).
    var currentDropSide = null;

    function clearHighlight() {
        if (currentDropTarget) {
            currentDropTarget.classList.remove(
                'cpb-drop-target',
                'cpb-drop-target--above',
                'cpb-drop-target--below');
        }
        currentDropTarget = null;
        currentDropSide = null;
    }

    function setHighlight(target, side) {
        if (currentDropTarget !== target || currentDropSide !== side) {
            clearHighlight();
            currentDropTarget = target;
            currentDropSide = side;
            target.classList.add('cpb-drop-target');
            if (side === 'above') target.classList.add('cpb-drop-target--above');
            else if (side === 'below') target.classList.add('cpb-drop-target--below');
        }
    }

    // Given a node target and the pointer Y, decides above/below based on the midpoint.
    function sideFor(node, clientY) {
        var r = node.getBoundingClientRect();
        var midpoint = r.top + r.height / 2;
        return clientY < midpoint ? 'above' : 'below';
    }

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

        var overNode = e.target.closest ? e.target.closest('.cpb-node') : null;
        var overColumn = e.target.closest ? e.target.closest('[data-column-path]') : null;

        // If the nearest column doesn't contain the nearest node, the node is the column's
        // region ancestor (i.e. we're over a region's own — often empty — column area).
        // In that case the drop appends to the column; highlight the column, no above/below.
        if (overColumn && (!overNode || !overColumn.contains(overNode))) {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            setHighlight(overColumn, null);
            return;
        }

        if (!overNode) return;

        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';

        var side = sideFor(overNode, e.clientY);
        setHighlight(overNode, side);
    });

    // ── dragleave ─────────────────────────────────────────────────────────────

    document.addEventListener('dragleave', function (e) {
        if (!currentDropTarget) return;
        // Only clear when the cursor truly leaves the element (not just into a child).
        if (!currentDropTarget.contains(e.relatedTarget)) {
            clearHighlight();
        }
    });

    // ── drop ─────────────────────────────────────────────────────────────────

    document.addEventListener('drop', function (e) {
        var side = currentDropSide;
        var target = currentDropTarget;
        clearHighlight();

        var fromPath = e.dataTransfer.getData('text/plain');
        if (!fromPath) return;

        var toPath = null;
        var droppedOnNode = e.target.closest ? e.target.closest('.cpb-node') : null;
        var droppedOnColumn = e.target.closest ? e.target.closest('[data-column-path]') : null;

        // Column-append path (dropped in an empty area of a column, not over a node).
        if (droppedOnColumn && droppedOnColumn.dataset && droppedOnColumn.dataset.columnPath
            && (!droppedOnNode || !droppedOnColumn.contains(droppedOnNode))) {
            toPath = droppedOnColumn.dataset.columnPath;
        }
        else if (droppedOnNode && droppedOnNode.dataset && droppedOnNode.dataset.nodePath) {
            // Above: MoveTo semantics already insert BEFORE toPath — use the node itself.
            // Below: resolve to the NEXT sibling's path, or the column-append path if last.
            if (side === 'below') {
                var next = droppedOnNode.nextElementSibling;
                // Skip over any inline "Add content here" details between nodes.
                while (next && !next.classList.contains('cpb-node')) {
                    next = next.nextElementSibling;
                }
                if (next && next.dataset && next.dataset.nodePath) {
                    toPath = next.dataset.nodePath;
                } else if (droppedOnColumn && droppedOnColumn.dataset && droppedOnColumn.dataset.columnPath) {
                    toPath = droppedOnColumn.dataset.columnPath;
                } else {
                    toPath = droppedOnNode.dataset.nodePath;
                }
            } else {
                toPath = droppedOnNode.dataset.nodePath;
            }
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
        clearHighlight();
    });
})();
