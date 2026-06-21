// Chart enlarge-to-modal. The dashboard charts are small and their labels can be hard to read, so
// clicking a chart (or its "Enlarge chart" button) opens a scaled-up clone in a native <dialog>.
// Pure progressive enhancement: with no JS the charts and their paired data tables are unchanged
// and fully readable; this only adds the enlarge affordance. The dialog gives Esc-to-close and a
// focus trap for free; a Close button and a backdrop click also dismiss it.
(function () {
  'use strict';

  function ready(fn) {
    if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', fn); }
    else { fn(); }
  }

  function closeDialog(dialog) {
    if (typeof dialog.close === 'function' && dialog.open) { dialog.close(); }
    else { dialog.removeAttribute('open'); }
  }

  // One shared dialog reused for every chart.
  function buildDialog() {
    var dialog = document.createElement('dialog');
    dialog.className = 'obs-chart-modal';
    dialog.setAttribute('data-obs-chart-modal', '');
    dialog.setAttribute('aria-label', 'Enlarged chart');

    var inner = document.createElement('div');
    inner.className = 'obs-chart-modal__inner';

    var bar = document.createElement('div');
    bar.className = 'obs-chart-modal__bar';

    var title = document.createElement('h2');
    title.className = 'govuk-heading-m obs-chart-modal__title';
    title.setAttribute('data-obs-chart-modal-title', '');

    var close = document.createElement('button');
    close.type = 'button';
    close.className = 'govuk-button govuk-button--secondary obs-chart-modal__close';
    close.textContent = 'Close';

    bar.appendChild(title);
    bar.appendChild(close);

    var body = document.createElement('div');
    body.className = 'obs-chart-modal__body';
    body.setAttribute('data-obs-chart-modal-body', '');

    inner.appendChild(bar);
    inner.appendChild(body);
    dialog.appendChild(inner);
    document.body.appendChild(dialog);

    close.addEventListener('click', function () { closeDialog(dialog); });
    // A click on the backdrop (the dialog element itself, outside its inner box) closes it.
    dialog.addEventListener('click', function (e) {
      if (e.target === dialog) { closeDialog(dialog); }
    });

    return dialog;
  }

  function openChart(dialog, panel) {
    var svg = panel.querySelector('svg.obs-chart');
    if (!svg) { return; }
    var heading = panel.querySelector('h3');
    var title = dialog.querySelector('[data-obs-chart-modal-title]');
    var body = dialog.querySelector('[data-obs-chart-modal-body]');

    title.textContent = heading ? heading.textContent.trim() : 'Chart';

    // A scaled clone: drop the fixed width/height so the viewBox lets it fill the dialog width while
    // keeping its aspect ratio, so the labels are large and readable.
    var clone = svg.cloneNode(true);
    clone.removeAttribute('width');
    clone.removeAttribute('height');
    clone.classList.add('obs-chart-modal__svg');

    body.innerHTML = '';
    body.appendChild(clone);

    // Bring the chart's colour legend along so the enlarged view explains its colours too (the
    // small inline legend would otherwise be left behind on the page).
    var legend = panel.querySelector('.obs-legend');
    if (legend) { body.appendChild(legend.cloneNode(true)); }

    if (typeof dialog.showModal === 'function') { dialog.showModal(); }
    else { dialog.setAttribute('open', ''); }
  }

  ready(function () {
    var panels = document.querySelectorAll('.obs-chart-panel');
    if (!panels.length) { return; }
    var dialog = buildDialog();

    Array.prototype.forEach.call(panels, function (panel) {
      var svg = panel.querySelector('svg.obs-chart');
      if (!svg) { return; }

      // A button so enlarging is keyboard- and assistive-tech-reachable, placed under the heading.
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'govuk-button govuk-button--secondary obs-chart-panel__enlarge';
      btn.textContent = 'Enlarge chart';
      btn.addEventListener('click', function () { openChart(dialog, panel); });

      var heading = panel.querySelector('h3');
      if (heading) { heading.insertAdjacentElement('afterend', btn); }
      else { panel.insertBefore(btn, panel.firstChild); }

      // Clicking the chart itself also enlarges it.
      svg.style.cursor = 'zoom-in';
      svg.addEventListener('click', function () { openChart(dialog, panel); });
    });
  });
})();
