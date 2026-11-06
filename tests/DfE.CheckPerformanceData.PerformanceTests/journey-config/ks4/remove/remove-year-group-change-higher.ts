import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isNotAddBack } from "../shared.ts";

export const removeYearGroupChangeHigher = {
    id: "ks4-remove-year-group-change-higher",
    journeyType: "Remove",
    reason: "Year group change (higher)",

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
                    "higher",
            },

            expectedRedirect:
                "page/year-group-change-higher",
        },

        {
            pageId: "year-group-change-higher",

            expectedHeading:
                "Year group moved to",

            answers: {
                q_year_group_higher_moved_to:
                    "12",

                q_year_group_higher_dfe_number:
                    "1234567",
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
