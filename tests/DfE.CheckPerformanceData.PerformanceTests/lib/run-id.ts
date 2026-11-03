/*
 * RUN_ID names one test run. Add Pupil derives its
 * generated surname from it, so separate runs do not
 * add the same pupil.
 *
 * setup() chooses the run ID once, from RUN_ID or a
 * generated value. Each VU then stores it with
 * setRunId() before it runs an iteration.
 */
const RUN_ID_PATTERN = /^[A-Za-z0-9_-]{1,32}$/;

let activeRunId: string | undefined;

/**
 * Returns RUN_ID when it is set, or a generated run ID.
 * Throws when RUN_ID is set but not valid.
 */
export function chooseRunId(): string {
    const supplied = __ENV.RUN_ID;

    if (supplied === undefined || supplied === "") {
        return generateRunId();
    }

    if (!RUN_ID_PATTERN.test(supplied)) {
        throw new Error(
            "RUN_ID must be 1-32 letters, digits, '-' or '_'. " +
            "Example: -e RUN_ID=run-01"
        );
    }

    return supplied;
}

/**
 * Stores the run ID for this VU.
 */
export function setRunId(runId: string): void {
    activeRunId = runId;
}

/**
 * Returns the run ID stored for this VU. Throws when
 * setRunId() has not run.
 */
export function getRunId(): string {
    if (activeRunId === undefined) {
        throw new Error("The run ID has not been set for this VU");
    }

    return activeRunId;
}

function generateRunId(): string {
    const time = Date.now().toString(36);
    const random = Math.floor(Math.random() * 36 ** 4)
        .toString(36)
        .padStart(4, "0");

    return `auto-${time}-${random}`;
}
