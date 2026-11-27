import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isIncluded } from "../../../lib/pupil-inclusion.ts";

export const removeKs2PupilDied = {
    id: "ks2-remove-pupil-died",
    journeyType: "Remove",
    reason: "Pupil has died",
    window: "ks2",

    pupilSearch: {
        pageId: "select-pupil",

        expectedHeading:
            "What is the name of the pupil to be removed?",

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
                    "pupil-died",
            },

            expectedRedirect:
                "page/pupil-died",
        },

        {
            pageId: "pupil-died",

            expectedHeading:
                "removed from your school roll",

            answers: {
                q_date_removed_from_roll_day:
                    "20",

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
