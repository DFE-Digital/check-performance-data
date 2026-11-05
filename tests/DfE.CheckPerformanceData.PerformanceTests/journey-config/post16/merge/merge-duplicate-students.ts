import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import {
    isIncluded,
} from "../shared.ts";

export const post16MergeDuplicateStudents = {
    id: "post16-merge-duplicate-students",
    journeyType: "Merge",
    reason: "Merge duplicate student records",
    window: "post16",

    pupilSearch: {
        pageId: "select-pupil",

        expectedHeading:
            "What is the name of the student for the first duplicate record to be merged?",

        expectedRedirect:
            "/pupil-search/select-match-pupil",
    },

    pupilFilter: isIncluded,

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

            answers: {
                q_how_evidence_supports:
                    "",
            },

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;
