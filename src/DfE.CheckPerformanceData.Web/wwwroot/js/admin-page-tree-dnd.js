(function () {
    'use strict';

    // =========================================================================
    //  CMS admin page-tree drag & drop
    //  Makes pages in the left nav draggable so they can be reordered among
    //  siblings and moved into other containers. Posts to
    //  /admin/pages/{id}/move-to; on success, reloads the tree so the new
    //  layout appears without any client-side patching.
    // =========================================================================

    var TREE_SELECTOR = '.admin-nav-tree';
    var NODE_SELECTOR = '[data-page-node]';
    var ROOT_SELECTOR = '[data-page-root]';

    // Zones for the drop target: top third = above sibling, bottom third = below sibling,
    // middle third = become last child. On the "Pages" root, the whole hit area is "onto".
    var ZONE_ABOVE = 'above';
    var ZONE_BELOW = 'below';
    var ZONE_ONTO  = 'onto';

    var draggingId = null;

    function attach() {
        var tree = document.querySelector(TREE_SELECTOR);
        if (!tree) return;

        tree.querySelectorAll(NODE_SELECTOR).forEach(function (el) {
            el.setAttribute('draggable', 'true');
            el.addEventListener('dragstart', onDragStart);
            el.addEventListener('dragend', onDragEnd);
            el.addEventListener('dragover', onDragOver);
            el.addEventListener('dragleave', onDragLeave);
            el.addEventListener('drop', onDrop);
        });

        tree.querySelectorAll(ROOT_SELECTOR).forEach(function (el) {
            el.addEventListener('dragover', onRootDragOver);
            el.addEventListener('dragleave', onDragLeave);
            el.addEventListener('drop', onRootDrop);
        });
    }

    function onDragStart(ev) {
        draggingId = ev.currentTarget.getAttribute('data-page-id');
        ev.dataTransfer.effectAllowed = 'move';
        // Some browsers require any data payload to allow drop.
        try { ev.dataTransfer.setData('text/plain', draggingId); } catch (_) {}
        ev.currentTarget.classList.add('cpb-tree-dragging');
    }

    function onDragEnd(ev) {
        draggingId = null;
        ev.currentTarget.classList.remove('cpb-tree-dragging');
        clearAllTargets();
    }

    function computeZone(el, clientY) {
        var rect = el.getBoundingClientRect();
        var offset = clientY - rect.top;
        var third = rect.height / 3;
        if (offset < third) return ZONE_ABOVE;
        if (offset > rect.height - third) return ZONE_BELOW;
        return ZONE_ONTO;
    }

    function onDragOver(ev) {
        if (draggingId == null) return;
        var target = ev.currentTarget;
        if (target.getAttribute('data-page-id') === draggingId) return;
        ev.preventDefault();
        ev.dataTransfer.dropEffect = 'move';

        var zone = computeZone(target, ev.clientY);
        target.classList.remove('cpb-tree-drop-above', 'cpb-tree-drop-below', 'cpb-tree-drop-onto');
        if (zone === ZONE_ABOVE) target.classList.add('cpb-tree-drop-above');
        else if (zone === ZONE_BELOW) target.classList.add('cpb-tree-drop-below');
        else target.classList.add('cpb-tree-drop-onto');
    }

    function onRootDragOver(ev) {
        if (draggingId == null) return;
        ev.preventDefault();
        ev.dataTransfer.dropEffect = 'move';
        ev.currentTarget.classList.add('cpb-tree-drop-onto');
    }

    function onDragLeave(ev) {
        ev.currentTarget.classList.remove(
            'cpb-tree-drop-above', 'cpb-tree-drop-below', 'cpb-tree-drop-onto');
    }

    function clearAllTargets() {
        document.querySelectorAll(
            '.cpb-tree-drop-above, .cpb-tree-drop-below, .cpb-tree-drop-onto'
        ).forEach(function (n) {
            n.classList.remove('cpb-tree-drop-above', 'cpb-tree-drop-below', 'cpb-tree-drop-onto');
        });
    }

    function onDrop(ev) {
        if (draggingId == null) return;
        var target = ev.currentTarget;
        var targetId = target.getAttribute('data-page-id');
        if (targetId === draggingId) return;
        ev.preventDefault();
        var zone = computeZone(target, ev.clientY);
        var move = resolveMove(target, zone);
        if (!move) return;
        submitMove(draggingId, move);
    }

    function onRootDrop(ev) {
        if (draggingId == null) return;
        ev.preventDefault();
        submitMove(draggingId, { NewParentId: null, NewSortOrder: 999999 });
    }

    // Given a target page-node element and a zone, return { NewParentId, NewSortOrder }.
    // Above/below: new parent = target's parent, sort = target's index (± 0/1).
    // Onto:        new parent = target itself, sort = end of children list.
    // Body keys are PascalCase to match the server's [FromBody] MoveNodeRequest binding.
    function resolveMove(target, zone) {
        if (zone === ZONE_ONTO) {
            return { NewParentId: target.getAttribute('data-page-id'), NewSortOrder: 999999 };
        }
        // Walk up the DOM to find the ancestor .tv-item's parent .tv-item (the tree-parent's link).
        var li = target.closest('.tv-item');
        if (!li) return null;
        var parentUl = li.parentElement;             // .tv or .tv-children of an ancestor .tv-item
        var parentLi = parentUl ? parentUl.closest('.tv-item') : null;
        var parentLink = parentLi ? parentLi.querySelector(':scope > .tv-row > [data-page-node], :scope > .tv-row > [data-page-root]') : null;
        var newParentId = parentLink && parentLink.hasAttribute('data-page-id')
            ? parentLink.getAttribute('data-page-id')
            : null;

        // Index of the target among its sibling .tv-item elements.
        var siblings = Array.prototype.slice.call(parentUl.children)
            .filter(function (c) { return c.classList.contains('tv-item'); });
        var idx = siblings.indexOf(li);
        var newSortOrder = zone === ZONE_ABOVE ? idx : idx + 1;
        return { NewParentId: newParentId, NewSortOrder: newSortOrder };
    }

    function getAntiForgery() {
        var form = document.getElementById('cms-tree-antiforgery');
        if (!form) return null;
        var input = form.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : null;
    }

    function submitMove(id, move) {
        var token = getAntiForgery();
        // App configures Antiforgery header name as X-XSRF-TOKEN (see Program.cs).
        var headers = { 'Content-Type': 'application/json' };
        if (token) headers['X-XSRF-TOKEN'] = token;

        fetch('/admin/pages/' + id + '/move-to', {
            method: 'POST',
            headers: headers,
            credentials: 'same-origin',
            body: JSON.stringify(move)
        }).then(function (res) {
            if (res.ok) {
                window.location.reload();
                return;
            }
            return res.json().catch(function () { return { message: 'Move failed.' }; })
                .then(function (payload) {
                    window.alert(payload && payload.message ? payload.message : 'Move failed.');
                });
        }).catch(function () {
            window.alert('Move failed.');
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', attach);
    } else {
        attach();
    }
})();
