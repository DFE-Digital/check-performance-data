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
    WindowProfile,
} from "../types/window.ts";

/**
 * Starts a results enquiry.
 *
 * GET  Check Your Pupil Data
 * POST Results Enquiry as the next step
 *
 * Returns the location of the issue question page.
 * That page is the first pre-search step.
 */
export function startResultsEnquiry(
    baseUrl: string,
    profile: WindowProfile
): string {

    const windowId =
        profile.windowId;

    const pupilDataPage =
        getPage(
            baseUrl,
            `/CheckYourPupilData/${windowId}`,
            {
                transaction:
                    "enquiry-check-pupil-data",

                amendment_type:
                    "ResultsEnquiry",
            }
        );

    checkPage(
        pupilDataPage,
        profile.checkPupilDataHeading
    );

    const nextStepPost =
        postForm(
            baseUrl,
            `/CheckYourPupilData/${windowId}/nextstep`,
            {
                __RequestVerificationToken:
                    getAntiforgeryToken(
                        pupilDataPage
                    ),

                SelectedNextStep:
                    "ResultsEnquiry",
            },
            {
                transaction:
                    "enquiry-submit-next-step",

                amendment_type:
                    "ResultsEnquiry",
            }
        );

    return checkRedirect(
        nextStepPost,
        `/${windowId}/ResultIssue`
    );
}
