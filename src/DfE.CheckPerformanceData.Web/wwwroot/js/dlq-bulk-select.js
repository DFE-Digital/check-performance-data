(function () {
    'use strict';

    // Dead-letter bulk selection: a select-all checkbox mirrors every row checkbox, the
    // bulk action buttons enable only when at least one row is selected, and on trigger the
    // selected ids are copied as hidden inputs into the matching confirm-modal form (the
    // checkboxes live outside the modal's own <form>, so they must be threaded in).

    var selectAll = document.getElementById('dlq-select-all');
    var rowBoxes = Array.prototype.slice.call(document.querySelectorAll('.dlq-select'));
    var bulkRedrive = document.getElementById('dlq-bulk-redrive-trigger');
    var bulkPurge = document.getElementById('dlq-bulk-purge-trigger');

    function selectedIds() {
        return rowBoxes.filter(function (b) { return b.checked; }).map(function (b) { return b.value; });
    }

    function refreshButtons() {
        var any = selectedIds().length > 0;
        if (bulkRedrive) bulkRedrive.disabled = !any;
        if (bulkPurge) bulkPurge.disabled = !any;
        if (selectAll) {
            var allChecked = rowBoxes.length > 0 && rowBoxes.every(function (b) { return b.checked; });
            selectAll.checked = allChecked;
            selectAll.indeterminate = !allChecked && any;
        }
    }

    if (selectAll) {
        selectAll.addEventListener('change', function () {
            rowBoxes.forEach(function (b) { b.checked = selectAll.checked; });
            refreshButtons();
        });
    }

    rowBoxes.forEach(function (b) {
        b.addEventListener('change', refreshButtons);
    });

    // Populate the bulk modal's form with the current selection before it opens. The modal
    // opener (confirm-modal.js) handles showModal() on the same click; this listener runs
    // first to ensure the form carries the ids the submit will post.
    function wireBulkTrigger(trigger, kind) {
        if (!trigger) return;
        trigger.addEventListener('click', function () {
            var holder = document.querySelector('[data-bulk-ids="' + kind + '"]');
            if (!holder) return;
            holder.innerHTML = '';
            selectedIds().forEach(function (id) {
                var input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'ids';
                input.value = id;
                holder.appendChild(input);
            });
        }, true);
    }

    wireBulkTrigger(bulkRedrive, 'redrive');
    wireBulkTrigger(bulkPurge, 'purge');

    refreshButtons();
})();
