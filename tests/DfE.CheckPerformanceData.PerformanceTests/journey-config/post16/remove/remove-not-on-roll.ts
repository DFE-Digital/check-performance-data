import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import {
    isIncluded,
} from "../shared.ts";

export const post16RemoveNotOnRoll = {
    id: "post16-remove-not-on-roll",
    journeyType: "Remove",
    reason: "Not on roll",
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
                    "not-on-roll",
            },

            expectedRedirect:
                "page/not-on-roll",
        },

        {
            pageId: "not-on-roll",

            expectedHeading:
                "Why are you asking to remove",

            answers: {
                q_not_on_roll_reason:
                    "apprentice",
            },

            expectedRedirect:
                "page/not-on-roll-evidence",
        },

        {
            pageId: "not-on-roll-evidence",

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
