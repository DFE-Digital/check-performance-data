(function () {
    'use strict';

    // =========================================================================
    //  CMS admin page-tree drag & drop
    //  Makes pages in the left nav draggable so they can be reordered among
    //  siblings and moved into other containers. Uses a placeholder <li> to
    //  show exactly where the drop will land — siblings shift out of the way
    //  instead of a floating above/below line, which was misleading when the
    //  source and target lived in the same parent.
    // =========================================================================

    var TREE_SELECTOR = '.admin-nav-tree';
    var NODE_SELECTOR = '[data-page-node]';
    var ROOT_SELECTOR = '[data-page-root]';

    var ZONE_ABOVE = 'above';
    var ZONE_BELOW = 'below';
    var ZONE_ONTO  = 'onto';

    var draggingId = null;
    var draggingLi = null;
    var placeholder = null;

    function attach() {
        var tree = document.querySelector(TREE_SELECTOR);
        if (!tree) return;

        tree.querySelectorAll(NODE_SELECTOR).forEach(function (el) {
            el.setAttribute('draggable', 'true');
            el.addEventListener('dragstart', onDragStart);
            el.addEventListener('dragend', onDragEnd);
            el.addEventListener('dragover', onDragOver);
            el.addEventListener('drop', onDrop);
        });

        tree.querySelectorAll(ROOT_SELECTOR).forEach(function (el) {
            el.addEventListener('dragover', onRootDragOver);
            el.addEventListener('drop', onRootDrop);
        });

        // A global drop-cancel on the tree so the placeholder is cleared even when
        // the pointer leaves every registered handler.
        tree.addEventListener('dragleave', onTreeDragLeave);
    }

    function ensurePlaceholder() {
        if (placeholder) return placeholder;
        placeholder = document.createElement('li');
        placeholder.className = 'tv-item cpb-tree-placeholder';
        placeholder.innerHTML =
            '<div class="tv-row">' +
              '<span class="tv-connector"></span>' +
              '<span class="cpb-tree-placeholder__slot" aria-hidden="true"></span>' +
            '</div>';
        // The placeholder is a real <li> inserted at the drop position. Without these two
        // handlers it would steal the dragover / drop events from the underlying target
        // (the pointer sits over the placeholder once it's inserted above/below the target),
        // and dropping would silently fail because no element ever called preventDefault.
        placeholder.addEventListener('dragover', function (ev) {
            if (draggingId == null) return;
            ev.preventDefault();
            ev.dataTransfer.dropEffect = 'move';
        });
        placeholder.addEventListener('drop', onPlaceholderDrop);
        return placeholder;
    }

    // The placeholder already sits in its final position, so its parent = the target parent
    // and the count of real siblings before it = the new sort order.
    function onPlaceholderDrop(ev) {
        if (draggingId == null) return;
        ev.preventDefault();
        var parentUl = placeholder.parentElement;
        if (!parentUl) return;
        var parentLi = parentUl.closest('.tv-item');
        var parentLink = parentLi
            ? parentLi.querySelector(':scope > .tv-row > [data-page-node], :scope > .tv-row > [data-page-root]')
            : null;
        var newParentId = parentLink && parentLink.hasAttribute('data-page-id')
            ? parentLink.getAttribute('data-page-id')
            : null;
        var newSortOrder = 0;
        for (var i = 0; i < parentUl.children.length; i++) {
            var el = parentUl.children[i];
            if (el === placeholder) break;
            if (el.classList.contains('tv-item') && el !== draggingLi) newSortOrder++;
        }
        submitMove(draggingId, { NewParentId: newParentId, NewSortOrder: newSortOrder });
    }

    function removePlaceholder() {
        if (placeholder && placeholder.parentNode) {
            placeholder.parentNode.removeChild(placeholder);
        }
    }

    function onDragStart(ev) {
        draggingId = ev.currentTarget.getAttribute('data-page-id');
        draggingLi = ev.currentTarget.closest('.tv-item');
        ev.dataTransfer.effectAllowed = 'move';
        // Some browsers require any data payload to allow drop.
        try { ev.dataTransfer.setData('text/plain', draggingId); } catch (_) {}
        if (draggingLi) draggingLi.classList.add('cpb-tree-dragging');
    }

    function onDragEnd() {
        if (draggingLi) draggingLi.classList.remove('cpb-tree-dragging');
        removePlaceholder();
        draggingId = null;
        draggingLi = null;
    }

    // Vertical zone under the pointer. The middle third is "onto" — become last child.
    function computeZone(el, clientY) {
        var rect = el.getBoundingClientRect();
        var offset = clientY - rect.top;
        var third = rect.height / 3;
        if (offset < third) return ZONE_ABOVE;
        if (offset > rect.height - third) return ZONE_BELOW;
        return ZONE_ONTO;
    }

    // Move the placeholder <li> into position based on the target and zone. Auto-expands
    // the target's children container when the zone is "onto".
    function positionPlaceholder(targetLi, zone) {
        var ph = ensurePlaceholder();
        if (zone === ZONE_ABOVE) {
            targetLi.parentNode.insertBefore(ph, targetLi);
        } else if (zone === ZONE_BELOW) {
            targetLi.parentNode.insertBefore(ph, targetLi.nextSibling);
        } else {
            // Onto: append to target's children UL (creating one if missing) so the
            // dropped page becomes the last child.
            var childrenUl = targetLi.querySelector(':scope > ul.tv-children');
            if (!childrenUl) {
                childrenUl = document.createElement('ul');
                childrenUl.className = 'tv tv-children';
                targetLi.appendChild(childrenUl);
            }
            childrenUl.appendChild(ph);
        }
    }

    function onDragOver(ev) {
        if (draggingId == null) return;
        var target = ev.currentTarget;
        if (target.getAttribute('data-page-id') === draggingId) return;

        // Don't allow dropping onto a descendant of the source — that would be a cycle.
        var targetLi = target.closest('.tv-item');
        if (draggingLi && draggingLi.contains(targetLi)) return;

        ev.preventDefault();
        ev.dataTransfer.dropEffect = 'move';

        var zone = computeZone(target, ev.clientY);
        positionPlaceholder(targetLi, zone);
    }

    function onRootDragOver(ev) {
        if (draggingId == null) return;
        ev.preventDefault();
        ev.dataTransfer.dropEffect = 'move';
        // Root drops append to the top-level page tree (the root's own <ul> children).
        var rootLi = ev.currentTarget.closest('.tv-item');
        if (!rootLi) return;
        var childrenUl = rootLi.querySelector(':scope > ul.tv-children');
        if (!childrenUl) return;
        var ph = ensurePlaceholder();
        childrenUl.appendChild(ph);
    }

    // When leaving the whole nav tree, drop the placeholder so it doesn't linger.
    function onTreeDragLeave(ev) {
        // Only fire when the pointer really leaves the tree (not just crossing between
        // child elements). Check relatedTarget for containment.
        var to = ev.relatedTarget;
        var tree = ev.currentTarget;
        if (!to || !tree.contains(to)) {
            removePlaceholder();
        }
    }

    function onDrop(ev) {
        if (draggingId == null) return;
        var target = ev.currentTarget;
        var targetId = target.getAttribute('data-page-id');
        if (targetId === draggingId) return;
        var targetLi = target.closest('.tv-item');
        if (draggingLi && draggingLi.contains(targetLi)) return;
        ev.preventDefault();
        var zone = computeZone(target, ev.clientY);
        var move = resolveMove(targetLi, zone);
        if (!move) return;
        submitMove(draggingId, move);
    }

    function onRootDrop(ev) {
        if (draggingId == null) return;
        ev.preventDefault();
        submitMove(draggingId, { NewParentId: null, NewSortOrder: 999999 });
    }

    // Given a target <li> and a zone, return { NewParentId, NewSortOrder }.
    // The sibling index is computed AFTER excluding the source's own <li>, so an
    // "above target" indicator always lands above target — even when the source
    // was already the target's previous sibling in the same parent.
    function resolveMove(targetLi, zone) {
        if (zone === ZONE_ONTO) {
            var targetLink = targetLi.querySelector(':scope > .tv-row > [data-page-node]');
            var ontoId = targetLink && targetLink.getAttribute('data-page-id');
            return { NewParentId: ontoId, NewSortOrder: 999999 };
        }

        var parentUl = targetLi.parentElement;
        var parentLi = parentUl ? parentUl.closest('.tv-item') : null;
        var parentLink = parentLi
            ? parentLi.querySelector(':scope > .tv-row > [data-page-node], :scope > .tv-row > [data-page-root]')
            : null;
        var newParentId = parentLink && parentLink.hasAttribute('data-page-id')
            ? parentLink.getAttribute('data-page-id')
            : null;

        // Sibling list without the source's <li>, so target's index maps directly to the
        // server's newSortOrder.
        var siblings = Array.prototype.slice.call(parentUl.children).filter(function (c) {
            return c.classList.contains('tv-item') && c !== draggingLi && c !== placeholder;
        });
        var idx = siblings.indexOf(targetLi);
        if (idx < 0) return null;
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
                // Navigate to the moved page so the tree's active-key logic force-expands the
                // branch it now lives on and highlights it.
                window.location.href = '/admin/pages/' + id;
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
