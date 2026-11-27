import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import { isIncluded } from "../../../lib/pupil-inclusion.ts";

export const removeKs2ChildMissingEducation = {
    id: "ks2-remove-child-missing-education",
    journeyType: "Remove",
    reason: "Child missing education",
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
                    "child-missing-education",
            },

            expectedRedirect:
                "page/child-missing-education",
        },

        {
            pageId: "child-missing-education",

            expectedHeading:
                "Tell us about the child missing education",

            answers: {
                q_why_removed:
                    "not-returned-after-agreed-leave",

                q_date_removed_from_roll_day:
                    "20",

                q_date_removed_from_roll_month:
                    "01",

                q_date_removed_from_roll_year:
                    "2026",
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
