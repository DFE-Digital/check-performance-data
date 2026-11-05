import type {
    JourneyConfig,
} from "../../../types/journey-config.ts";

import type {
    Pupil,
} from "../../../types/pupil.ts";

/*
 * Include a pupil who is not currently in the data.
 *
 * Only pupils that the INCLUDABLE stamp allows are used.
 * The pupil is selected by iteration from that list.
 */
export const includePupil = {
    id: "include-pupil",
    journeyType: "Include",
    reason: "Include a pupil in data",

    pupilSearch: {
        pageId: "select-pupil",

        expectedHeading:
            "What is the name of the pupil to be included?",

        expectedRedirect:
            "/page/evidence",
    },

    pupilFilter: (pupil: Pupil): boolean =>
        pupil.includable,

    completion: "submit",

    steps: [
        {
            pageId: "evidence",

            expectedHeading:
                "Provide evidence for the inclusion of",

            // No evidence file. The text field is optional.
            answers: {
                q_how_evidence_supports:
                    "",
            },

            expectedRedirect:
                "/summary",
        },
    ],
} satisfies JourneyConfig;
