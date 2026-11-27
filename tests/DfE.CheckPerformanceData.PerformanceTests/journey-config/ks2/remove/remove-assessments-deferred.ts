import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isIncluded } from "../../../lib/pupil-inclusion.ts";

export const removeKs2AssessmentsDeferred = {
    id: "ks2-remove-assessments-deferred",
    journeyType: "Remove",
    reason: "One or more end-of-key-stage assessments deferred by a year",
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
                    "assessments-deferred",
            },

            expectedRedirect:
                "page/deferred-assessments",
        },

        {
            pageId: "deferred-assessments",

            expectedHeading:
                "continuing with KS2 studies?",

            answers: {
                q_continuing_ks2_studies:
                    "no",
            },

            expectedRedirect:
                "page/evidence-with-comments",
        },

        {
            pageId: "evidence-with-comments",

            expectedHeading:
                "Provide evidence for the removal",

            // Evidence file is uploaded before the text is submitted.
            uploadEvidence: true,

            answers: {
                q_additional_comments:
                    "Verification evidence text",
            },

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;
