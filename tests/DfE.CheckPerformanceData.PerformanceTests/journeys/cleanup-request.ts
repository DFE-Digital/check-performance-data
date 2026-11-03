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
    CleanupRequestOptions,
    CleanupRequestResult,
} from "../types/cleanup-request.ts";

/**
 * Cleans up an amendment request created during the test.
 *
 * Cleanup is needed only so the same pupil can be
 * amended again, because the application allows one
 * amendment request per pupil at a time. Draft requests
 * are deleted; submitted requests are withdrawn by the
 * application. Add Pupil and Results Enquiry journeys
 * are not cleaned up.
 */
export function cleanupRequest({
    baseUrl,
    windowId,
    requestReferenceNumber,
}: CleanupRequestOptions): CleanupRequestResult {
    if (!windowId) {
        throw new Error(
            "windowId is required to clean up a request"
        );
    }

    if (!requestReferenceNumber) {
        throw new Error(
            "requestReferenceNumber is required " +
            "to clean up a request"
        );
    }

    const encodedReferenceNumber =
        encodeURIComponent(
            requestReferenceNumber
        );

    /*
     * Step 1:
     * Open the amendment request summary page.
     */
    const amendmentRequestsLocation =
        `/${windowId}/AmendmentRequests`;

    const amendmentRequestsPage =
        getPage(
            baseUrl,
            amendmentRequestsLocation,
            {
                transaction:
                    "amendment-requests-page",
            }
        );

    checkPage(
        amendmentRequestsPage,
        "Amendment request summary"
    );

    /*
     * Step 2:
     * Open the delete confirmation page.
     *
     * This page contains the delete form and its
     * anti-forgery token.
     */
    const deleteLocation =
        `/${windowId}/AmendmentRequests/` +
        `${encodedReferenceNumber}/delete`;

    const deleteConfirmationPage =
        getPage(
            baseUrl,
            deleteLocation,
            {
                transaction:
                    "cleanup-request-confirmation-page",
            }
        );

    checkPage(
        deleteConfirmationPage,
        "Are you sure you want to delete"
    );

    const antiforgeryToken =
        getAntiforgeryToken(
            deleteConfirmationPage
        );

    /*
     * Step 3:
     * Confirm the application's delete action, which
     * deletes a draft or withdraws a submitted request.
     */
    const cleanupSubmit =
        postForm(
            baseUrl,
            `${deleteLocation}?allEst=False`,
            {
                __RequestVerificationToken:
                    antiforgeryToken,
            },
            {
                transaction:
                    "cleanup-request-submit",
            }
        );

    const redirectLocation =
        checkRedirect(
            cleanupSubmit,
            "/AmendmentRequests"
        );

    /*
     * Step 4:
     * Follow the redirect and verify that the
     * amendment request summary page loads.
     */
    const cleanupResultPage =
        getPage(
            baseUrl,
            redirectLocation,
            {
                transaction:
                    "cleanup-request-result-page",
            }
        );

    checkPage(
        cleanupResultPage,
        "Amendment request summary"
    );

    return {
        redirectLocation,
        response:
            cleanupResultPage,
    };
}