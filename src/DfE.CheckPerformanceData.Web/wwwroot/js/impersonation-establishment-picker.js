(() => {
    const picker = document.getElementById('establishment-picker');
    if (!picker) return;

    const searchUrl = picker.dataset.searchUrl;
    let suggestions = [];
    let activeRequest;
    let selectedLabel = '';

    function setField(id, value) {
        const field = document.getElementById(id);
        if (field) field.value = value ?? '';
    }

    function clearFields() {
        setField('Laestab', '');
        setField('Urn', '');
        setField('LowestAge', '');
        setField('HighestAge', '');
    }

    accessibleAutocomplete({
        element: document.querySelector('#establishment-search-container'),
        id: 'establishment-search',
        name: 'establishmentSearch',
        displayMenu: 'overlay',
        minLength: 2,
        confirmOnBlur: false,
        inputClasses: 'govuk-input',
        tAssistiveHint: function () {
            return 'Search by establishment name or URN. Enter at least 2 characters. ' +
                'When results are available, use the up and down arrows to review them and Enter to select one.';
        },
        tNoResults: function () { return 'No establishments found'; },
        source: function (query, populateResults) {
            activeRequest?.abort();
            activeRequest = new AbortController();
            const url = new URL(searchUrl, window.location.origin);
            url.searchParams.set('query', query);

            fetch(url, {
                headers: { Accept: 'application/json' },
                signal: activeRequest.signal
            })
                .then((response) => {
                    if (!response.ok) throw new Error('Search request failed');
                    return response.json();
                })
                .then((establishments) => {
                    suggestions = establishments.map((establishment) => {
                        const identifiers = [
                            establishment.urn ? `URN ${establishment.urn}` : null,
                            establishment.laestab ? `LAESTAB ${establishment.laestab}` : null
                        ].filter(Boolean).join(' · ');

                        return {
                            ...establishment,
                            label: identifiers ? `${establishment.name} — ${identifiers}` : establishment.name
                        };
                    });
                    populateResults(suggestions.map((establishment) => establishment.label));
                })
                .catch((error) => {
                    if (error.name === 'AbortError') return;
                    suggestions = [];
                    populateResults([]);
                });
        },
        onConfirm: function (label) {
            const establishment = suggestions.find((item) => item.label === label);
            if (!establishment) {
                selectedLabel = '';
                clearFields();
                return;
            }

            selectedLabel = label;
            setField('Laestab', establishment.laestab);
            setField('Urn', establishment.urn);
            setField('LowestAge', establishment.lowestAge);
            setField('HighestAge', establishment.highestAge);
        }
    });

    document.getElementById('establishment-search').addEventListener('input', (event) => {
        if (selectedLabel && event.target.value !== selectedLabel) {
            selectedLabel = '';
            clearFields();
        }
    });
})();
