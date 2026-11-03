import { sleep } from "k6";
import exec from "k6/execution";

import {
    runJourney,
} from "../journeys/run-journey.ts";

import {
    cleanupRequest,
} from "../journeys/cleanup-request.ts";

import {
    BASE_URL,
} from "../lib/config.ts";

import {
    loadPupils,
} from "../lib/test-data.ts";

import {
    windowProfileFor,
} from "../lib/windows.ts";

import type {
    WindowKey,
} from "../types/window.ts";

import {
    authenticate,
} from "../lib/auth.ts";

import {
    getPupilForCurrentIteration,
    getSecondPupilForCurrentIteration,
} from "../lib/pupil-selection.ts";

import {
    needsCleanup,
    windowOf,
} from "../lib/journey-selection.ts";

import {
    journeyCompletion,
    journeysCompleted,
} from "../lib/metrics.ts";

import {
    resolveWorkload,
} from "../lib/workload.ts";

import type {
    Pupil,
} from "../types/pupil.ts";

/*
 * Evidence file uploaded by steps with uploadEvidence.
 * k6 resolves open() paths relative to this script.
 * Override with -e EVIDENCE_FILE=<path>.
 */
const EVIDENCE_FILE_PATH: string =
    __ENV.EVIDENCE_FILE ?? "../data/evidence-upload.pdf";

const evidenceFile = {
    name:
        EVIDENCE_FILE_PATH.split("/").pop() ??
        EVIDENCE_FILE_PATH,

    contentType: "application/pdf",

    data: open(EVIDENCE_FILE_PATH, "b"),
};

/*
 * Resolved at init. An invalid configuration throws here,
 * before setup() and before any request is sent.
 */
const workload = resolveWorkload();

export const options = workload.options;

interface TestData {
    pupilsByWindow: Record<WindowKey, Pupil[]>;
}

export function setup(): TestData {
    /*
     * Fails before any request when the command line has
     * changed the execution model (for example --vus).
     */
    workload.assertExecutionModel();

    for (const line of workload.describe()) {
        console.log(line);
    }

    /*
     * setup() needs admin access because the pupil
     * data file is exposed through the admin storage
     * download endpoint.
     */
    authenticate(
        BASE_URL,
        "admin"
    );

    const pupilsByWindow: Record<WindowKey, Pupil[]> = {
        ks4:
            loadPupils(
                BASE_URL,
                "ks4"
            ),

        post16:
            loadPupils(
                BASE_URL,
                "post16"
            ),
    };

    for (const key of Object.keys(pupilsByWindow) as WindowKey[]) {
        const count =
            pupilsByWindow[key].length;

        console.log(
            `Loaded ${count} pupils for ${key}`
        );

        if (count === 0) {
            throw new Error(
                `No pupils were returned for ${key}`
            );
        }
    }

    return {
        pupilsByWindow,
    };
}

export default function (
    data: TestData
): void {
    const iteration =
        exec.scenario.iterationInTest;

    const config =
        workload.journeyFor(iteration);

    /*
     * Tag every metric from this iteration, including
     * authentication, so results and thresholds can be
     * read per journey.
     */
    exec.vu.metrics.tags.journey =
        config.id;

    exec.vu.metrics.tags.journey_type =
        config.journeyType;

    exec.vu.metrics.tags.window =
        windowOf(config);

    /*
     * setup() authentication is not sufficient for
     * journey VUs. Each VU establishes its own editor
     * session before making journey requests.
     */
    authenticate(
        BASE_URL,
        "editor"
    );

    const profile =
        windowProfileFor(
            config.window
        );

    console.log(
        `Journey: ${config.journeyType} - ${config.reason}`
    );

    /*
     * Journeys may restrict the pupils they use. The
     * filter is applied before choosing by iteration.
     */
    const windowPupils =
        data.pupilsByWindow[profile.key];

    const pupils =
        config.pupilFilter
            ? windowPupils.filter(
                config.pupilFilter
            )
            : windowPupils;

    /*
     * Regression uses pupil index 0 for every journey,
     * matching each journey's solo run.
     */
    const pupilIteration =
        workload.pupilIterationFor(iteration);

    /*
     * Only journeys with a pupil search select a pupil.
     */
    const pupil =
        config.pupilSearch
            ? getPupilForCurrentIteration(
                pupils,
                pupilIteration
            )
            : undefined;

    if (pupil) {
        console.log(
            "Using pupil " +
            `${pupil.firstName} ` +
            `${pupil.surname} ` +
            `${pupil.dateOfBirth} ` +
            `(${pupil.id})`
        );
    }

    const secondPupil =
        config.secondPupilSearch
            ? getSecondPupilForCurrentIteration(
                pupils,
                pupilIteration
            )
            : undefined;

    if (secondPupil) {
        console.log(
            "Using second pupil " +
            `${secondPupil.firstName} ` +
            `${secondPupil.surname} ` +
            `${secondPupil.dateOfBirth} ` +
            `(${secondPupil.id})`
        );
    }

    try {
        const result =
            runJourney({
                baseUrl:
                    BASE_URL,

                profile,

                pupil,

                secondPupil,

                iteration,

                evidenceFile,

                config,
            });

        console.log(
            `Completed (${result.completion}): ` +
            `reference ${result.referenceNumber}`
        );

        /*
         * Amendment journeys are cleaned up after submission
         * so the same pupil can be amended again. Add Pupil
         * and Results Enquiry are not cleaned up because
         * they can be submitted as many times as needed.
         */
        if (needsCleanup(config)) {
            cleanupRequest({
                baseUrl:
                    BASE_URL,

                windowId:
                    profile.windowId,

                requestReferenceNumber:
                    result.referenceNumber,
            });

            console.log(
                `Cleaned up request ${result.referenceNumber}`
            );
        }

        journeysCompleted.add(1);
        journeyCompletion.add(true);
    } catch (error) {
        journeyCompletion.add(false);

        if (workload.profile === "sequential") {
            sleep(workload.pauseSeconds);
        }

        throw error;
    }

    sleep(workload.pauseSeconds);
}