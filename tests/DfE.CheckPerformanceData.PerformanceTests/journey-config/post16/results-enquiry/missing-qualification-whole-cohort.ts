import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import {
    isEnquiryPupil,
} from "./pupils.ts";

export const post16ResultsEnquiryMissingQualificationWholeCohort = {
    id: "post16-results-enquiry-missing-qualification-whole-cohort",
    journeyType: "ResultsEnquiry",
    reason: "Missing qualification, whole cohort",
    window: "post16",

    pupilSearch: {
        pageId: "select-student-cohort",

        expectedHeading:
            "What is the name of one of the students from the affected cohort?",

        expectedRedirect:
            "/qualification-search/select-qualification",
    },

    pupilFilter:
        isEnquiryPupil,

    preSteps: [
        {
            pageId: "result-issue",

            expectedHeading:
                "What issue with the results do you need to report?",

            answers: {
                IssueType:
                    "missing-qualification",
            },

            expectedRedirect:
                "/page/cohort-scope",
        },

        {
            pageId: "cohort-scope",

            expectedHeading:
                "Does the missing qualification affect the whole cohort?",

            answers: {
                q_q_cohort_scope:
                    "yes",
            },

            expectedRedirect:
                "/page/cohort-count",
        },

        {
            pageId: "cohort-count",

            expectedHeading:
                "How many students are missing this qualification?",

            answers: {
                q_q_cohort_count:
                    "5",
            },

            expectedRedirect:
                "/page/select-student-cohort",
        },
    ],

    steps: [
        {
            pageId: "qualification-search",

            expectedHeading:
                "Provide the missing qualification details for",

            answers: {
                selectedAo:
                    "AQA",

                selectedQan:
                    "60144567",
            },

            expectedRedirect:
                "/page/qualification-details",
        },

        {
            pageId: "qualification-details",

            expectedHeading:
                "Provide the missing qualification details for",

            answers: {
                q_q_syllabus_code:
                    "7201",

                q_q_award_date_day:
                    "20",

                q_q_award_date_month:
                    "8",

                q_q_award_date_year:
                    "2026",

                q_q_missing_grade:
                    "A",

                q_q_ncn:
                    "12345",
            },

            expectedRedirect:
                "/page/additional-info",
        },

        {
            pageId: "additional-info",

            expectedHeading:
                "Additional information (Optional)",

            answers: {
                q_q_additional_info:
                    "",
            },

            expectedRedirect:
                "/summary",
        },
    ],

    completion: "submit",
} satisfies JourneyConfig;
