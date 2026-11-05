import type {
    Response,
} from "k6/http";

import type {
    Selection,
} from "k6/html";

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
    FormData,
} from "../types/form-data.ts";

export interface SaveRequestForLaterOptions {
    baseUrl: string;
    summaryLocation: string;
}

export const SAVE_FOR_LATER_LABEL =
    "Save and continue later";

/*
 * Returns the visible label of a button or input.
 */
function buttonLabel(
    button: Selection
): string {
    const text =
        button
            .text()
            .replace(/\s+/g, " ")
            .trim();

    return text || (button.attr("value") ?? "");
}

/**
 * Reads the save button's name/value pair from the
 * summary page, so the field name is not hard-coded.
 *
 * Returns the form action and the fields to post.
 */
function saveButtonSubmission(
    response: Response,
    summaryLocation: string
): { action: string; fields: FormData } {
    const buttons =
        response
            .html()
            .find(
                "button, input[type='submit']"
            )
            .filter(
                (
                    _index: number,
                    element: Selection
                ) =>
                    buttonLabel(element) ===
                    SAVE_FOR_LATER_LABEL
            );

    if (buttons.size() !== 1) {
        throw new Error(
            `Expected one "${SAVE_FOR_LATER_LABEL}" ` +
            `button on ${response.url}, ` +
            `found ${buttons.size()}`
        );
    }

    const button =
        buttons.first();

    const name =
        button.attr("name");

    const value =
        button.attr("value") ??
        SAVE_FOR_LATER_LABEL;

    const action =
        button.attr("formaction") ??
        button
            .closest("form")
            .attr("action") ??
        summaryLocation;

    return {
        action,

        fields:
            name
                ? { [name]: value }
                : {},
    };
}

/**
 * Saves the request as a draft from the summary page.
 *
 * Returns the redirect location after the save.
 */
export function saveRequestForLater({
    baseUrl,
    summaryLocation,
}: SaveRequestForLaterOptions): string {

    const summaryPage =
        getPage(
            baseUrl,
            summaryLocation,
            {
                transaction:
                    "summary-page",
            }
        );

    checkPage(
        summaryPage,
        "Summary of amendment request"
    );

    const token =
        getAntiforgeryToken(
            summaryPage
        );

    const submission =
        saveButtonSubmission(
            summaryPage,
            summaryLocation
        );

    const response =
        postForm(
            baseUrl,
            submission.action,
            {
                __RequestVerificationToken:
                    token,

                ...submission.fields,
            },
            {
                transaction:
                    "save-request-for-later",
            }
        );

    return checkRedirect(
        response,
        "/AmendmentRequests"
    );
}
