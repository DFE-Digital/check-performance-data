(function () {
    'use strict';

    // =========================================================================
    //  CMS page-tree right-click context menu
    //  Targets: [data-page-node] (individual page) and [data-page-root] (Pages root)
    //  Only active on admin pages that load this script via _AdminLayout.cshtml.
    // =========================================================================

    var menu = null;

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

    function addItem(label, action, isDanger) {
        var li = document.createElement('li');
        li.className = 'cms-context-menu__item' + (isDanger ? ' cms-context-menu__item--danger' : '');
        li.setAttribute('role', 'menuitem');

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.textContent = label;
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
        addItem('New child page', function () { navigate('/admin/pages/new'); });
    }

    function buildMenuForPage(pageId, pagePath, hasLive) {
        addItem('New child page', function () { navigate('/admin/pages/new?parentId=' + pageId); });
        addItem('Edit', function () { navigate('/admin/pages/' + pageId + '/edit'); });
        addItem('Versions', function () { navigate('/admin/pages/' + pageId + '/versions'); });
        addItem('Move up', function () { postMove(pageId, 'up'); });
        addItem('Move down', function () { postMove(pageId, 'down'); });
        addItem('Delete', function () { navigate('/admin/pages/' + pageId + '/delete'); }, true);
        if (hasLive) {
            addItem('View', function () {
                var path = pagePath.replace(/^\//, '');
                window.open('/' + path, '_blank', 'noopener');
            });
        }
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
            var hasLive = target.getAttribute('data-page-has-live') === 'true';
            buildMenuForPage(pageId, pagePath, hasLive);
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
