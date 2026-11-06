import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

export const removeAdmittedFromAbroad = {
    id: "ks4-remove-admitted-from-abroad",
    journeyType: "Remove",
    reason: "Admitted from abroad with English not first language",

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
                    "english-not-first-language",
            },

            expectedRedirect:
                "page/english-not-first-language",
        },

        {
            pageId: "english-not-first-language",

            expectedHeading:
                "first language?",

            answers: {
                q_first_language:
                    "english",
            },

            expectedRedirect:
                "page/english-not-first-language-details",
        },

        {
            pageId: "english-not-first-language-details",

            expectedHeading:
                "Admission and arrival dates",

            answers: {
                q_country_originally_from:
                    "Spain",

                q_country_originally_from_code:
                    "ES",

                q_date_pupil_started_day:
                    "20",

                q_date_pupil_started_month:
                    "01",

                q_date_pupil_started_year:
                    "2026",

                q_date_pupil_started_school_in_england_day:
                    "20",

                q_date_pupil_started_school_in_england_month:
                    "01",

                q_date_pupil_started_school_in_england_year:
                    "2026",

                q_date_pupil_arrived_in_england_day:
                    "20",

                q_date_pupil_arrived_in_england_month:
                    "01",

                q_date_pupil_arrived_in_england_year:
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
