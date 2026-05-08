(function () {
    // Runs synchronously before DOMContentLoaded so GOV.UK tabs init sees the hash.
    // history.replaceState sets the hash without triggering a browser scroll.
    var params = new URLSearchParams(location.search);
    var activeTab = params.get('activeTab');
    if (activeTab) {
        params.delete('activeTab');
        var search = params.toString() ? '?' + params.toString() : '';
        history.replaceState(null, '', location.pathname + search + '#' + activeTab);
    }
})();

(function () {
    'use strict';

    // ====================================================================
    //  Sidebar toggle
    // ====================================================================
    var sidebarToggle = document.getElementById('wiki-sidebar-toggle');
    var sidebar = document.getElementById('wiki-sidebar');
    if (sidebarToggle && sidebar) {
        sidebarToggle.addEventListener('click', function () {
            var collapsed = sidebar.classList.toggle('wiki-sidebar--collapsed');
            sidebarToggle.setAttribute('aria-expanded', String(!collapsed));
        });
    }

    var root = document.querySelector('.tv-root');
    if (!root) return;

    // ====================================================================
    //  Expand / Collapse
    // ====================================================================

    // Auto-collapse branches that don't contain the active page
    root.querySelectorAll('.tv-children').forEach(function (ul) {
        var parentItem = ul.closest('.tv-item');
        if (!parentItem) return;
        var hasActive = parentItem.querySelector('.tv-link-active');
        if (!hasActive) {
            ul.classList.add('tv-collapsed');
            var toggle = parentItem.querySelector(':scope > .tv-row .tv-toggle');
            if (toggle) toggle.setAttribute('aria-expanded', 'false');
        }
    });

    root.addEventListener('click', function (e) {
        var toggle = e.target.closest('.tv-toggle');
        if (!toggle) return;
        e.preventDefault();
        e.stopPropagation();

        var item = toggle.closest('.tv-item');
        if (!item) return;
        var children = item.querySelector(':scope > .tv-children');
        if (!children) return;

        var isExpanded = toggle.getAttribute('aria-expanded') === 'true';
        if (isExpanded) {
            children.classList.add('tv-collapsed');
            toggle.setAttribute('aria-expanded', 'false');
        } else {
            children.classList.remove('tv-collapsed');
            toggle.setAttribute('aria-expanded', 'true');
        }
    });

    // ====================================================================
    //  Drag & Drop  (edit mode only)
    // ====================================================================

    var editMode = root.querySelector('.tv-del') !== null;
    if (!editMode) return;

    var draggedRow = null;
    var dropLine = null;

    function getToken() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function clearDrop() {
        root.querySelectorAll('.tv-drop-into').forEach(function (el) {
            el.classList.remove('tv-drop-into');
        });
        if (dropLine && dropLine.parentNode) {
            dropLine.parentNode.removeChild(dropLine);
        }
        dropLine = null;
        root._dropInfo = null;
    }

    function movePage(pageId, newParentId, newSortOrder) {
        fetch('/help/move', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-XSRF-TOKEN': getToken()
            },
            body: JSON.stringify({ id: pageId, newParentId: newParentId, newSortOrder: newSortOrder })
        }).then(function (r) {
            if (r.ok) {
                return r.json().then(function (data) {
                    var editSuffix = window.location.search.indexOf('edit') >= 0 ? '?edit' : '';
                    window.location.href = '/help/' + data.slugPath + editSuffix;
                });
            } else {
                r.text().then(function (t) { console.error('Move failed:', r.status, t); });
            }
        }).catch(function (err) { console.error('Move error:', err); });
    }

    // Drag start/end
    root.querySelectorAll('.tv-row[data-page-id]').forEach(function (row) {
        row.addEventListener('dragstart', function (e) {
            draggedRow = row;
            row.classList.add('tv-dragging');
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', row.getAttribute('data-page-id'));
        });

        row.addEventListener('dragend', function () {
            if (draggedRow) draggedRow.classList.remove('tv-dragging');
            draggedRow = null;
            clearDrop();
        });
    });

    // Drag over
    root.addEventListener('dragover', function (e) {
        if (!draggedRow) return;
        var targetRow = e.target.closest('.tv-row[data-page-id]');
        if (!targetRow || targetRow === draggedRow) return;

        // Prevent dropping onto own children
        var draggedItem = draggedRow.closest('.tv-item');
        var targetItem = targetRow.closest('.tv-item');
        if (draggedItem && draggedItem.contains(targetItem)) return;

        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';
        clearDrop();

        var rect = targetRow.getBoundingClientRect();
        var y = e.clientY - rect.top;
        var zone = y < rect.height * 0.3 ? 'before' : y > rect.height * 0.7 ? 'after' : 'into';

        if (zone === 'into') {
            targetRow.classList.add('tv-drop-into');
        } else {
            dropLine = document.createElement('div');
            dropLine.className = 'tv-drop-line';
            var item = targetRow.closest('.tv-item');
            if (zone === 'before') {
                item.parentNode.insertBefore(dropLine, item);
            } else {
                item.parentNode.insertBefore(dropLine, item.nextSibling);
            }
        }

        root._dropInfo = { row: targetRow, zone: zone };
    });

    root.addEventListener('dragleave', function (e) {
        if (!root.contains(e.relatedTarget)) clearDrop();
    });

    // Drop
    root.addEventListener('drop', function (e) {
        e.preventDefault();
        if (!draggedRow || !root._dropInfo) return;

        var info = root._dropInfo;
        var targetRow = info.row;
        var zone = info.zone;
        clearDrop();

        var pageId = parseInt(draggedRow.getAttribute('data-page-id'), 10);

        if (zone === 'into') {
            var newParentId = parseInt(targetRow.getAttribute('data-page-id'), 10);
            movePage(pageId, newParentId, 0);
        } else {
            var raw = targetRow.getAttribute('data-parent-id');
            var parentId = raw && raw !== '' ? parseInt(raw, 10) : null;
            var targetSort = parseInt(targetRow.getAttribute('data-sort'), 10) || 0;
            movePage(pageId, parentId, zone === 'before' ? targetSort : targetSort + 1);
        }
    });

})();
