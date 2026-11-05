import type {
    WindowKey,
    WindowProfile,
} from "../types/window.ts";

/*
 * Window profiles. Add a window here to run journeys
 * against it.
 *
 * The 16 to 19 window uses a different window id,
 * a different pupil blob schema and different page
 * wording from KS4.
 */
export const WINDOW_PROFILES: Record<WindowKey, WindowProfile> = {
    ks4: {
        key: "ks4",

        windowId:
            "f34d285b-8660-4d12-9c30-787328deaa0a",

        pupilFile:
            "8604070_pupils.json",

        checkPupilDataHeading:
            "Check your pupil data",

        whatToChangeHeading:
            "What pupil data would you like to change?",
    },

    post16: {
        key: "post16",

        windowId:
            "ec6493b8-9b66-4090-8be6-9dfc3805751f",

        pupilFile:
            "8604070_pupils.json",

        resultsFile:
            "8604070_results.json",

        checkPupilDataHeading:
            "Check your student data",

        whatToChangeHeading:
            "What student data would you like to change?",
    },
};

/**
 * Returns the profile for a journey's window. Journeys
 * without a window use KS4.
 */
export function windowProfileFor(
    key: WindowKey | undefined
): WindowProfile {
    return WINDOW_PROFILES[key ?? "ks4"];
}
