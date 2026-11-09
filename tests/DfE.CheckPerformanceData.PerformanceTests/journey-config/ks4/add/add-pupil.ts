import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import {
    getRunId,
} from "../../../lib/run-id.ts";

const RUN_CODES = 26 ** 5;
const ITERATIONS_PER_RUN = 26 ** 3;

/*
 * Deterministic 32-bit FNV-1a hash of the run ID.
 */
function hashRunId(runId: string): number {
    let hash = 0x811c9dc5;

    for (let i = 0; i < runId.length; i++) {
        hash ^= runId.charCodeAt(i);
        hash = Math.imul(hash, 0x01000193);
    }

    return hash >>> 0;
}

/*
 * Fixed-width lowercase letters, so the parts never run together.
 */
function toLetters(value: number, width: number): string {
    let letters = "";
    let rest = value;

    for (let i = 0; i < width; i++) {
        letters =
            String.fromCharCode(
                97 + (rest % 26)
            ) + letters;

        rest = Math.floor(rest / 26);
    }

    return letters;
}

/*
 * Surname = "Add" + 5 letters from RUN_ID + 3 letters
 * from the iteration. Letters only, as the live name
 * check requires. The same RUN_ID and iteration always
 * give the same surname.
 */
function surnameFor(
    runId: string,
    iteration: number,
): string {
    if (iteration >= ITERATIONS_PER_RUN) {
        throw new Error(
            `ks4-add-pupil supports ${ITERATIONS_PER_RUN} iterations per run`
        );
    }

    return "Add" +
        toLetters(hashRunId(runId) % RUN_CODES, 5) +
        toLetters(iteration, 3);
}

/*
 * Add a pupil who is not in the data.
 *
 * No pupil is selected from the blob. The learner details
 * are created per iteration. The UPN is left empty.
 */
export const addPupil = {
    id: "ks4-add-pupil",
    journeyType: "Add",
    reason: "Add a pupil to data",

    completion: "submit",

    steps: [
        {
            pageId: "learner-details",

            expectedHeading:
                "Enter the details of the pupil you want to add",

            answers:
                (iteration: number) => ({
                    q_first_name:
                        "Test",

                    q_last_name:
                        surnameFor(
                            getRunId(),
                            iteration
                        ),

                    q_date_of_birth_day:
                        "1",

                    q_date_of_birth_month:
                        "1",

                    q_date_of_birth_year:
                        "2000",

                    q_sex:
                        "M",

                    q_upn:
                        "",
                }),

            expectedRedirect:
                "/page/admission-details",
        },
        {
            pageId: "admission-details",

            expectedHeading:
                "details at your school",

            answers: {
                q_admission_date_day:
                    "1",

                q_admission_date_month:
                    "1",

                q_admission_date_year:
                    "2000",

                q_year_group:
                    "10",

                q_sen_status:
                    "K",
            },

            expectedRedirect:
                "/page/evidence",
        },
        {
            pageId: "evidence",

            expectedHeading:
                "Provide evidence for the addition of",

            // No evidence file. The text field is optional.
            answers: {
                q_how_evidence_supports:
                    "",
            },

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;
