import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import {
    enquiryResultKey,
    isRevisableEnquiryPupil,
    revisedGradeFor,
} from "./pupils.ts";

export const post16ResultsEnquiryIncorrectGradeWholeCohort = {
    id: "post16-results-enquiry-incorrect-grade-whole-cohort",
    journeyType: "ResultsEnquiry",
    reason: "Incorrect grade, whole cohort",
    window: "post16",

    pupilSearch: {
        pageId: "select-student-cohort",

        expectedHeading:
            "What is the name of one of the students from the affected cohort?",

        expectedRedirect:
            "/result-search/select-result",
    },

    pupilFilter:
        isRevisableEnquiryPupil,

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
                    "yes",
            },

            expectedRedirect:
                "/page/cohort-count",
        },

        {
            pageId: "cohort-count",

            expectedHeading:
                "Enter the number of students who have an incorrect grade for this qualification",

            answers: {
                q_q_cohort_count:
                    "2",
            },

            expectedRedirect:
                "/page/select-student-cohort",
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

            answers: (_iteration, pupil) => ({
                q_q_revised_grade:
                    revisedGradeFor(pupil),
            }),

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
