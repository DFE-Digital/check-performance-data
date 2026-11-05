import type {
    Selection,
} from "k6/html";

import {
    getPage,
} from "../lib/navigation.ts";

import {
    checkPage,
} from "../lib/assertions.ts";

export interface FindDraftReferenceOptions {
    baseUrl: string;
    windowId: string;

    /*
     * Name shown in the amendment request table, for
     * example "Diana Smith".
     */
    pupilName: string;
}

/*
 * Each draft row links to
 * /{windowId}/AmendmentRequests/{reference}/delete.
 */
const DELETE_LINK_PATTERN =
    /\/AmendmentRequests\/([^/?#]+)\/delete/;

/**
 * Finds the reference of the single draft request for
 * a pupil on the amendment request summary page.
 *
 * Throws when no draft, or more than one draft, matches.
 * Refusing to guess avoids deleting a draft created by
 * another run.
 */
export function findDraftReference({
    baseUrl,
    windowId,
    pupilName,
}: FindDraftReferenceOptions): string {

    const page =
        getPage(
            baseUrl,
            `/${windowId}/AmendmentRequests`,
            {
                transaction:
                    "amendment-requests-page",
            }
        );

    checkPage(
        page,
        "Amendment request summary"
    );

    const matchingRows =
        page
            .html()
            .find("form[action$='/AmendmentRequests/bulk-detailed'] tr")
            .filter(
                (
                    _index: number,
                    row: Selection
                ) =>
                    row.find("td").eq(1).text().trim() === pupilName &&
                    row.find("a[href*='/delete']").size() > 0
            );

    if (matchingRows.size() !== 1) {
        throw new Error(
            `Expected one draft request on ${page.url}, ` +
            `found ${matchingRows.size()}`
        );
    }

    const href =
        matchingRows
            .first()
            .find("a[href*='/delete']")
            .first()
            .attr("href") ?? "";

    const match =
        DELETE_LINK_PATTERN.exec(href);

    if (!match) {
        throw new Error(
            "Draft delete link did not contain a " +
            `reference: href=${JSON.stringify(href)}`
        );
    }

    return decodeURIComponent(match[1]);
}
