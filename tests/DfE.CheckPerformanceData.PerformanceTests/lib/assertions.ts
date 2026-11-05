import {
    check,
    fail,
} from "k6";

import type {
    Response,
} from "k6/http";

/**
 * Validates an HTML page response.
 *
 * Functional failures stop the current k6 iteration.
 * The response-time assertion is currently also treated
 * as fatal because it is included in the same check group.
 *
 * Failure messages never include rendered text. Headings and
 * error summaries can contain pupil names, and k6 copies
 * failure messages into the captured logs.
 */
export function checkPage(
    response: Response,
    expectedHeading: string
): void {
    const heading =
        response
            .html()
            .find("h1")
            .text()
            .trim();

    const functionalChecksPassed =
        check(response, {
            "Page returned 200":
                (r: Response) =>
                    r.status === 200,

            [`Heading contains "${expectedHeading}"`]:
                () =>
                    heading.includes(
                        expectedHeading
                    ),
        });

    /*
     * Keep the performance assertion separate from
     * functional validation. A slow but functionally
     * correct page should fail the check metric without
     * causing a misleading page-validation exception.
     */
    check(response, {
        "Response under 1 second":
            (r: Response) =>
                r.timings.duration < 1000,
    });

    if (!functionalChecksPassed) {
        fail(
            "Page check failed: " +
            `url=${response.url}, ` +
            `status=${response.status}, ` +
            `expectedHeading=${JSON.stringify(expectedHeading)}`
        );
    }
}

/**
 * Validates a redirect response and returns its Location
 * header for the next journey request.
 *
 * Functional redirect failures stop the current iteration.
 * The response-time check contributes to k6 metrics but
 * does not prevent the journey from following a valid
 * redirect.
 */
export function checkRedirect(
    response: Response,
    expectedLocation: string
): string {
    /*
     * k6 normalises response header values to strings in
     * this use case, but the fallback guarantees that
     * location remains a string for subsequent operations.
     */
    const location =
        response.headers.Location ??
        response.headers.location ??
        "";

    const correctStatus =
        response.status === 302;

    const locationPresent =
        location.length > 0;

    const correctLocation =
        location.includes(
            expectedLocation
        );

    check(response, {
        "Redirect returned 302":
            () => correctStatus,

        "Redirect location present":
            () => locationPresent,

        [`Redirect contains ${expectedLocation}`]:
            () => correctLocation,
    });

    check(response, {
        "Redirect response under 1 second":
            (r: Response) =>
                r.timings.duration < 1000,
    });

    if (
        !correctStatus ||
        !locationPresent ||
        !correctLocation
    ) {
        fail(
            "Redirect check failed: " +
            `url=${response.url}, ` +
            `status=${response.status}, ` +
            `location=${JSON.stringify(location)}, ` +
            `expected=${JSON.stringify(expectedLocation)}`
        );
    }

    return location;
}