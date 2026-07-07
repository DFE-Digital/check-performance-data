(function () {
    'use strict';

    // =========================================================================
    //  CMS page-tree right-click context menu
    //  Targets: [data-page-node] (individual page) and [data-page-root] (Pages root)
    //  Only active on admin pages that load this script via _AdminLayout.cshtml.
    // =========================================================================

    var menu = null;

    // Material Symbols Outlined SVG paths — kept in sync with Views/Shared/PageTree/_ActionIcon.cshtml
    // so the icons in the context menu match the icons on the page-tree toolbar rows.
    var ICON_PATHS = {
        add:            'M440-440H200v-80h240v-240h80v240h240v80H520v240h-80v-240Z',
        edit:           'M200-200h57l391-391-57-57-391 391v57Zm-80 80v-170l528-527q12-11 26.5-17t30.5-6q16 0 31 6t26 18l55 56q12 11 17.5 26t5.5 30q0 16-5.5 30.5T817-647L290-120H120Zm640-584-56-56 56 56Zm-141 85-28-29 57 57-29-28Z',
        history:        'M480-120q-138 0-240.5-91.5T122-440h82q14 104 92.5 172T480-200q117 0 198.5-81.5T760-480q0-117-81.5-198.5T480-760q-69 0-129 32t-101 88h110v80H120v-240h80v94q51-64 124.5-99T480-840q75 0 140.5 28.5t114 77q48.5 48.5 77 114T840-480q0 75-28.5 140.5t-77 114q-48.5 48.5-114 77T480-120Zm112-192L440-464v-216h80v184l128 128-56 56Z',
        arrow_upward:   'M440-160v-487L216-423l-56-57 320-320 320 320-56 57-224-224v487h-80Z',
        arrow_downward: 'M440-800v487L216-537l-56 57 320 320 320-320-56-57-224 224V-800h-80Z',
        'delete':       'M280-120q-33 0-56.5-23.5T200-200v-520h-40v-80h200v-40h240v40h200v80h-40v520q0 33-23.5 56.5T680-120H280Zm400-600H280v520h400v-520ZM360-280h80v-360h-80v360Zm160 0h80v-360h-80v360ZM280-720v520-520Z',
        open_in_new:    'M200-120q-33 0-56.5-23.5T120-200v-560q0-33 23.5-56.5T200-840h280v80H200v560h560v-280h80v280q0 33-23.5 56.5T760-120H200Zm188-212-56-56 372-372H560v-80h280v280h-80v-144L388-332Z'
    };

    function makeIcon(iconName) {
        var path = ICON_PATHS[iconName];
        if (!path) return null;
        var svgNs = 'http://www.w3.org/2000/svg';
        var svg = document.createElementNS(svgNs, 'svg');
        svg.setAttribute('xmlns', svgNs);
        svg.setAttribute('viewBox', '0 -960 960 960');
        svg.setAttribute('width', '18');
        svg.setAttribute('height', '18');
        svg.setAttribute('fill', 'currentColor');
        svg.setAttribute('aria-hidden', 'true');
        svg.setAttribute('focusable', 'false');
        var p = document.createElementNS(svgNs, 'path');
        p.setAttribute('d', path);
        svg.appendChild(p);
        return svg;
    }

    function getAntiForgeryToken() {
        var form = document.getElementById('cms-tree-antiforgery');
        if (!form) return '';
        var input = form.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function clearMenu() {
        while (menu.firstChild) {
            menu.removeChild(menu.firstChild);
        }
    }

    function hideMenu() {
        if (menu) {
            menu.hidden = true;
            clearMenu();
        }
    }

    function addItem(label, iconName, action, isDanger) {
        var li = document.createElement('li');
        li.className = 'cms-context-menu__item' + (isDanger ? ' cms-context-menu__item--danger' : '');
        li.setAttribute('role', 'menuitem');

        var btn = document.createElement('button');
        btn.type = 'button';

        var icon = makeIcon(iconName);
        if (icon) btn.appendChild(icon);

        var span = document.createElement('span');
        span.textContent = label;
        btn.appendChild(span);

        btn.addEventListener('click', function () {
            hideMenu();
            action();
        });

        li.appendChild(btn);
        menu.appendChild(li);
    }

    function navigate(url) {
        window.location.href = url;
    }

    function postMove(pageId, direction) {
        var token = getAntiForgeryToken();
        var form = document.createElement('form');
        form.method = 'post';
        form.action = '/admin/pages/' + pageId + '/move';
        form.style.display = 'none';

        var dirInput = document.createElement('input');
        dirInput.type = 'hidden';
        dirInput.name = 'direction';
        dirInput.value = direction;
        form.appendChild(dirInput);

        if (token) {
            var tokenInput = document.createElement('input');
            tokenInput.type = 'hidden';
            tokenInput.name = '__RequestVerificationToken';
            tokenInput.value = token;
            form.appendChild(tokenInput);
        }

        document.body.appendChild(form);
        form.submit();
    }

    function buildMenuForRoot() {
        addItem('New child page', 'add', function () {
            window.open('/admin/pages/new', '_blank', 'noopener');
        });
    }

    function buildMenuForPage(pageId, pagePath) {
        addItem('New child page', 'add', function () {
            window.open('/admin/pages/new?parentId=' + pageId, '_blank', 'noopener');
        });
        addItem('Edit', 'edit', function () {
            window.open('/admin/pages/' + pageId + '/edit', '_blank', 'noopener');
        });
        addItem('Versions', 'history', function () {
            window.open('/admin/pages/' + pageId + '/edit#version-history', '_blank', 'noopener');
        });
        addItem('Move up', 'arrow_upward', function () { postMove(pageId, 'up'); });
        addItem('Move down', 'arrow_downward', function () { postMove(pageId, 'down'); });
        addItem('Delete', 'delete', function () { navigate('/admin/pages/' + pageId + '/delete'); }, true);
        addItem('View', 'open_in_new', function () {
            var path = pagePath.replace(/^\//, '');
            window.open('/' + path, '_blank', 'noopener');
        });
    }

    function positionMenu(x, y) {
        // Show off-screen first to measure
        menu.style.left = '-9999px';
        menu.style.top = '-9999px';
        menu.hidden = false;

        var menuW = menu.offsetWidth;
        var menuH = menu.offsetHeight;
        var vpW = window.innerWidth || document.documentElement.clientWidth;
        var vpH = window.innerHeight || document.documentElement.clientHeight;

        var left = x + window.scrollX;
        var top = y + window.scrollY;

        // Keep on screen (viewport-relative check)
        if (x + menuW > vpW) left = Math.max(0, (x - menuW) + window.scrollX);
        if (y + menuH > vpH) top = Math.max(0, (y - menuH) + window.scrollY);

        menu.style.left = left + 'px';
        menu.style.top = top + 'px';
    }

    function onContextMenu(e) {
        var target = e.target.closest('[data-page-node], [data-page-root]');
        if (!target) return;

        e.preventDefault();

        clearMenu();

        if (target.hasAttribute('data-page-root')) {
            buildMenuForRoot();
        } else {
            var pageId = target.getAttribute('data-page-id');
            var pagePath = target.getAttribute('data-page-path') || '';
            buildMenuForPage(pageId, pagePath);
        }

        positionMenu(e.clientX, e.clientY);

        // Focus first item for keyboard users
        var firstBtn = menu.querySelector('button');
        if (firstBtn) firstBtn.focus();
    }

    function onDismiss(e) {
        if (!menu || menu.hidden) return;
        if (!menu.contains(e.target)) {
            hideMenu();
        }
    }

    function init() {
        if (menu) return; // guard: init only once

        menu = document.createElement('ul');
        menu.className = 'cms-context-menu';
        menu.setAttribute('role', 'menu');
        menu.setAttribute('aria-label', 'Page actions');
        menu.hidden = true;
        document.body.appendChild(menu);

        document.addEventListener('contextmenu', onContextMenu);
        document.addEventListener('click', onDismiss);
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') hideMenu();
        });
        window.addEventListener('scroll', hideMenu, { passive: true });
        window.addEventListener('resize', hideMenu, { passive: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
