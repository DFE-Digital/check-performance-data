// Preserve the form's establishment/mode generation on same-origin AJAX writes.
(() => {
    const stamp = document.querySelector('meta[name="establishment-context"]')?.content;
    if (!stamp) return;
    const originalFetch = window.fetch;
    window.fetch = function (input, init) {
        const url = new URL(input instanceof Request ? input.url : input, window.location.href);
        if (url.origin !== window.location.origin) return originalFetch.call(this, input, init);
        const headers = new Headers(init?.headers ?? (input instanceof Request ? input.headers : undefined));
        headers.set('X-Establishment-Context', stamp);
        return originalFetch.call(this, input, { ...init, headers });
    };
    const originalOpen = XMLHttpRequest.prototype.open;
    const originalSend = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.open = function (method, url, ...rest) {
        this.cpdSameOrigin = new URL(url, window.location.href).origin === window.location.origin;
        return originalOpen.call(this, method, url, ...rest);
    };
    XMLHttpRequest.prototype.send = function (...args) {
        if (this.cpdSameOrigin) this.setRequestHeader('X-Establishment-Context', stamp);
        return originalSend.apply(this, args);
    };
})();
