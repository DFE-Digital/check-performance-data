import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import {
    enquiryResultKey,
    isEnquiryPupil,
} from "./pupils.ts";

export const post16ResultsEnquiryResultDoesNotBelong = {
    id: "post16-results-enquiry-result-does-not-belong",
    journeyType: "ResultsEnquiry",
    reason: "Result does not belong to student",
    window: "post16",

    pupilSearch: {
        pageId: "select-student",

        expectedHeading:
            "What is the name of the student the result does not belong to?",

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
                    "result-does-not-belong",
            },

            expectedRedirect:
                "/page/select-student",
        },
    ],

    steps: [
        {
            pageId: "result-search",

            expectedHeading:
                "Which result does not belong to",

            answers: (_iteration, pupil) => ({
                selectedResultKey:
                    enquiryResultKey(pupil),
            }),

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
