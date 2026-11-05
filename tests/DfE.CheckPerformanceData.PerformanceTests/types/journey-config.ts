import type {
    AmendmentType,
} from "./amendment.ts";

import type {
    JourneyStep,
} from "./journey-step.ts";

import type {
    Pupil,
} from "./pupil.ts";

import type {
    WindowKey,
} from "./window.ts";

/**
 * How a journey finishes.
 *
 * submit:          submit the request and read its
 *                  reference from the confirmation page.
 *
 * save-for-later:  save the request as a draft and
 *                  locate its reference on the
 *                  amendment request summary page.
 */
export type CompletionMode =
    | "submit"
    | "save-for-later";

/**
 * The pupil-search page that selects the pupil for
 * the journey.
 */
export interface PupilSearchConfig {
    /*
     * Journey page id. The journey dispatcher redirects
     * /Journey/{windowId}/page/{pageId} to the
     * pupil-search page.
     */
    pageId: string;

    expectedHeading: string;

    /*
     * Location fragment expected after the pupil is
     * selected. This is normally the first question page.
     */
    expectedRedirect: string;
}

/**
 * A second pupil-search page that follows the first.
 *
 * The page is reached by the redirect from the first
 * pupil search, so it has no page id of its own.
 */
export interface SecondPupilSearchConfig {
    /*
     * Heading of the second pupil-search page.
     */
    expectedHeading: string;

    /*
     * Location fragment expected after the second pupil
     * is selected.
     */
    expectedRedirect: string;
}

export interface JourneyConfig {
    /*
     * Unique registry key, selected with the JOURNEY
     * environment variable.
     */
    id: string;

    journeyType: AmendmentType;
    reason: string;

    /*
     * Checking window the journey runs in. Omit for KS4.
     */
    window?: WindowKey;

    /*
     * Omit for journeys that do not select an existing
     * pupil.
     */
    pupilSearch?: PupilSearchConfig;

    /*
     * Restricts the pupils offered to pupilSearch (and
     * secondPupilSearch) to those for which this returns
     * true. Omit to use every pupil in the data file.
     */
    pupilFilter?: (pupil: Pupil) => boolean;

    /*
     * Omit unless the journey selects a second existing
     * pupil, for example a merge of two records. Requires
     * pupilSearch.
     */
    secondPupilSearch?: SecondPupilSearchConfig;

    /*
     * Pages answered after the journey starts and before
     * the pupil search, for example the issue type and
     * cohort scope of a results enquiry. Omit when the
     * journey has none.
     */
    preSteps?: JourneyStep[];

    steps: JourneyStep[];

    completion: CompletionMode;
}