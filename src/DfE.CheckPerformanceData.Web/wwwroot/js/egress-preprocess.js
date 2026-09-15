(function () {
    'use strict';

    // Enhances the egress Preprocessing page (AB#294553). Without JS the form posts and the server
    // runs every step before redirecting. With JS the Run button opens a server-sent stream and
    // each of the eight steps is marked Running / Done / Failed as the server reports it; the
    // terminal event carries the URL to move on to (summary, or the failure list).

    function init() {
        var root = document.querySelector('[data-module="egress-preprocess"]');
        if (!root) { return; }

        var streamUrl = root.getAttribute('data-stream-url');
        if (!streamUrl || !('EventSource' in window)) { return; }

        var form = root.querySelector('[data-egress-form]');
        var startButton = root.querySelector('[data-egress-start]');
        var wrapper = root.querySelector('[data-egress-progress-wrapper]');
        var status = root.querySelector('[data-egress-status]');
        var bar = root.querySelector('[data-egress-progressbar]');
        if (!startButton) { return; }

        startButton.addEventListener('click', function (e) {
            e.preventDefault();
            begin();
        });

        function begin() {
            if (form) { form.classList.add('govuk-!-display-none'); }
            if (wrapper) { wrapper.classList.remove('govuk-!-display-none'); }
            disableLinks();
            setStatus('Starting…');

            var es = new EventSource(streamUrl);
            es.addEventListener('progress', function (event) {
                var data;
                try { data = JSON.parse(event.data); } catch (err) { return; }
                render(data);
                if (data.isComplete) {
                    es.close();
                    if (data.nextUrl) { window.location.href = data.nextUrl; }
                }
            });
            es.onerror = function () {
                setStatus('Connection lost. Refresh the page to see where the run got to.');
                es.close();
            };
        }

        function render(data) {
            var li = root.querySelector('[data-egress-step="' + data.step + '"]');
            if (li) {
                var tag = li.querySelector('[data-egress-step-state]');
                var message = li.querySelector('[data-egress-step-message]');
                if (tag) {
                    tag.className = 'govuk-tag govuk-!-margin-left-2 ' + (data.state === 'done' ? 'govuk-tag--green' : data.state === 'failed' ? 'govuk-tag--red' : 'govuk-tag--blue');
                    tag.textContent = data.state === 'done' ? 'Done' : data.state === 'failed' ? 'Failed' : 'Running';
                }
                if (message && data.state !== 'running') { message.textContent = data.message; }
            }
            if (bar) {
                var completed = data.state === 'running' ? data.step - 1 : data.step;
                bar.setAttribute('aria-valuenow', String(completed));
            }
            setStatus((data.state === 'running' ? data.stepName + '…' : data.message) + ' (' + (data.state === 'running' ? data.step - 1 : data.step) + ' of ' + data.totalSteps + ' steps complete)');
        }

        function disableLinks() {
            var links = root.querySelectorAll('a.govuk-button');
            for (var i = 0; i < links.length; i++) {
                links[i].setAttribute('aria-disabled', 'true');
                links[i].classList.add('govuk-button--disabled');
                links[i].addEventListener('click', function (e) { e.preventDefault(); });
            }
        }

        function setStatus(text) {
            if (status) { status.textContent = text; }
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
