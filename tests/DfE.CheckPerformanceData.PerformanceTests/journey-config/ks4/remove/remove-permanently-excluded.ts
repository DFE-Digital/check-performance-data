import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isNotAddBack } from "../shared.ts";

export const removePermanentlyExcluded = {
    id: "ks4-remove-permanently-excluded",
    journeyType: "Remove",
    reason: "Permanently excluded from current school",

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
                    "permanently-excluded",
            },

            expectedRedirect:
                "page/permanently-excluded",
        },

        {
            pageId: "permanently-excluded",

            expectedHeading:
                "Tell us about the permanent exclusion",

            answers: {
                q_date_permanently_excluded_day:
                    "20",

                q_date_permanently_excluded_month:
                    "01",

                q_date_permanently_excluded_year:
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
