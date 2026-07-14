(function () {
    'use strict';

    // Live validation progress. Enhances the Validate window page: instead of a plain POST that
    // blocks until the whole file is processed, the Run button opens a server-sent stream and the
    // seven processing steps (start checks -> checksums -> record count -> per-file progress ->
    // summary) are shown as they happen. Without JS (or EventSource) the plain form still posts to
    // the fallback action and renders the same summary server-side.

    function init() {
        var root = document.querySelector('[data-module="validate-progress"]');
        if (!root) { return; }

        var streamUrl = root.getAttribute('data-stream-url');
        if (!streamUrl || !('EventSource' in window)) { return; }

        var form = root.querySelector('[data-validate-form]');
        var startButton = root.querySelector('[data-validate-start]');
        var statusWrapper = root.querySelector('[data-validate-status-wrapper]');
        var statusLine = root.querySelector('[data-validate-status]');
        var summary = root.querySelector('[data-validate-summary]');

        var fields = {
            recordsRead: root.querySelector('[data-validate-records-read]'),
            recordsProcessed: root.querySelector('[data-validate-records-processed]'),
            filesWritten: root.querySelector('[data-validate-files-written]'),
            errorCount: root.querySelector('[data-validate-errors]')
        };

        if (!startButton) { return; }

        startButton.addEventListener('click', function (e) {
            e.preventDefault();
            begin();
        });

        function begin() {
            // Step 1: show that checks are starting and disable the other actions on the page so the
            // run can't be triggered twice or navigated away from mid-process.
            disableOtherButtons();
            if (form) { form.classList.add('govuk-!-display-none'); }
            if (statusWrapper) { statusWrapper.classList.remove('govuk-!-display-none'); }
            setStatus('Starting checks…');

            var es = new EventSource(streamUrl);

            es.addEventListener('progress', function (event) {
                var data;
                try { data = JSON.parse(event.data); } catch (err) { return; }

                render(data);

                if (data.isComplete) {
                    es.close();
                    showSummary(data);
                }
            });

            es.onerror = function () {
                // EventSource retries on its own; only surface a message if we never finished.
                setStatus('Connection lost. Refresh the page to try again.');
                es.close();
            };
        }

        function render(data) {
            setText(fields.recordsRead, data.recordsRead);
            setText(fields.recordsProcessed, data.recordsProcessed);
            setText(fields.filesWritten, data.filesWritten);
            setText(fields.errorCount, data.errorCount);
            setStatus(data.message);
        }

        function showSummary(data) {
            if (!summary) { return; }

            var title = data.isError ? 'Validation failed' : 'Validation complete';
            var panel = document.createElement('div');
            panel.className = 'govuk-panel govuk-panel--confirmation';
            if (data.isError) {
                panel.style.background = '#d4351c';
            }

            var heading = document.createElement('h2');
            heading.className = 'govuk-panel__title';
            heading.textContent = title;

            var body = document.createElement('div');
            body.className = 'govuk-panel__body';
            body.textContent = data.message;

            panel.appendChild(heading);
            panel.appendChild(body);

            summary.innerHTML = '';
            summary.appendChild(panel);

            if (data.schoolSummary && data.schoolSummary.length) {
                summary.appendChild(buildSummaryTable(data.schoolSummary));
            }

            summary.classList.remove('govuk-!-display-none');
        }

        // Records-processed-per-LAESTAB table with a totals footer, mirroring the server-rendered
        // no-JS summary.
        function buildSummaryTable(schoolSummary) {
            var table = document.createElement('table');
            table.className = 'govuk-table';

            var caption = document.createElement('caption');
            caption.className = 'govuk-table__caption govuk-table__caption--m';
            caption.textContent = 'Records processed per LAESTAB';
            table.appendChild(caption);

            var thead = document.createElement('thead');
            thead.className = 'govuk-table__head';
            thead.appendChild(row('th', [
                { text: 'LAESTAB', className: 'govuk-table__header' },
                { text: 'Records processed', className: 'govuk-table__header govuk-table__header--numeric' }
            ]));
            table.appendChild(thead);

            var tbody = document.createElement('tbody');
            tbody.className = 'govuk-table__body';
            var total = 0;
            for (var i = 0; i < schoolSummary.length; i++) {
                var school = schoolSummary[i];
                total += school.recordCount;
                tbody.appendChild(row('td', [
                    { text: school.laestab, className: 'govuk-table__cell' },
                    { text: school.recordCount, className: 'govuk-table__cell govuk-table__cell--numeric' }
                ]));
            }
            table.appendChild(tbody);

            var tfoot = document.createElement('tfoot');
            tfoot.className = 'govuk-table__foot';
            tfoot.appendChild(row('td', [
                { text: 'Total LAESTAB: ' + schoolSummary.length, className: 'govuk-table__header' },
                { text: total, className: 'govuk-table__cell govuk-table__cell--numeric' }
            ]));
            table.appendChild(tfoot);

            return table;
        }

        function row(cellTag, cells) {
            var tr = document.createElement('tr');
            tr.className = 'govuk-table__row';
            for (var i = 0; i < cells.length; i++) {
                var cell = document.createElement(cellTag);
                cell.className = cells[i].className;
                cell.textContent = cells[i].text;
                tr.appendChild(cell);
            }
            return tr;
        }

        function disableOtherButtons() {
            var buttons = document.querySelectorAll('.govuk-button, button');
            for (var i = 0; i < buttons.length; i++) {
                var button = buttons[i];
                button.setAttribute('disabled', 'disabled');
                button.setAttribute('aria-disabled', 'true');
                button.classList.add('govuk-button--disabled');
            }
        }

        function setStatus(message) {
            if (statusLine && typeof message === 'string') { statusLine.textContent = message; }
        }

        function setText(el, value) {
            if (el && value !== undefined && value !== null) { el.textContent = value; }
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
