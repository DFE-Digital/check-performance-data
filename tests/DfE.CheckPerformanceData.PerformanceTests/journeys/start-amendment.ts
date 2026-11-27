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
    AmendmentType,
} from "../types/amendment.ts";

import type {
    StartedAmendment,
} from "../types/start-amendment.ts";

import type {
    WindowProfile,
} from "../types/window.ts";

/**
 * Starts a pupil-data amendment journey.
 *
 * Common behaviour:
 *
 * GET  Check Your Pupil Data
 * POST Request Change
 * GET  What To Change
 * POST Amendment Type
 *
 * A profile with no whatToChangeHeading is a window that
 * auto-starts its only change type (KS2). Its What To
 * Change GET must redirect to the journey. The POST is
 * then skipped. Any other profile must return the page.
 *
 * Returns the first configured page
 * of the selected journey.
 */
export function startAmendment(
    baseUrl: string,
    profile: WindowProfile,
    amendmentType: AmendmentType
): StartedAmendment {

    const windowId =
        profile.windowId;

    const pupilDataPage =
        getPage(
            baseUrl,
            `/CheckYourPupilData/${windowId}`,
            {
                transaction:
                    "check-pupil-data",

                amendment_type:
                    amendmentType,
            }
        );

    checkPage(
        pupilDataPage,
        profile.checkPupilDataHeading
    );

    let token =
        getAntiforgeryToken(
            pupilDataPage
        );

    const nextStepPost =
        postForm(
            baseUrl,
            `/CheckYourPupilData/${windowId}/nextstep`,
            {
                __RequestVerificationToken:
                    token,

                SelectedNextStep:
                    "RequestChange",
            },
            {
                transaction:
                    "submit-request-amendment",

                amendment_type:
                    amendmentType,
            }
        );

    const whatToChangeLocation =
        checkRedirect(
            nextStepPost,
            "/WhatToChange/"
        );

    const whatToChangePage =
        getPage(
            baseUrl,
            whatToChangeLocation,
            {
                transaction:
                    "what-to-change",

                amendment_type:
                    amendmentType,
            }
        );

    if (!profile.whatToChangeHeading) {
        return {
            location:
                checkRedirect(
                    whatToChangePage,
                    "/Journey/"
                ),

            amendmentType,
        };
    }

    checkPage(
        whatToChangePage,
        profile.whatToChangeHeading
    );

    token =
        getAntiforgeryToken(
            whatToChangePage
        );

    const whatToChangePost =
        postForm(
            baseUrl,
            whatToChangeLocation,
            {
                __RequestVerificationToken:
                    token,

                SelectedWhatToChange:
                    amendmentType,
            },
            {
                transaction:
                    "select-amendment-type",

                amendment_type:
                    amendmentType,
            }
        );

    const firstPageLocation =
        checkRedirect(
            whatToChangePost,
            "/Journey/"
        );

    return {
        location:
            firstPageLocation,

        amendmentType,
    };
}