import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import type {
    Pupil,
} from "../../../types/pupil.ts";

/*
 * P_INCL codes for which the live service accepts an
 * inclusion. Codes 401, 403, 414, 421 and 431 are
 * rejected with "This pupil is already included".
 * Confirmed by a read-only probe of each code.
 */
const INCLUDABLE_INCLUSION_CODES: readonly string[] = [
    "402",
    "404",
    "407",
    "408",
    "410",
    "413",
    "422",
    "430",
];

function isIncludable(pupil: Pupil): boolean {
    return INCLUDABLE_INCLUSION_CODES.includes(
        pupil.inclusionCode
    );
}

/*
 * Include a pupil who is not currently in the data.
 *
 * Only pupils with an includable P_INCL code are used.
 * The pupil is selected by iteration from that list.
 */
export const includePupil = {
    id: "include-pupil",
    journeyType: "Include",
    reason: "Include a pupil in data",

    pupilSearch: {
        pageId: "select-pupil",

        expectedHeading:
            "What is the name of the pupil to be included?",

        expectedRedirect:
            "/page/evidence",
    },

    pupilFilter: isIncludable,

    completion: "submit",

    steps: [
        {
            pageId: "evidence",

            expectedHeading:
                "Provide evidence for the inclusion of",

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
