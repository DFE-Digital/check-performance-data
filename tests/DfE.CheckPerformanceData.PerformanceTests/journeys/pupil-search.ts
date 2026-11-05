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
    PupilSearchConfig,
    SecondPupilSearchConfig,
} from "../types/journey-config.ts";

import type {
    Pupil,
} from "../types/pupil.ts";

export interface PupilSearchStart {
    location: string;
    antiforgeryToken: string;
}

export interface OpenPupilSearchOptions {
    baseUrl: string;
    amendmentType: AmendmentType;

    /*
     * Location of the journey's first page, as returned
     * by startAmendment().
     */
    location: string;

    config: PupilSearchConfig;
}

export interface SelectPupilOptions {
    baseUrl: string;
    amendmentType: AmendmentType;
    location: string;
    antiforgeryToken: string;
    pupil: Pupil;
    config: PupilSearchConfig;
}

/*
 * Transaction names keep the existing per-journey
 * prefixes, for example "remove-select-pupil".
 */
function transactionPrefix(
    amendmentType: AmendmentType
): string {
    return amendmentType.toLowerCase();
}

/**
 * Opens the pupil-search page for a journey.
 *
 * Expected navigation:
 *
 * GET /Journey/{windowId}/page/{pageId}
 *   -> /Journey/{windowId}/pupil-search/{pageId}
 *
 * GET /Journey/{windowId}/pupil-search/{pageId}
 *   -> 200 with the configured heading
 */
export function openPupilSearch({
    baseUrl,
    amendmentType,
    location,
    config,
}: OpenPupilSearchOptions): PupilSearchStart {

    const prefix =
        transactionPrefix(amendmentType);

    if (
        !location.includes(
            `/page/${config.pageId}`
        )
    ) {
        throw new Error(
            `${amendmentType} journey did not start at ` +
            `page/${config.pageId}. ` +
            `Location: ${location}`
        );
    }

    /*
     * JourneyController.Page dispatches a page whose
     * type is PupilSearch to the pupil-search endpoint.
     */
    const dispatcher =
        getPage(
            baseUrl,
            location,
            {
                transaction:
                    `${prefix}-pupil-search-dispatcher`,

                amendment_type:
                    amendmentType,
            }
        );

    const searchLocation =
        checkRedirect(
            dispatcher,
            `/pupil-search/${config.pageId}`
        );

    const searchPage =
        getPage(
            baseUrl,
            searchLocation,
            {
                transaction:
                    `${prefix}-pupil-search-page`,

                amendment_type:
                    amendmentType,
            }
        );

    checkPage(
        searchPage,
        config.expectedHeading
    );

    return {
        location:
            searchLocation,

        antiforgeryToken:
            getAntiforgeryToken(
                searchPage
            ),
    };
}

/**
 * Selects the pupil on the pupil-search page.
 *
 * Returns the redirect location of the next page.
 */
export function selectPupil({
    baseUrl,
    amendmentType,
    location,
    antiforgeryToken,
    pupil,
    config,
}: SelectPupilOptions): string {

    const selectedPupilLabel =
        pupil.searchLabel;

    const response =
        postForm(
            baseUrl,
            location,
            {
                __RequestVerificationToken:
                    antiforgeryToken,

                SelectedPupilId:
                    pupil.id,

                SelectedPupilLabel:
                    selectedPupilLabel,
            },
            {
                transaction:
                    `${transactionPrefix(amendmentType)}-select-pupil`,

                amendment_type:
                    amendmentType,
            }
        );

    return checkRedirect(
        response,
        config.expectedRedirect
    );
}

export interface SelectSecondPupilOptions {
    baseUrl: string;
    amendmentType: AmendmentType;

    /*
     * Location of the second pupil-search page, as
     * returned by selectPupil().
     */
    location: string;

    pupil: Pupil;
    config: SecondPupilSearchConfig;
}

/**
 * Selects the second pupil on the second pupil-search
 * page, for example the second record of a merge.
 *
 * Returns the redirect location of the next page.
 */
export function selectSecondPupil({
    baseUrl,
    amendmentType,
    location,
    pupil,
    config,
}: SelectSecondPupilOptions): string {

    const prefix =
        transactionPrefix(amendmentType);

    const searchPage =
        getPage(
            baseUrl,
            location,
            {
                transaction:
                    `${prefix}-second-pupil-search-page`,

                amendment_type:
                    amendmentType,
            }
        );

    checkPage(
        searchPage,
        config.expectedHeading
    );

    const response =
        postForm(
            baseUrl,
            location,
            {
                __RequestVerificationToken:
                    getAntiforgeryToken(
                        searchPage
                    ),

                SelectedPupilId:
                    pupil.id,

                SelectedPupilLabel:
                    pupil.searchLabel,
            },
            {
                transaction:
                    `${prefix}-select-second-pupil`,

                amendment_type:
                    amendmentType,
            }
        );

    return checkRedirect(
        response,
        config.expectedRedirect
    );
}
