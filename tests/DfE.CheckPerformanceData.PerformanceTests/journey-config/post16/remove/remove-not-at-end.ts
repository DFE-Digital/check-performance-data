import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import {
    isIncluded,
} from "../shared.ts";

export const post16RemoveNotAtEnd = {
    id: "post16-remove-not-at-end",
    journeyType: "Remove",
    reason: "Not at end of 16 to 19 study",
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
                    "not-at-end-of-16-19-study",
            },

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;
