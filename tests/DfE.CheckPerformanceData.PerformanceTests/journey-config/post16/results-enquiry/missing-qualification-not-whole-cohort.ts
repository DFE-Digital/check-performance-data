import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import {
    isEnquiryPupil,
} from "./pupils.ts";

export const post16ResultsEnquiryMissingQualificationNotWholeCohort = {
    id: "post16-results-enquiry-missing-qualification-not-whole-cohort",
    journeyType: "ResultsEnquiry",
    reason: "Missing qualification, not whole cohort",
    window: "post16",

    pupilSearch: {
        pageId: "select-student-single",

        expectedHeading:
            "What is the name of the student missing this qualification?",

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
                    "no",
            },

            expectedRedirect:
                "/page/select-student-single",
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
