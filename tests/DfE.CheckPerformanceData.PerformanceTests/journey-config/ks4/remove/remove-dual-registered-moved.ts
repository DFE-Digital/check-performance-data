import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isNotAddBack } from "../shared.ts";

export const removeDualRegisteredMoved = {
    id: "remove-dual-registered-moved",
    journeyType: "Remove",
    reason: "Dual registered or moved school",

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
                    "dual-registered-moved",
            },

            expectedRedirect:
                "page/dual-registered-moved",
        },

        {
            pageId: "dual-registered-moved",

            expectedHeading:
                "What is the DfE number of the school",

            answers: {
                q_dual_registered_moved_dfe_number:
                    "1234567",
            },

            expectedRedirect:
                "page/dual-registered-moved-evidence",
        },

        {
            pageId: "dual-registered-moved-evidence",

            expectedHeading:
                "Provide evidence for the removal",

            // Evidence is optional for this reason.
            answers: {},

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;
