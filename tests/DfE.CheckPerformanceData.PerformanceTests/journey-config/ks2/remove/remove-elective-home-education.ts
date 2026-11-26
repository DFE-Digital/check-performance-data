import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isIncluded } from "../shared.ts";

export const removeKs2ElectiveHomeEducation = {
    id: "ks2-remove-elective-home-education",
    journeyType: "Remove",
    reason: "Elective home education",
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
                    "elective-home-education",
            },

            expectedRedirect:
                "page/elective-home-education",
        },

        {
            pageId: "elective-home-education",

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
                "page/evidence",
        },

        {
            pageId: "evidence",

            expectedHeading:
                "Provide evidence for the removal",

            // Evidence file is uploaded before the text is submitted.
            uploadEvidence: true,

            answers: {
                q_how_evidence_supports:
                    "Verification evidence text",
            },

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;
