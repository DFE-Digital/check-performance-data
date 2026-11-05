import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import {
    isIncluded,
} from "../shared.ts";

export const post16RemoveStudentDied = {
    id: "post16-remove-student-died",
    journeyType: "Remove",
    reason: "Student died",
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
                    "student-died",
            },

            expectedRedirect:
                "page/student-died",
        },

        {
            pageId: "student-died",

            expectedHeading:
                "removed from your school or college roll",

            answers: {
                q_date_removed_from_roll_day:
                    "01",

                q_date_removed_from_roll_month:
                    "01",

                q_date_removed_from_roll_year:
                    "2026",
            },

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;
