import {
    getPage,
    postForm,
} from "../lib/navigation.ts";

import {
    checkPage,
    checkRedirect,
} from "../lib/assertions.ts";

import {
    getAntiforgeryToken,
} from "../lib/antiforgery.ts";

import type {
    SubmitHeadings,
    SubmitRequestOptions,
    SubmittedRequest,
} from "../types/submit-request.ts";

/*
 * Summary and confirmation headings for each journey
 * type. Results enquiries use their own pages.
 */
export const AMENDMENT_SUBMIT_HEADINGS: SubmitHeadings = {
    summary: "Summary of amendment request",
    confirmation: "Data amendment request submitted",
    confirmationPath: "/confirmation",
};

export const RESULTS_ENQUIRY_SUBMIT_HEADINGS: SubmitHeadings = {
    summary: "Summary of result enquiry",
    confirmation: "Results enquiry submitted",
    confirmationPath: "/enquiry-confirmation",
};

export function submitRequest({
    baseUrl,
    summaryLocation,
    headings,
}: SubmitRequestOptions): SubmittedRequest {

    if (!summaryLocation) {
        throw new Error(
            "summaryLocation is required"
        );
    }

    const summaryPage =
        getPage(
            baseUrl,
            summaryLocation,
            {
                transaction:
                    "summary-page",
            }
        );

    checkPage(
        summaryPage,
        headings.summary
    );

    const antiforgeryToken =
        getAntiforgeryToken(
            summaryPage
        );

    const summaryPost =
        postForm(
            baseUrl,
            summaryLocation,
            {
                __RequestVerificationToken:
                    antiforgeryToken,
            },
            {
                transaction:
                    "submit-amendment-request",
            }
        );

    const confirmationLocation =
        checkRedirect(
            summaryPost,
            headings.confirmationPath
        );

    const confirmationPage =
        getPage(
            baseUrl,
            confirmationLocation,
            {
                transaction:
                    "confirmation-page",
            }
        );

    checkPage(
        confirmationPage,
        headings.confirmation
    );

    const referenceNumber =
        confirmationPage
            .html()
            .find("h1 + div strong")
            .text()
            .trim();

    if (!referenceNumber) {
        throw new Error(
            "Request reference number was not found " +
            `on ${confirmationPage.url}`
        );
    }

    return {
        referenceNumber,
        confirmationLocation,
        confirmationPage,
    };
}
