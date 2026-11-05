/**
 * Checking windows that journeys can run in.
 *
 * ks4:    Key Stage 4 checking window. Records are pupils.
 *
 * post16: 16 to 19 checking window. Records are students.
 */
export type WindowKey =
    | "ks4"
    | "post16";

/**
 * Full blob paths in a window's admin storage, found at
 * setup() from the newest release of each file.
 */
export interface WindowBlobs {
    pupilBlob: string;
    resultsBlob?: string;
}

/**
 * Per-window values that differ between checking windows.
 */
export interface WindowProfile {
    key: WindowKey;

    /*
     * Checking window id, used in every window-scoped URL.
     */
    windowId: string;

    /*
     * File name of the pupil or student records blob, for
     * example 8604070_pupils.json. setup() finds the blob
     * in the newest release of the window.
     */
    pupilFile: string;

    /*
     * File name of the results blob used by Results
     * Enquiry. Only set for windows that run Results
     * Enquiry journeys.
     */
    resultsFile?: string;

    /*
     * Heading of the first page of an amendment
     * (Check your pupil data / student data).
     */
    checkPupilDataHeading: string;

    /*
     * Heading of the What to change page.
     */
    whatToChangeHeading: string;
}
