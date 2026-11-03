import type {
    CompletionMode,
    JourneyConfig,
} from "./journey-config.ts";

import type {
    Pupil,
} from "./pupil.ts";

import type {
    EvidenceFile,
} from "./evidence.ts";

import type {
    WindowProfile,
} from "./window.ts";

export interface RunJourneyOptions {
    baseUrl: string;

    /*
     * Checking window the journey runs in.
     */
    profile: WindowProfile;

    /*
     * Required when the journey has a pupilSearch step.
     */
    pupil?: Pupil | undefined;

    /*
     * Iteration number. Passed to step answers that are
     * functions.
     */
    iteration: number;

    /*
     * Required when the journey has a secondPupilSearch
     * step. Must be a different pupil from pupil.
     */
    secondPupil?: Pupil | undefined;

    /*
     * Required when any step has uploadEvidence set.
     */
    evidenceFile?: EvidenceFile;

    config: JourneyConfig;
}

export interface JourneyResult {
    /*
     * Reference of the submitted request or of the
     * saved draft. Used for cleanup.
     */
    referenceNumber: string;

    completion: CompletionMode;
}
