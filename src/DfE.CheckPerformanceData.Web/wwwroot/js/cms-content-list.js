// Progressive enhancement for the content-staging selection page: a text filter across both
// tables, per-table select-all (honouring the current filter), and click-to-sort columns.
// Without JS the tables still render in their default order and remain fully selectable.
(function () {
    function ready(fn) {
        if (document.readyState !== 'loading') { fn(); }
        else { document.addEventListener('DOMContentLoaded', fn); }
    }

    function cellSortValue(cell) {
        var v = cell.getAttribute('data-sort-value');
        return v != null ? v.toLowerCase() : cell.textContent.trim().toLowerCase();
    }

    ready(function () {
        var filter = document.getElementById('cs-filter');
        if (filter) {
            filter.addEventListener('input', function () {
                var q = filter.value.trim().toLowerCase();
                document.querySelectorAll('tr[data-filter]').forEach(function (row) {
                    var hay = row.getAttribute('data-filter') || '';
                    row.style.display = (!q || hay.indexOf(q) !== -1) ? '' : 'none';
                });
            });
        }

        document.querySelectorAll('[data-select-all]').forEach(function (master) {
            master.addEventListener('change', function () {
                var table = document.getElementById(master.getAttribute('data-select-all'));
                if (!table) { return; }
                table.querySelectorAll('tbody tr').forEach(function (row) {
                    if (row.style.display === 'none') { return; }
                    var cb = row.querySelector('input[type=checkbox]');
                    if (cb) { cb.checked = master.checked; }
                });
            });
        });

        document.querySelectorAll('th[data-sort]').forEach(function (th) {
            th.style.cursor = 'pointer';
            th.addEventListener('click', function () {
                var table = th.closest('table');
                var headerCells = th.parentNode.children;
                var idx = Array.prototype.indexOf.call(headerCells, th);
                var ascending = th.getAttribute('aria-sort') !== 'ascending';
                var tbody = table.tBodies[0];
                var rows = Array.prototype.slice.call(tbody.rows);

                rows.sort(function (a, b) {
                    var av = cellSortValue(a.cells[idx]);
                    var bv = cellSortValue(b.cells[idx]);
                    if (av < bv) { return ascending ? -1 : 1; }
                    if (av > bv) { return ascending ? 1 : -1; }
                    return 0;
                });
                rows.forEach(function (r) { tbody.appendChild(r); });

                table.querySelectorAll('th[data-sort]').forEach(function (h) { h.removeAttribute('aria-sort'); });
                th.setAttribute('aria-sort', ascending ? 'ascending' : 'descending');
            });
        });
    });
})();
