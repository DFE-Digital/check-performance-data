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

import {
    uploadEvidenceFile,
} from "./upload-evidence.ts";

import type {
    JourneyStep,
} from "../types/journey-step.ts";

import type {
    EvidenceFile,
} from "../types/evidence.ts";

export interface AnswerQuestionPageOptions
    extends JourneyStep {
    baseUrl: string;
    location: string;
    evidenceFile?: EvidenceFile | undefined;
}

/**
 * Opens a configured journey question page,
 * validates the expected heading, uploads evidence
 * when configured, submits the configured answers
 * and returns the next location.
 */
export function answerQuestionPage({
    baseUrl,
    location,
    pageId,
    expectedHeading,
    answers,
    expectedRedirect,
    uploadEvidence,
    evidenceFile,
}: AnswerQuestionPageOptions): string {
    const page =
        getPage(
            baseUrl,
            location,
            {
                transaction:
                    `${pageId}-page`,
            }
        );

    checkPage(
        page,
        expectedHeading
    );

    let token =
        getAntiforgeryToken(
            page
        );

    if (uploadEvidence) {
        if (!evidenceFile) {
            throw new Error(
                `Page "${pageId}" requires an evidence file`
            );
        }

        token =
            uploadEvidenceFile({
                baseUrl,
                location,
                antiforgeryToken: token,
                evidenceFile,
            }).antiforgeryToken;
    }

    const response =
        postForm(
            baseUrl,
            location,
            {
                __RequestVerificationToken:
                    token,

                FromSummary:
                    "false",

                ...answers,
            },
            {
                transaction:
                    `${pageId}-submit`,
            }
        );

    return checkRedirect(
        response,
        expectedRedirect
    );
}