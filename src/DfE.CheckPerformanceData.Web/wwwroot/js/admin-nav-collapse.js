// Collapse / expand the admin left-nav rail on the full-width admin pages (Pipeline
// dashboard, Debug pipeline). The toggle sits between the rail and the content; collapsing
// slides the rail out and lets the content fill the row. State is remembered in
// localStorage and the control is fully keyboard-operable via aria-expanded.
(function () {
  'use strict';

  var STORAGE_KEY = 'admin-nav-collapsed';

  function init() {
    var toggle = document.querySelector('[data-admin-nav-collapse]');
    var root = document.querySelector('[data-admin-nav-collapse-root]');
    if (!toggle || !root) return;

    function apply(collapsed) {
      root.classList.toggle('admin-layout--nav-collapsed', collapsed);
      // aria-expanded reflects the rail's visibility: expanded = rail shown.
      toggle.setAttribute('aria-expanded', String(!collapsed));
      toggle.setAttribute(
        'aria-label',
        collapsed ? 'Expand navigation' : 'Collapse navigation'
      );
      toggle.setAttribute('title', collapsed ? 'Expand navigation' : 'Collapse navigation');
    }

    var stored = null;
    try {
      stored = window.localStorage.getItem(STORAGE_KEY);
    } catch (e) {
      stored = null;
    }
    apply(stored === 'true');

    toggle.addEventListener('click', function () {
      var collapsed = !root.classList.contains('admin-layout--nav-collapsed');
      apply(collapsed);
      try {
        window.localStorage.setItem(STORAGE_KEY, String(collapsed));
      } catch (e) {
        /* storage unavailable (private mode / disabled) — degrade silently */
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
