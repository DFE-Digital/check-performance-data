import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isIncluded } from "../shared.ts";

export const removeKs2PermanentlyExcluded = {
    id: "ks2-remove-permanently-excluded",
    journeyType: "Remove",
    reason: "Permanently excluded from current school",
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

                q_permanently_excluded_dfe_number:
                    "1234567",
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
