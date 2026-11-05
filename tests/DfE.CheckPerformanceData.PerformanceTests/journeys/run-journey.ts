import {
    startAmendment,
} from "./start-amendment.ts";

import {
    startResultsEnquiry,
} from "./start-results-enquiry.ts";

import {
    openPupilSearch,
    selectPupil,
    selectSecondPupil,
} from "./pupil-search.ts";

import {
    answerQuestionPage,
} from "./answer-question.ts";

import {
    submitRequest,
} from "./submit-request.ts";

import {
    AMENDMENT_SUBMIT_HEADINGS,
    RESULTS_ENQUIRY_SUBMIT_HEADINGS,
} from "./submit-request.ts";

import {
    saveRequestForLater,
} from "./save-request.ts";

import {
    findDraftReference,
} from "./find-draft-request.ts";

import {
    pupilFullName,
} from "../lib/pupil-selection.ts";

import type {
    JourneyConfig,
} from "../types/journey-config.ts";

import type {
    FormData,
} from "../types/form-data.ts";

import type {
    JourneyStep,
    StepAnswers,
} from "../types/journey-step.ts";

import type {
    EvidenceFile,
} from "../types/evidence.ts";

import type {
    Pupil,
} from "../types/pupil.ts";

import type {
    JourneyResult,
    RunJourneyOptions,
} from "../types/run-journey.ts";

/**
 * Runs a configured journey from start to completion.
 *
 * 1. Start the amendment (common sequence), or the
 *    results enquiry for ResultsEnquiry journeys
 * 2. Answer the pre-search pages, when configured
 * 3. Select a pupil, when the journey configures a
 *    pupil-search step
 * 4. Answer each configured question page
 * 5. Complete the request using the configured mode
 */
export function runJourney({
    baseUrl,
    profile,
    pupil,
    secondPupil,
    iteration,
    evidenceFile,
    config,
}: RunJourneyOptions): JourneyResult {

    let location =
        config.journeyType === "ResultsEnquiry"
            ? startResultsEnquiry(
                baseUrl,
                profile
            )
            : startAmendment(
                baseUrl,
                profile,
                config.journeyType
            ).location;

    location = answerSteps(
        baseUrl,
        location,
        config.preSteps ?? [],
        iteration,
        pupil,
        evidenceFile
    );

    if (config.pupilSearch) {
        if (!pupil) {
            throw new Error(
                `Journey "${config.id}" requires a pupil`
            );
        }

        const search =
            openPupilSearch({
                baseUrl,
                amendmentType:
                    config.journeyType,
                location,
                config:
                    config.pupilSearch,
            });

        location =
            selectPupil({
                baseUrl,
                amendmentType:
                    config.journeyType,
                location:
                    search.location,
                antiforgeryToken:
                    search.antiforgeryToken,
                pupil,
                config:
                    config.pupilSearch,
            });

        if (config.secondPupilSearch) {
            if (!secondPupil) {
                throw new Error(
                    `Journey "${config.id}" requires a second pupil`
                );
            }

            location =
                selectSecondPupil({
                    baseUrl,
                    amendmentType:
                        config.journeyType,
                    location,
                    pupil: secondPupil,
                    config:
                        config.secondPupilSearch,
                });
        }
    }

    location = answerSteps(
        baseUrl,
        location,
        config.steps,
        iteration,
        pupil,
        evidenceFile
    );

    return complete({
        baseUrl,
        windowId: profile.windowId,
        pupil,
        summaryLocation:
            location,
        config,
    });
}

function answerSteps(
    baseUrl: string,
    startLocation: string,
    steps: JourneyStep[],
    iteration: number,
    pupil: Pupil | undefined,
    evidenceFile: EvidenceFile | undefined
): string {
    let location = startLocation;

    for (const step of steps) {
        location =
            answerQuestionPage({
                baseUrl,
                location,
                ...step,
                answers:
                    resolveAnswers(
                        step.answers,
                        iteration,
                        pupil
                    ),
                evidenceFile,
            });
    }

    return location;
}

function resolveAnswers(
    answers: StepAnswers,
    iteration: number,
    pupil: Pupil | undefined
): FormData {
    return typeof answers === "function"
        ? answers(iteration, pupil)
        : answers;
}

interface CompleteOptions {
    baseUrl: string;
    windowId: string;
    pupil: Pupil | undefined;
    summaryLocation: string;
    config: JourneyConfig;
}

function complete({
    baseUrl,
    windowId,
    pupil,
    summaryLocation,
    config,
}: CompleteOptions): JourneyResult {

    switch (config.completion) {

        case "submit": {
            const submitted =
                submitRequest({
                    baseUrl,
                    summaryLocation,
                    headings:
                        config.journeyType === "ResultsEnquiry"
                            ? RESULTS_ENQUIRY_SUBMIT_HEADINGS
                            : AMENDMENT_SUBMIT_HEADINGS,
                });

            return {
                referenceNumber:
                    submitted.referenceNumber,

                completion:
                    "submit",
            };
        }

        case "save-for-later": {
            if (!pupil) {
                throw new Error(
                    "save-for-later requires a pupil " +
                    "to locate the draft"
                );
            }

            saveRequestForLater({
                baseUrl,
                summaryLocation,
            });

            const referenceNumber =
                findDraftReference({
                    baseUrl,
                    windowId,
                    pupilName:
                        pupilFullName(pupil),
                });

            return {
                referenceNumber,

                completion:
                    "save-for-later",
            };
        }

        default:
            throw new Error(
                `Unknown completion mode: ` +
                `${String(config.completion)}`
            );
    }
}
