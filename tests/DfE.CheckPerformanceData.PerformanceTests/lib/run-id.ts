import type {
    JourneyConfig,
} from "../types/journey-config.ts";

import {
    isAddPupil,
} from "./journey-selection.ts";

/*
 * RUN_ID names one test run. Add Pupil derives its
 * generated surname from it, so separate runs do not
 * add the same pupil.
 */
const RUN_ID_PATTERN = /^[A-Za-z0-9_-]{1,32}$/;

/**
 * Returns true when any of the journeys is Add Pupil.
 */
export function requiresRunId(
    journeys: JourneyConfig[]
): boolean {
    return journeys.some(isAddPupil);
}

/**
 * Returns RUN_ID. Throws when it is missing or invalid.
 */
export function getRunId(): string {
    const runId = __ENV.RUN_ID;

    if (!runId || !RUN_ID_PATTERN.test(runId)) {
        throw new Error(
            "RUN_ID is required for add-pupil: 1-32 letters, " +
            "digits, '-' or '_'. Example: -e RUN_ID=run-01"
        );
    }

    return runId;
}
