export interface SubmitHeadings {
    summary: string;
    confirmation: string;
    confirmationPath: string;
}

export interface SubmitRequestOptions {
    baseUrl: string;
    summaryLocation: string;
    headings: SubmitHeadings;
}

export interface SubmittedRequest {
    referenceNumber: string;
    confirmationLocation: string;

    /*
     * We'll refine this once we type
     * navigation/assertions helpers.
     */
    confirmationPage: unknown;
}