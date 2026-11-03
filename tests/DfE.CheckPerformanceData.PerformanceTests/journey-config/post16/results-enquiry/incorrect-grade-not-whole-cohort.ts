import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import {
    enquiryResultKey,
    isEnquiryPupil,
} from "./pupils.ts";

export const post16ResultsEnquiryIncorrectGradeNotWholeCohort = {
    id: "post16-results-enquiry-incorrect-grade-not-whole-cohort",
    journeyType: "ResultsEnquiry",
    reason: "Incorrect grade, not whole cohort",
    window: "post16",

    pupilSearch: {
        pageId: "select-student-single",

        expectedHeading:
            "What is the name of the student with an incorrect grade?",

        expectedRedirect:
            "/result-search/select-result",
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
                    "incorrect-grade",
            },

            expectedRedirect:
                "/page/check-late-results",
        },

        {
            pageId: "check-late-results",

            expectedHeading:
                "Check your second late results file before you report an incorrect grade",

            answers: {},

            expectedRedirect:
                "/page/cohort-scope",
        },

        {
            pageId: "cohort-scope",

            expectedHeading:
                "Does the incorrect grade affect the whole cohort?",

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
            pageId: "result-search",

            expectedHeading:
                "results is incorrect?",

            answers: (_iteration, pupil) => ({
                selectedResultKey:
                    enquiryResultKey(pupil),
            }),

            expectedRedirect:
                "/page/grade-details",
        },

        {
            pageId: "grade-details",

            expectedHeading:
                "Incorrect grade details",

            answers: {
                q_q_revised_grade:
                    "1",
            },

            expectedRedirect:
                "/page/additional-info",
        },

        {
            pageId: "additional-info",

            expectedHeading:
                "Additional information",

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
