import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

export const removePupilDied = {
    id: "remove-pupil-died",
    journeyType: "Remove",
    reason: "Pupil died",

    pupilSearch: {
        pageId: "select-pupil",

        expectedHeading:
            "What is the name of the pupil to be removed?",

        expectedRedirect:
            "/page/reason",
    },

    completion: "submit",

    steps: [
        {
            pageId: "reason",

            expectedHeading:
                "Why are you asking to remove",

            answers: {
                Q_Reason:
                    "pupil-died",
            },

            expectedRedirect:
                "page/pupil-died",
        },

        {
            pageId: "pupil-died",

            expectedHeading:
                "removed from your school roll",

            answers: {
                q_date_removed_from_roll_day:
                    "01",

                q_date_removed_from_roll_month:
                    "01",

                q_date_removed_from_roll_year:
                    "2026",
            },

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;