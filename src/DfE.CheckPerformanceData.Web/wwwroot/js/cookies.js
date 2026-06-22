(function () {
    'use strict';

    function getCookie(name) {
        var match = document.cookie.match(new RegExp('(?:^|;\\s*)' + name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '=([^;]*)'));
        return match ? decodeURIComponent(match[1]) : null;
    }

    function setCookie(name, value, days) {
        var d = new Date();
        d.setTime(d.getTime() + days * 86400000);
        document.cookie = name + '=' + encodeURIComponent(value) +
            '; expires=' + d.toUTCString() +
            '; path=/' +
            '; SameSite=Lax';
    }

    function getAnalyticsConsent() {
        var raw = getCookie('cookies_policy');
        if (!raw) return null;
        try { return JSON.parse(raw).analytics === true; } catch (e) { return null; }
    }

    function updateGtagConsent(analytics) {
        if (typeof gtag !== 'function') return;
        gtag('consent', 'update', {
            'analytics_storage': analytics ? 'granted' : 'denied',
            'ad_storage': 'denied',
            'ad_user_data': 'denied',
            'ad_personalization': 'denied'
        });
        // Basic consent mode: GTM and Clarity are not loaded until consent is
        // granted. Load them now so first-time accepters get analytics without
        // needing a page reload.
        if (analytics) {
            if (typeof window.loadGtm === 'function') window.loadGtm();
            if (typeof window.loadClarity === 'function') window.loadClarity();
        }
    }

    function saveConsentCookie(analytics) {
        setCookie('cookies_policy', JSON.stringify({ analytics: analytics }), 365);
    }

    // ── Cookie banner ──────────────────────────────────────────────────────────

    var banner = document.querySelector('[data-module="govuk-cookie-banner"]');
    if (banner && getAnalyticsConsent() === null) {
        banner.removeAttribute('hidden');
    }

    if (banner) {
        var initialMsg = banner.querySelector('[data-cookie-banner-message]');
        var acceptedMsg = banner.querySelector('[data-cookie-banner-accepted]');
        var rejectedMsg = banner.querySelector('[data-cookie-banner-rejected]');

        var acceptBtn = banner.querySelector('[data-accept-cookies]');
        var rejectBtn = banner.querySelector('[data-reject-cookies]');

        if (acceptBtn) {
            acceptBtn.addEventListener('click', function () {
                saveConsentCookie(true);
                updateGtagConsent(true);
                if (initialMsg) initialMsg.setAttribute('hidden', '');
                if (acceptedMsg) acceptedMsg.removeAttribute('hidden');
            });
        }

        if (rejectBtn) {
            rejectBtn.addEventListener('click', function () {
                saveConsentCookie(false);
                updateGtagConsent(false);
                if (initialMsg) initialMsg.setAttribute('hidden', '');
                if (rejectedMsg) rejectedMsg.removeAttribute('hidden');
            });
        }

        banner.querySelectorAll('[data-hide-cookie-banner]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                banner.setAttribute('hidden', '');
            });
        });
    }

    // ── Cookies preference page ────────────────────────────────────────────────

    var cookiesForm = document.getElementById('cookies-form');
    if (cookiesForm) {
        cookiesForm.addEventListener('submit', function () {
            var selected = cookiesForm.querySelector('input[name="analytics"]:checked');
            if (!selected) return;
            updateGtagConsent(selected.value === 'true');
        });
    }

})();
