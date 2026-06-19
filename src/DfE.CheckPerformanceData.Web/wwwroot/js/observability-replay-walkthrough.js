(function () {
    'use strict';

    // Interactive stage-replay walkthrough. Given a cohort of selected references (each with the
    // ordered board-stage keys it really visited), follow the SAME items across the five pipeline
    // stages: a global step index advances every item one stage along its own path, the items are
    // re-rendered under their current stage, and the stages holding active items are highlighted.
    // Single-step advances one step; Play steps automatically until every item has reached its last
    // stage. No motion library — each step is a discrete re-render, so it is reduced-motion safe.

    var STEP_INTERVAL_MS = 1100;

    function init() {
        var root = document.querySelector('[data-replay-walkthrough]');
        if (!root) { return; }

        var cohort;
        try {
            cohort = JSON.parse(root.getAttribute('data-cohort') || '[]');
        } catch (e) {
            cohort = [];
        }
        if (!cohort.length) { return; }

        var stepBtn = root.querySelector('[data-replay-step]');
        var playBtn = root.querySelector('[data-replay-autoplay]');
        var resetBtn = root.querySelector('[data-replay-reset]');
        var progress = root.querySelector('[data-replay-progress]');

        // The longest path determines how many steps reach the end of the cohort.
        var maxLen = cohort.reduce(function (m, item) {
            var n = (item.stageKeys || []).length;
            return n > m ? n : m;
        }, 0);
        var maxStep = Math.max(0, maxLen - 1);

        var step = 0;
        var timer = null;

        function stageColumnFor(key) {
            return root.querySelector('[data-stage-items="' + key + '"]');
        }

        function clearStages() {
            var lists = root.querySelectorAll('[data-stage-items]');
            Array.prototype.forEach.call(lists, function (ul) { ul.innerHTML = ''; });
            var stages = root.querySelectorAll('.obs-walkthrough__stage');
            Array.prototype.forEach.call(stages, function (li) {
                li.classList.remove('obs-walkthrough__stage--active');
            });
        }

        function render() {
            clearStages();

            cohort.forEach(function (item) {
                var keys = item.stageKeys || [];
                if (!keys.length) { return; }
                // The item sits at the stage for the current step, clamped to its own last stage so
                // a short-journey item parks at its final stage while longer ones keep moving.
                var idx = Math.min(step, keys.length - 1);
                var key = keys[idx];
                var ul = stageColumnFor(key);
                if (!ul) { return; }

                var li = document.createElement('li');
                li.className = 'obs-walkthrough__item obs-walkthrough__item--active';
                var atEnd = idx === keys.length - 1;
                li.textContent = item.reference + (atEnd && item.decision ? ' (' + item.decision + ')' : '');
                ul.appendChild(li);

                var stage = ul.closest('.obs-walkthrough__stage');
                if (stage) { stage.classList.add('obs-walkthrough__stage--active'); }
            });

            if (progress) {
                progress.textContent = 'Step ' + step + ' of ' + maxStep + '.';
            }
        }

        function atEnd() { return step >= maxStep; }

        function advance() {
            if (atEnd()) { stopAuto(); return; }
            step += 1;
            render();
            if (atEnd()) { stopAuto(); }
        }

        function startAuto() {
            if (timer || atEnd()) { return; }
            if (playBtn) { playBtn.setAttribute('aria-pressed', 'true'); playBtn.textContent = 'Pause'; }
            timer = window.setInterval(advance, STEP_INTERVAL_MS);
        }

        function stopAuto() {
            if (timer) { window.clearInterval(timer); timer = null; }
            if (playBtn) { playBtn.setAttribute('aria-pressed', 'false'); playBtn.textContent = 'Play'; }
        }

        function reset() {
            stopAuto();
            step = 0;
            render();
        }

        if (stepBtn) { stepBtn.addEventListener('click', advance); }
        if (resetBtn) { resetBtn.addEventListener('click', reset); }
        if (playBtn) {
            playBtn.addEventListener('click', function () {
                if (timer) { stopAuto(); } else { startAuto(); }
            });
        }

        render();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
