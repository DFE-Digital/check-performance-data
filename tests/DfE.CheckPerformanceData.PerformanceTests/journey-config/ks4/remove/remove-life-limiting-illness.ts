import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isNotAddBack } from "../shared.ts";

export const removeLifeLimitingIllness = {
    id: "ks4-remove-life-limiting-illness",
    journeyType: "Remove",
    reason: "Life-limiting or critical illness",

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
                    "life-limiting-illness",
            },

            expectedRedirect:
                "page/life-limiting-illness",
        },

        {
            pageId: "life-limiting-illness",

            expectedHeading:
                "Terminal or critical illness",

            answers: {
                q_life_limiting_illness_health_issue:
                    "terminal-illness",

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
