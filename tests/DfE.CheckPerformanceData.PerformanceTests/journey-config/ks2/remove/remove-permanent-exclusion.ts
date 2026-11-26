import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isIncluded } from "../shared.ts";

export const removeKs2PermanentExclusion = {
    id: "ks2-remove-permanent-exclusion",
    journeyType: "Remove",
    reason: "Admitted following permanent exclusion",
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
                    "permanent-exclusion",
            },

            expectedRedirect:
                "page/permanent-exclusion",
        },

        {
            pageId: "permanent-exclusion",

            expectedHeading:
                "Tell us about the permanent exclusion",

            answers: {
                q_permanent_exclusion_dfe_number:
                    "1234567",

                q_date_pupil_excluded_day:
                    "20",

                q_date_pupil_excluded_month:
                    "01",

                q_date_pupil_excluded_year:
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
