import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

export const removePermanentlyLeftEngland = {
    id: "ks4-remove-permanently-left-england",
    journeyType: "Remove",
    reason: "Permanently left England",

    pupilSearch: {
        pageId: "select-pupil",

        expectedHeading:
            "What is the name of the pupil to be removed?",

        expectedRedirect:
            "/page/reason",
    },

    completion: "submit",

    steps: [
        {
            pageId: "reason",

            expectedHeading:
                "Why are you asking to remove",

            answers: {
                q_reason:
                    "permanently-left-england",
            },

            expectedRedirect:
                "page/permanently-left-england-questions",
        },

        {
            pageId: "permanently-left-england-questions",

            expectedHeading:
                "Tell us about the pupil leaving England",

            answers: {
                q_country_moved_to:
                    "Spain",

                q_country_moved_to_code:
                    "ES",

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
