(function () {
    'use strict';

    // The submissions picker: enable Play only when at least one reference is checked, and wire a
    // "select all on this page" button. Progressive enhancement only — with JS off the checkboxes
    // and the Play submit still work; this just adds the convenience and the disabled-state guard.

    function init() {
        var form = document.querySelector('[data-replay-picker]');
        if (!form) { return; }

        var play = form.querySelector('[data-replay-play]');
        var selectAll = form.querySelector('[data-replay-selectall]');
        var boxes = Array.prototype.slice.call(form.querySelectorAll('[data-replay-checkbox]'));

        function anyChecked() {
            return boxes.some(function (b) { return b.checked; });
        }

        function sync() {
            if (play) { play.disabled = !anyChecked(); }
        }

        boxes.forEach(function (b) { b.addEventListener('change', sync); });

        if (selectAll) {
            selectAll.addEventListener('click', function () {
                var target = !boxes.every(function (b) { return b.checked; });
                boxes.forEach(function (b) { b.checked = target; });
                sync();
            });
        }

        sync();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
