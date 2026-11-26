import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isIncluded } from "../shared.ts";

export const removeKs2AddedAfterSummerTerm = {
    id: "ks2-remove-added-after-summer-term",
    journeyType: "Remove",
    reason: "Pupil added to school roll after start of summer term",
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
                    "added-after-summer-term",
            },

            expectedRedirect:
                "page/added-after-summer-term",
        },

        {
            pageId: "added-after-summer-term",

            expectedHeading:
                "added to your roll?",

            answers: {
                q_date_added_to_roll_day:
                    "20",

                q_date_added_to_roll_month:
                    "01",

                q_date_added_to_roll_year:
                    "2026",
            },

            expectedRedirect:
                "page/evidence-with-comments",
        },

        {
            pageId: "evidence-with-comments",

            expectedHeading:
                "Provide evidence for the removal",

            // Evidence file is uploaded before the text is submitted.
            uploadEvidence: true,

            answers: {
                q_additional_comments:
                    "Verification evidence text",
            },

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;
