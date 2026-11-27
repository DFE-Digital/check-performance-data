import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isIncluded } from "../../../lib/pupil-inclusion.ts";

export const removeKs2SocialCareInvolvement = {
    id: "ks2-remove-social-care-involvement",
    journeyType: "Remove",
    reason: "Social care involvement - including police",
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
                    "social-care-involvement",
            },

            expectedRedirect:
                "page/social-care",
        },

        {
            pageId: "social-care",

            expectedHeading:
                "Tell us about the social care involvement",

            answers: {
                q_social_care_reason:
                    "social-care-situation",

                q_sat_exams:
                    "no",
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
