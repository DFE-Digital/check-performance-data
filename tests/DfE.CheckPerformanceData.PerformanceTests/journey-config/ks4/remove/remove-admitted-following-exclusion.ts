import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isNotAddBack } from "../shared.ts";

export const removeAdmittedFollowingExclusion = {
    id: "ks4-remove-admitted-following-exclusion",
    journeyType: "Remove",
    reason: "Admitted following permanent exclusion",

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
