import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isNotAddBack } from "../shared.ts";

export const removeYearGroupChangeLower = {
    id: "remove-year-group-change-lower",
    journeyType: "Remove",
    reason: "Year group change (lower)",

    pupilSearch: {
        pageId: "select-pupil",

        expectedHeading:
            "What is the name of the pupil to be removed?",

        expectedRedirect:
            "/page/reason",
    },

    pupilFilter: isNotAddBack,

    completion: "submit",

    steps: [
        {
            pageId: "reason",

            expectedHeading:
                "Why are you asking to remove",

            answers: {
                q_reason:
                    "year-group-change",
            },

            expectedRedirect:
                "page/year-group-change-higher-lower",
        },

        {
            pageId: "year-group-change-higher-lower",

            expectedHeading:
                "higher or lower than year 11",

            answers: {
                q_higher_lower:
                    "lower",
            },

            expectedRedirect:
                "page/year-group-change-lower",
        },

        {
            pageId: "year-group-change-lower",

            expectedHeading:
                "Which year group should",

            answers: {
                q_year_group_lower_moved_to:
                    "8",
            },

            expectedRedirect:
                "page/year-group-change-evidence",
        },

        {
            pageId: "year-group-change-evidence",

            expectedHeading:
                "Provide evidence for the removal",

            // Evidence is optional for this reason.
            answers: {},

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;
