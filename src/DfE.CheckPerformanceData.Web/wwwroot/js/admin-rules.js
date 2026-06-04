// Progressive enhancement for the rules branch editor.
//
// Each leaf condition has a "Field" <select> whose choice determines the valid
// operators and the value editor. Without JS the user picks a field then clicks
// the "Update field" button to re-render the row server-side. With JS we remove
// that step: changing the field auto-submits the same transform, so the correct
// operators and value editor appear immediately.
//
// We reuse the existing fallback button rather than build a new request: clicking
// it submits the form with its name/value/formaction, hitting the exact same
// server path. The button is hidden via CSS (body.js-enabled .rules-condition__apply)
// but a programmatic click still submits.
(function () {
  'use strict';

  function applyFieldChange(select) {
    var group = select.closest('.rules-condition__field');
    if (!group) return;
    var button = group.querySelector('.rules-condition__apply');
    if (button) button.click();
  }

  document.addEventListener('change', function (event) {
    var target = event.target;
    if (target && target.matches && target.matches('select[data-rules-field]')) {
      applyFieldChange(target);
    }
  });
})();
