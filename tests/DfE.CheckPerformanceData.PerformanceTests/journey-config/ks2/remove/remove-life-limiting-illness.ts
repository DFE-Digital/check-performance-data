import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isIncluded } from "../shared.ts";

export const removeKs2LifeLimitingIllness = {
    id: "ks2-remove-life-limiting-illness",
    journeyType: "Remove",
    reason: "Terminal or critical illness",
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
                    "life-limiting-illness",
            },

            expectedRedirect:
                "page/life-limiting-illness",
        },

        {
            pageId: "life-limiting-illness",

            expectedHeading:
                "Tell us about the terminal or critical illness",

            answers: {
                q_life_limiting_illness_health_issue:
                    "terminal-illness",

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
