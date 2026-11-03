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
 * Per-window values that differ between checking windows.
 */
export interface WindowProfile {
    key: WindowKey;

    /*
     * Checking window id, used in every window-scoped URL.
     */
    windowId: string;

    /*
     * Blob in the window's admin storage that holds the
     * pupil or student records.
     */
    pupilBlob: string;

    /*
     * Blob of results used by Results Enquiry. Only set
     * for windows that run Results Enquiry journeys.
     */
    resultsBlob?: string;

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
