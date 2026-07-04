(function () {
    'use strict';

    // =========================================================================
    //  CMS admin page-tree drag & drop
    //  Makes pages in the left nav draggable so they can be reordered among
    //  siblings and moved into other containers. Uses a placeholder <li> to
    //  show exactly where the drop will land — siblings shift out of the way
    //  instead of a floating above/below line, which was misleading when the
    //  source and target lived in the same parent.
    //
    //  Event delegation: dragover/drop are bound to the tree root and resolve
    //  the target via ev.target.closest('[data-page-node]'). This catches the
    //  case where the pointer sits over the row's padding / connector span /
    //  toggle button rather than the <a> itself — attaching handlers to just
    //  the <a> missed those spots, which is why the drop looked "dead".
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
    var tree = null;

    function attach() {
        tree = document.querySelector(TREE_SELECTOR);
        if (!tree) return;

        // dragstart / dragend on each page-node link so the browser knows they're the
        // draggables (draggable=true doesn't propagate off HTML defaults for <a>).
        tree.querySelectorAll(NODE_SELECTOR).forEach(function (el) {
            el.setAttribute('draggable', 'true');
            el.addEventListener('dragstart', onDragStart);
            el.addEventListener('dragend', onDragEnd);
        });

        // dragover / drop live at the tree root. Event delegation catches drops on the
        // padding / connectors / toggle spans that surround a link, not just on the
        // <a> itself.
        tree.addEventListener('dragover', onTreeDragOver);
        tree.addEventListener('drop', onTreeDrop);
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
        return placeholder;
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
        try { ev.dataTransfer.setData('text/plain', draggingId); } catch (_) {}
        if (draggingLi) draggingLi.classList.add('cpb-tree-dragging');
    }

    function onDragEnd() {
        if (draggingLi) draggingLi.classList.remove('cpb-tree-dragging');
        removePlaceholder();
        draggingId = null;
        draggingLi = null;
    }

    // Zone under the pointer relative to the target row. Middle third = "onto"
    // (become last child); top/bottom third = insert above / below sibling.
    function computeZone(targetLi, clientY) {
        var row = targetLi.querySelector(':scope > .tv-row') || targetLi;
        var rect = row.getBoundingClientRect();
        var offset = clientY - rect.top;
        var third = rect.height / 3;
        if (offset < third) return ZONE_ABOVE;
        if (offset > rect.height - third) return ZONE_BELOW;
        return ZONE_ONTO;
    }

    // Slide the placeholder <li> into position based on the target and zone.
    function positionPlaceholder(targetLi, zone) {
        var ph = ensurePlaceholder();
        if (zone === ZONE_ABOVE) {
            if (ph.nextSibling !== targetLi || ph.parentNode !== targetLi.parentNode) {
                targetLi.parentNode.insertBefore(ph, targetLi);
            }
        } else if (zone === ZONE_BELOW) {
            if (ph !== targetLi.nextSibling) {
                targetLi.parentNode.insertBefore(ph, targetLi.nextSibling);
            }
        } else {
            var childrenUl = targetLi.querySelector(':scope > ul.tv-children');
            if (!childrenUl) {
                childrenUl = document.createElement('ul');
                childrenUl.className = 'tv tv-children';
                targetLi.appendChild(childrenUl);
            }
            if (ph.parentNode !== childrenUl || ph !== childrenUl.lastChild) {
                childrenUl.appendChild(ph);
            }
        }
    }

    // Find the page-node <a> associated with the element the pointer is over. closest()
    // walks UP the DOM, and the [data-page-node] link is a DESCENDANT of its <li>'s
    // .tv-row, so ev.target.closest('[data-page-node]') returned null whenever the
    // pointer sat on the row wrapper, the connector span, or the toggle button — which
    // is most of the visible surface of a nav row. Look up to the enclosing .tv-item
    // instead and then pluck its direct-child link.
    function resolveHoverTarget(ev) {
        var el = ev.target;
        if (!el || !(el instanceof Element)) return null;

        var ph = el.closest('.cpb-tree-placeholder');
        if (ph) return { kind: 'placeholder' };

        var li = el.closest('.tv-item');
        if (!li) return null;

        var link = li.querySelector(':scope > .tv-row > [data-page-node]');
        if (link) return { kind: 'node', link: link, li: li };

        var root = li.querySelector(':scope > .tv-row > [data-page-root]');
        if (root) return { kind: 'root', link: root, li: li };

        return null;
    }

    function onTreeDragOver(ev) {
        if (draggingId == null) return;
        var hover = resolveHoverTarget(ev);
        if (!hover) return;

        if (hover.kind === 'placeholder') {
            ev.preventDefault();
            ev.dataTransfer.dropEffect = 'move';
            return;
        }

        if (hover.link.getAttribute('data-page-id') === draggingId) return;

        // Reject a drop onto a descendant of the source (would create a cycle).
        if (draggingLi && draggingLi.contains(hover.li)) return;

        ev.preventDefault();
        ev.dataTransfer.dropEffect = 'move';

        if (hover.kind === 'root') {
            // Root drops append to the top-level page tree.
            var rootLi = hover.li;
            var childrenUl = rootLi.querySelector(':scope > ul.tv-children');
            if (!childrenUl) return;
            var ph = ensurePlaceholder();
            if (ph.parentNode !== childrenUl || ph !== childrenUl.lastChild) {
                childrenUl.appendChild(ph);
            }
            return;
        }

        // Regular page-node target.
        var zone = computeZone(hover.li, ev.clientY);
        positionPlaceholder(hover.li, zone);
    }

    function onTreeDrop(ev) {
        if (draggingId == null) return;
        ev.preventDefault();
        // If a placeholder is in the DOM, use its position — that's the drop-truth,
        // and it accounts for the pointer sitting over the placeholder or over
        // whatever it was displacing.
        if (placeholder && placeholder.parentNode) {
            submitFromPlaceholder();
            return;
        }
        // No placeholder — treat as an "onto" drop if the pointer is over a page-node.
        var hover = resolveHoverTarget(ev);
        if (!hover || hover.kind !== 'node') return;
        if (hover.link.getAttribute('data-page-id') === draggingId) return;
        if (draggingLi && draggingLi.contains(hover.li)) return;
        var ontoId = hover.link.getAttribute('data-page-id');
        submitMove(draggingId, { NewParentId: ontoId, NewSortOrder: 999999 });
    }

    // The placeholder already sits in its final position, so its parent = the target
    // parent and the count of real siblings before it = the new sort order.
    function submitFromPlaceholder() {
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

    // Fires when the pointer really leaves the tree. Cleans the placeholder up.
    function onTreeDragLeave(ev) {
        var to = ev.relatedTarget;
        if (!to || !tree.contains(to)) {
            removePlaceholder();
        }
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
                // Land on the moved page so the tree's active-key logic force-expands the
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
