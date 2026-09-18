(function () {
    'use strict';

    var actions = document.getElementById('checking-exercise-actions');
    if (!actions) return;

    var actionableTabs = new Set((actions.dataset.actionableTabs || '').split(',').filter(Boolean));
    var defaultTab = actions.dataset.defaultTab;

    function showFor(tabId) {
        actions.hidden = !actionableTabs.has(tabId);
        actions.querySelectorAll('input[name="selectedExerciseId"]').forEach(function (input) {
            input.value = tabId && tabId.startsWith('exercise-') ? tabId.substring('exercise-'.length) : '';
        });
    }

    function selectedTab() {
        var hash = decodeURIComponent(window.location.hash.substring(1));
        return hash.startsWith('exercise-') ? hash : defaultTab;
    }

    showFor(selectedTab());
    window.addEventListener('hashchange', function () { showFor(selectedTab()); });
    window.addEventListener('popstate', function () { showFor(selectedTab()); });

    document.querySelectorAll('.govuk-tabs__tab[href^="#exercise-"]').forEach(function (tab) {
        tab.addEventListener('click', function () {
            showFor(tab.getAttribute('href').substring(1));
        });
    });
})();
