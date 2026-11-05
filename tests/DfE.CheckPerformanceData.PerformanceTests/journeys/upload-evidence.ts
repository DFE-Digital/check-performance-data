import {
    check,
    fail,
} from "k6";

import {
    getPage,
    postMultipartFile,
} from "../lib/navigation.ts";

import {
    checkPage,
    checkRedirect,
} from "../lib/assertions.ts";

import {
    getAntiforgeryToken,
} from "../lib/antiforgery.ts";

import type {
    EvidenceFile,
} from "../types/evidence.ts";

export interface UploadEvidenceOptions {
    baseUrl: string;

    /*
     * Path of the evidence page, without a query string.
     */
    location: string;

    antiforgeryToken: string;

    evidenceFile: EvidenceFile;
}

export interface UploadEvidenceResult {
    /*
     * Token from the evidence page after the upload.
     * Use it for the next POST, because the page is
     * rendered again after the upload.
     */
    antiforgeryToken: string;
}

/**
 * Uploads one evidence file using the page's
 * "Upload file" control, then validates the page
 * that the upload redirects to.
 *
 * The upload is a multipart POST to the same page's
 * question upload endpoint, which redirects back to
 * the evidence page with the file listed.
 */
export function uploadEvidenceFile({
    baseUrl,
    location,
    antiforgeryToken,
    evidenceFile,
}: UploadEvidenceOptions): UploadEvidenceResult {

    const uploadResponse =
        postMultipartFile(
            baseUrl,
            `${location}/question/evidence/upload?fromSummary=False`,
            {
                __RequestVerificationToken:
                    antiforgeryToken,

                fromSummary:
                    "false",
            },
            "fileUpload",
            evidenceFile,
            {
                transaction:
                    "evidence-upload",
            }
        );

    const redirectLocation =
        checkRedirect(
            uploadResponse,
            location
        );

    const page =
        getPage(
            baseUrl,
            redirectLocation,
            {
                transaction:
                    "evidence-upload-page",
            }
        );

    checkPage(
        page,
        "Provide evidence"
    );

    const fileListed =
        page
            .html()
            .find(".govuk-table__body")
            .text()
            .includes(
                evidenceFile.name
            );

    const listedCheckPassed =
        check(page, {
            [`Evidence file "${evidenceFile.name}" is listed`]:
                () => fileListed,
        });

    if (!listedCheckPassed) {
        fail(
            "Evidence upload check failed: " +
            `url=${page.url}, ` +
            `file=${JSON.stringify(evidenceFile.name)}`
        );
    }

    return {
        antiforgeryToken:
            getAntiforgeryToken(
                page
            ),
    };
}
