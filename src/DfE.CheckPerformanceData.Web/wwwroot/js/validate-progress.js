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
            summary.classList.remove('govuk-!-display-none');
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
