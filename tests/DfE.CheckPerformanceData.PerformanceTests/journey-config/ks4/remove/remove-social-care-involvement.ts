import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isNotAddBack } from "../shared.ts";

export const removeSocialCareInvolvement = {
    id: "remove-social-care-involvement",
    journeyType: "Remove",
    reason: "Social care involvement",

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
                    "social-care-involvement",
            },

            expectedRedirect:
                "page/social-care",
        },

        {
            pageId: "social-care",

            expectedHeading:
                "Social care involvement - including police or prison",

            answers: {
                q_social_care_reason:
                    "social-care-situation",

                q_sat_exams:
                    "yes",
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
