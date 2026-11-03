import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import {
    isIncluded,
} from "../shared.ts";

export const post16RemoveOther = {
    id: "post16-remove-other",
    journeyType: "Remove",
    reason: "Other",
    window: "post16",

    pupilSearch: {
        pageId: "select-pupil",

        expectedHeading:
            "What is the name of the student to be removed?",

        expectedRedirect:
            "/page/reason",
    },

    pupilFilter: isIncluded,

    completion: "submit",

    steps: [
        {
            pageId: "reason",

            expectedHeading:
                "Why are you asking to remove",

            answers: {
                q_reason:
                    "other",
            },

            expectedRedirect:
                "page/other",
        },

        {
            pageId: "other",

            expectedHeading:
                "Which years do you want to remove",

            answers: {
                q_years_to_remove:
                    "2025-2026",
            },

            expectedRedirect:
                "page/other-evidence",
        },

        {
            pageId: "other-evidence",

            expectedHeading:
                "Provide evidence for the removal of",

            answers: {
                q_how_evidence_supports:
                    "Verification evidence text",
            },

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;
