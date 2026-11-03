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

        pupilBlob:
            "exercises/71e0ee21-3f3f-4ba6-945d-a1b1edc1bf3b/releases/1d8dba47-63e4-48f7-b029-039f50b58cd9/data/8604070_pupils.json",

        checkPupilDataHeading:
            "Check your pupil data",

        whatToChangeHeading:
            "What pupil data would you like to change?",
    },

    post16: {
        key: "post16",

        windowId:
            "ec6493b8-9b66-4090-8be6-9dfc3805751f",

        pupilBlob:
            "exercises/41476d6c-20b6-420b-887c-a531bea3c74c/releases/5ac44d35-81a3-490d-8e5f-424cec112335/data/8604070_pupils.json",

        resultsBlob:
            "exercises/346ccd29-e9fb-4e33-a47a-da9210c8e305/releases/48b54c09-2bc0-4f61-8993-0d0474d2569c/data/8604070_results.json",

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
