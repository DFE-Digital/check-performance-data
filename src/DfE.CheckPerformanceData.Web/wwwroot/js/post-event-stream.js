(function () {
    'use strict';

    // Reads a server-sent event stream from a POST. EventSource can only GET, and a GET that starts
    // a run lets a crafted link start it for a signed-in admin. So the run starts from a POST that
    // carries the page form's antiforgery token, and this reads the same text/event-stream body.
    //
    // postEventStream(url, form, onEvent, onEnd): onEvent(type, data) for each event, data parsed
    // as JSON; onEnd() once when the request fails or the stream closes. A caller that has seen its
    // terminal event ignores onEnd; any other caller reports a lost connection. Returns false when
    // the browser cannot read a streamed body, so the caller keeps the plain form.

    function supported() {
        return 'fetch' in window && 'ReadableStream' in window && 'TextDecoder' in window;
    }

    function postEventStream(url, form, onEvent, onEnd) {
        if (!supported()) { return false; }

        var ended = false;
        function end() {
            if (ended) { return; }
            ended = true;
            onEnd();
        }

        fetch(url, {
            method: 'POST',
            body: new FormData(form),
            credentials: 'same-origin',
            headers: { 'Accept': 'text/event-stream' }
        }).then(function (response) {
            if (!response.ok || !response.body) { end(); return; }

            var reader = response.body.getReader();
            var decoder = new TextDecoder();
            var buffer = '';

            function pump() {
                return reader.read().then(function (chunk) {
                    if (chunk.done) {
                        buffer += decoder.decode();
                        dispatch(buffer);
                        end();
                        return;
                    }

                    buffer += decoder.decode(chunk.value, { stream: true }).replace(/\r\n?/g, '\n');

                    var boundary;
                    while ((boundary = buffer.indexOf('\n\n')) !== -1) {
                        dispatch(buffer.slice(0, boundary));
                        buffer = buffer.slice(boundary + 2);
                    }

                    return pump();
                });
            }

            return pump();
        }).catch(end);

        // One event block: "event: <type>" and one or more "data: <line>" lines.
        function dispatch(block) {
            if (!block) { return; }

            var type = 'message';
            var data = [];
            var lines = block.split('\n');
            for (var i = 0; i < lines.length; i++) {
                var line = lines[i];
                if (line.indexOf('event:') === 0) {
                    type = line.slice(6).trim();
                } else if (line.indexOf('data:') === 0) {
                    data.push(line.slice(5).replace(/^ /, ''));
                }
            }

            if (data.length === 0) { return; }

            var parsed;
            try { parsed = JSON.parse(data.join('\n')); } catch (err) { return; }
            onEvent(type, parsed);
        }

        return true;
    }

    window.postEventStream = postEventStream;
    window.postEventStream.supported = supported;
})();
