import type {
    FormData,
} from "./form-data";

import type {
    Pupil,
} from "./pupil";

/*
 * Answers posted on a page. A function is called with
 * the iteration number and the selected pupil so that
 * each iteration can post different values.
 */
export type StepAnswers =
    | FormData
    | ((iteration: number, pupil: Pupil | undefined) => FormData);

export interface JourneyStep {
    pageId: string;
    expectedHeading: string;
    answers: StepAnswers;
    expectedRedirect: string;

    /*
     * When true, the evidence file from the run options
     * is uploaded on this page before the answers are
     * submitted. Only valid on the evidence page.
     */
    uploadEvidence?: boolean;
}