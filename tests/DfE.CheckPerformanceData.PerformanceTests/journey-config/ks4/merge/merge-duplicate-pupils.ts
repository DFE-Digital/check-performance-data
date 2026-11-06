import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

/*
 * Merge two duplicate pupil records.
 *
 * The first record is the iteration's pupil and the
 * second record is the next pupil in the data list, so
 * each iteration merges two different pupils.
 */
export const mergeDuplicatePupils = {
    id: "ks4-merge-duplicate-pupils",
    journeyType: "Merge",
    reason: "Duplicate pupil records",

    pupilSearch: {
        pageId: "select-pupil",

        expectedHeading:
            "What is the name of the pupil for the first duplicate record to be merged?",

        expectedRedirect:
            "/pupil-search/select-match-pupil",
    },

    secondPupilSearch: {
        expectedHeading:
            "What is the CYPMD ID of the second duplicate record to be merged?",

        expectedRedirect:
            "/page/evidence",
    },

    completion: "submit",

    steps: [
        {
            pageId: "evidence",

            expectedHeading:
                "Provide evidence for merging duplicate records",

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
