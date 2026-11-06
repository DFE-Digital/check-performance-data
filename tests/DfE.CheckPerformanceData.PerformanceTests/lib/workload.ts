import type {
    Options,
} from "k6/options";

import exec from "k6/execution";

import {
    journeyRegistry,
} from "../journey-config/registry.ts";

import type {
    JourneyConfig,
} from "../types/journey-config.ts";

import {
    BASE_URL,
} from "./config.ts";

import {
    buildSchedule,
    defaultLoadWeights,
    findJourney,
    isAddPupil,
    isResultsEnquiry,
    parseJourneyWeights,
} from "./journey-selection.ts";

/*
 * Profiles and their execution model. The profile is
 * chosen with PROFILE. All checks run at init, before
 * any request is sent.
 */
export const PROFILES = [
    "smoke",
    "regression",
    "single",
    "sequential",
    "load",
] as const;

export type ProfileName =
    (typeof PROFILES)[number];

type FixedProfile =
    "smoke" | "regression" | "single";

/* Journey run by PROFILE=smoke. */
const SMOKE_JOURNEY_ID = "ks4-remove-pupil-died";

/*
 * PROVISIONAL response-time threshold, p(95) in ms, for
 * the submit and cleanup transactions. This is a
 * placeholder for review, not an agreed SLO.
 */
const PROVISIONAL_P95_MS = 1000;

/* Add Pupil surnames use 26^3 values per RUN_ID. */
const ADD_PUPIL_ITERATIONS_PER_RUN = 26 ** 3;

const SUBMIT_TRANSACTIONS = [
    "summary-page",
    "submit-amendment-request",
    "confirmation-page",
];

/* Transactions of the cleanup journey, run after amendments. */
const CLEANUP_TRANSACTIONS = [
    "amendment-requests-page",
    "cleanup-request-confirmation-page",
    "cleanup-request-submit",
    "cleanup-request-result-page",
];

/* Variables that only PROFILE=load reads. */
const LOAD_ONLY_VARIABLES = [
    "LOAD_RATE_PER_MINUTE",
    "LOAD_DURATION",
    "LOAD_PREALLOCATED_VUS",
    "LOAD_MAX_VUS",
];

/* Scenario fields the execution model must match. */
type ScenarioFields = Record<string, unknown>;

export interface Workload {
    profile: ProfileName;

    pauseSeconds: number;

    options: Options;

    /* Returns the journey for an iteration. */
    journeyFor(iteration: number): JourneyConfig;

    /* Returns the pupil iteration index for an iteration. */
    pupilIterationFor(iteration: number): number;

    /*
     * Fails when the command line has replaced the
     * scenarios (for example --vus or --iterations).
     * Called from setup(), before any request.
     */
    assertExecutionModel(): void;

    /* Lines printed from setup() to describe the run. */
    describe(): string[];
}

/**
 * Returns an environment variable, or undefined when it
 * is unset or empty.
 */
function env(name: string): string | undefined {
    const value: string | undefined = __ENV[name];

    return value === undefined || value === ""
        ? undefined
        : value;
}

function isProfile(value: string): value is ProfileName {
    return (PROFILES as readonly string[]).includes(value);
}

/**
 * Returns the profile. Without PROFILE, JOURNEY=all
 * means regression and any other JOURNEY means single.
 */
function resolveProfile(): ProfileName {
    const requested = env("PROFILE");
    const journey = env("JOURNEY");

    if (requested === undefined) {
        if (journey === undefined) {
            throw new Error(
                "PROFILE is required: smoke, regression, single, sequential or load. " +
                "Example: -e PROFILE=smoke"
            );
        }

        return journey === "all"
            ? "regression"
            : "single";
    }

    if (!isProfile(requested)) {
        throw new Error(
            `Unknown PROFILE "${requested}". ` +
            "Use smoke, regression, single, sequential or load."
        );
    }

    return requested;
}

/**
 * Checks that JOURNEY matches the profile.
 */
function checkJourneyVariable(
    profile: ProfileName,
    journey: string | undefined
): void {
    if (profile === "smoke" || profile === "load" || profile === "sequential") {
        if (journey !== undefined) {
            throw new Error(
                `JOURNEY is not used by PROFILE=${profile}. ` +
                "Remove it, or use PROFILE=single with JOURNEY=<id>."
            );
        }

        return;
    }

    if (profile === "single") {
        if (journey === undefined) {
            throw new Error(
                "PROFILE=single requires JOURNEY=<id>. " +
                "Example: -e PROFILE=single -e JOURNEY=ks4-remove-pupil-died"
            );
        }

        if (journey === "all") {
            throw new Error(
                "JOURNEY=all requires PROFILE=regression"
            );
        }

        return;
    }

    if (journey !== undefined && journey !== "all") {
        throw new Error(
            "PROFILE=regression runs every journey. " +
            "Use PROFILE=single to run one journey by id."
        );
    }
}

/**
 * Rejects load-only variables under other profiles, so
 * they cannot be set by accident.
 */
function rejectLoadOnlyVariables(
    profile: ProfileName
): void {
    for (const name of ["SEQUENTIAL_ITERATIONS", "SEQUENTIAL_PAUSE_SECONDS"]) {
        if (profile !== "sequential" && env(name) !== undefined) {
            throw new Error(`${name} is only used by PROFILE=sequential`);
        }
    }

    if (profile !== "load" && profile !== "sequential" && env("JOURNEY_WEIGHTS") !== undefined) {
        throw new Error("JOURNEY_WEIGHTS is only used by PROFILE=load or PROFILE=sequential");
    }

    if (profile === "load") {
        return;
    }

    for (const name of LOAD_ONLY_VARIABLES) {
        if (env(name) !== undefined) {
            throw new Error(
                `${name} is only used by PROFILE=load`
            );
        }
    }
}

function positiveInteger(name: string, profile: ProfileName = "load"): number {
    const text = env(name);

    if (text === undefined) {
        throw new Error(
            `${name} is required for PROFILE=${profile}`
        );
    }

    const value = Number(text);

    if (!Number.isInteger(value) || value < 1) {
        throw new Error(
            `${name} must be a whole number of at least 1, got "${text}"`
        );
    }

    return value;
}

/**
 * Parses a duration such as 300s or 5m. Returns the text
 * for k6 and the length in seconds.
 */
function durationFrom(name: string): {
    text: string;
    seconds: number;
} {
    const text = env(name);

    if (text === undefined) {
        throw new Error(
            `${name} is required for PROFILE=load, for example 5m or 300s`
        );
    }

    const match = /^(\d+)([sm])$/.exec(text);

    if (!match) {
        throw new Error(
            `${name} must be a whole number followed by s or m, got "${text}"`
        );
    }

    const amount = Number(match[1]);

    if (amount < 1) {
        throw new Error(
            `${name} must be greater than zero`
        );
    }

    return {
        text,
        seconds:
            match[2] === "m"
                ? amount * 60
                : amount,
    };
}

/**
 * Thresholds shared by every profile. Fixed profiles add
 * an exact completion count.
 */
function thresholdsFor(
    expectedCompletions: number | undefined
): Record<string, string[]> {
    const thresholds: Record<string, string[]> = {
        checks: ["rate==1"],
        http_req_failed: ["rate==0"],
        journey_completion: ["rate==1"],
    };

    if (expectedCompletions !== undefined) {
        thresholds.journeys_completed = [
            `count==${expectedCompletions}`,
        ];
    }

    const transactions = [...SUBMIT_TRANSACTIONS, ...CLEANUP_TRANSACTIONS];

    for (const transaction of transactions) {
        thresholds[
            `http_req_duration{transaction:${transaction}}`
        ] = [`p(95)<${PROVISIONAL_P95_MS}`];
    }

    return thresholds;
}

/**
 * Checks the scenarios that k6 is running against the
 * scenarios this profile defines.
 */
function assertScenarios(
    expected: Record<string, ScenarioFields>
): void {
    const actual = exec.test.options as unknown as {
        scenarios?: Record<string, ScenarioFields>;
    };

    const actualScenarios = actual.scenarios ?? {};

    /*
     * k6 moves --vus, --iterations, --duration and --stages
     * into a scenario named "default", so that name means
     * the command line has replaced the profile's scenarios.
     */
    if (actualScenarios.default !== undefined) {
        throw new Error(
            "The command line overrides the execution model " +
            "(--vus, --iterations, --duration or --stages). " +
            "Remove them; the PROFILE sets them."
        );
    }

    const expectedNames = Object.keys(expected).sort();
    const actualNames = Object.keys(actualScenarios).sort();

    if (expectedNames.join(",") !== actualNames.join(",")) {
        throw new Error(
            "The execution model does not match the profile. " +
            `Expected scenarios: ${expectedNames.join(", ")}; ` +
            `found: ${actualNames.join(", ")}`
        );
    }

    for (const name of expectedNames) {
        const fields = expected[name] ?? {};
        const scenario = actualScenarios[name] ?? {};

        for (const [key, value] of Object.entries(fields)) {
            if (key === "maxDuration" || key === "gracefulStop") {
                continue;
            }

            if (DURATION_FIELDS.has(key)) {
                if (sameDuration(scenario[key], value)) {
                    continue;
                }
            } else if (scenario[key] === value) {
                continue;
            }

            if (DURATION_FIELDS.has(key)) {
                if (sameDuration(scenario[key], value)) {
                    continue;
                }
            } else if (scenario[key] === value) {
                continue;
            }

            throw new Error(
                `The execution model does not match the profile: ` +
                `${name}.${key} is ${JSON.stringify(scenario[key])}, ` +
                `expected ${JSON.stringify(value)}`
            );
        }
    }
}

/*
 * k6 reports durations in Go form, for example "1m0s" for
 * "1m". Keys that hold a duration are compared by length.
 */
const DURATION_FIELDS = new Set(["timeUnit", "duration"]);

/*
 * Converts a duration such as "1m", "1m0s", "10m0s" or
 * "1h2m0.5s" to milliseconds. Returns undefined when the
 * text is not a duration.
 */
function durationMs(text: string): number | undefined {
    const match =
        /^(?:(\d+(?:\.\d+)?)h)?(?:(\d+(?:\.\d+)?)m(?!s))?(?:(\d+(?:\.\d+)?)s)?(?:(\d+(?:\.\d+)?)ms)?$/
            .exec(text);

    if (!match || text === "") {
        return undefined;
    }

    const [, hours, minutes, seconds, millis] = match;

    return (
        Number(hours ?? 0) * 3_600_000 +
        Number(minutes ?? 0) * 60_000 +
        Number(seconds ?? 0) * 1_000 +
        Number(millis ?? 0)
    );
}

function sameDuration(actual: unknown, expected: unknown): boolean {
    if (typeof actual !== "string" || typeof expected !== "string") {
        return false;
    }

    const actualMs = durationMs(actual);

    return (
        actualMs !== undefined &&
        actualMs === durationMs(expected)
    );
}

/**
 * Builds a fixed profile: smoke, regression or single.
 * Each iteration runs the journey at its index, once.
 */
function fixedWorkload(
    profile: FixedProfile,
    journeys: JourneyConfig[]
): Workload {
    const iterations = journeys.length;

    const expectedScenarios: Record<string, ScenarioFields> = {
        [profile]: {
            executor: "shared-iterations",
            vus: 1,
            iterations,
            maxDuration:
                profile === "regression"
                    ? "2h"
                    : "30m",
        },
    };

    const options: Options = {
        scenarios:
            expectedScenarios as unknown as NonNullable<Options["scenarios"]>,

        thresholds:
            thresholdsFor(
                iterations
            ),
    };

    return {
        profile,

        pauseSeconds: 1,

        options,

        journeyFor(iteration) {
            const journey = journeys[iteration];

            if (!journey) {
                throw new Error(
                    `No journey for iteration ${iteration}`
                );
            }

            return journey;
        },

        /*
         * Regression keeps pupil index 0 for every
         * journey, as each journey did in its solo run.
         */
        pupilIterationFor(iteration) {
            return profile === "regression"
                ? 0
                : iteration;
        },

        assertExecutionModel() {
            assertScenarios(expectedScenarios);
        },

        describe() {
            return [
                `Profile: ${profile}`,
                `Iterations: ${iterations}`,
                `Expected completions: ${iterations}`,
            ];
        },
    };
}

function sequentialWorkload(): Workload {
    const iterations = positiveInteger("SEQUENTIAL_ITERATIONS", "sequential");
    const pauseText = env("SEQUENTIAL_PAUSE_SECONDS");
    const pauseSeconds = Number(pauseText);

    if (pauseText === undefined || pauseText.trim() === "" || !Number.isFinite(pauseSeconds) || pauseSeconds < 0) {
        throw new Error("SEQUENTIAL_PAUSE_SECONDS is required and must be a finite number of at least 0");
    }

    const weightsText = env("JOURNEY_WEIGHTS");
    const weights = weightsText === undefined
        ? defaultLoadWeights()
        : parseJourneyWeights(weightsText);
    const schedule = buildSchedule(weights);
    const mix = journeyRegistry.filter((journey) => (weights[journey.id] ?? 0) > 0);

    if (mix.some(isAddPupil) && iterations > ADD_PUPIL_ITERATIONS_PER_RUN) {
        throw new Error(`Add Pupil allows at most ${ADD_PUPIL_ITERATIONS_PER_RUN} iterations per RUN_ID`);
    }

    const expectedScenarios: Record<string, ScenarioFields> = {
        sequential: {
            executor: "shared-iterations",
            vus: 1,
            iterations,
            maxDuration: "2h",
        },
    };

    return {
        profile: "sequential",
        pauseSeconds,
        options: {
            scenarios: expectedScenarios as unknown as NonNullable<Options["scenarios"]>,
            thresholds: thresholdsFor(iterations),
        },
        journeyFor(iteration) {
            return schedule[iteration % schedule.length]!;
        },
        pupilIterationFor(iteration) {
            return iteration;
        },
        assertExecutionModel() {
            assertScenarios(expectedScenarios);
        },
        describe() {
            return [
                "Profile: sequential",
                "Virtual users: 1",
                `Iterations: ${iterations}`,
                `Expected completions: ${iterations}`,
                `Pause between journeys: ${pauseSeconds}s`,
                "Maximum duration: 2h (including pauses)",
                ...mix.map((journey) => `  ${journey.id}: weight ${weights[journey.id] ?? 0}`),
                "Default mix is PROVISIONAL unless JOURNEY_WEIGHTS is set.",
            ];
        },
    };
}

/**
 * Builds PROFILE=load. It runs a weighted, deterministic
 * schedule at a constant arrival rate.
 */
function loadWorkload(): Workload {
    const ratePerMinute =
        positiveInteger("LOAD_RATE_PER_MINUTE");

    const duration =
        durationFrom("LOAD_DURATION");

    const preAllocatedVUs =
        positiveInteger("LOAD_PREALLOCATED_VUS");

    const maxVUs =
        positiveInteger("LOAD_MAX_VUS");

    if (preAllocatedVUs > maxVUs) {
        throw new Error(
            "LOAD_PREALLOCATED_VUS cannot exceed LOAD_MAX_VUS"
        );
    }

    const weightsText = env("JOURNEY_WEIGHTS");

    const weights =
        weightsText === undefined
            ? defaultLoadWeights()
            : parseJourneyWeights(weightsText);

    const schedule = buildSchedule(weights);

    const mix = journeyRegistry.filter(
        (journey) => (weights[journey.id] ?? 0) > 0
    );

    const expectedIterations =
        Math.ceil(ratePerMinute * duration.seconds / 60);

    if (
        mix.some(isAddPupil) &&
        expectedIterations > ADD_PUPIL_ITERATIONS_PER_RUN
    ) {
        throw new Error(
            `The run would start about ${expectedIterations} iterations, ` +
            `but Add Pupil surnames allow ${ADD_PUPIL_ITERATIONS_PER_RUN} per RUN_ID. ` +
            "Lower the rate or duration, or remove ks4-add-pupil from the mix."
        );
    }

    const expectedScenarios: Record<string, ScenarioFields> = {
        load: {
            executor: "constant-arrival-rate",
            rate: ratePerMinute,
            timeUnit: "1m",
            duration: duration.text,
            preAllocatedVUs,
            maxVUs,
        },
    };

    const options: Options = {
        scenarios:
            expectedScenarios as unknown as NonNullable<Options["scenarios"]>,

        thresholds: {
            ...thresholdsFor(undefined),
            dropped_iterations: ["count==0"],
        },
    };

    const addPupilShare =
        schedule.filter(isAddPupil).length /
        schedule.length;

    const resultsEnquiryShare =
        schedule.filter(isResultsEnquiry).length /
        schedule.length;

    const mixLines = mix.map(
        (journey) =>
            `  ${journey.id}: weight ${weights[journey.id] ?? 0}`
    );

    return {
        profile: "load",

        pauseSeconds: 1,

        options,

        journeyFor(iteration) {
            return schedule[iteration % schedule.length]!;
        },

        pupilIterationFor(iteration) {
            return iteration;
        },

        assertExecutionModel() {
            assertScenarios(expectedScenarios);
        },

        describe() {
            return [
                "Profile: load",
                `Rate: ${ratePerMinute} iterations per minute for ${duration.text}`,
                `Expected iterations: about ${expectedIterations}`,
                `Virtual users: pre-allocated ${preAllocatedVUs}, max ${maxVUs}`,
                `Journey mix (${schedule.length} schedule slots):`,
                ...mixLines,
                `Expected ks4-add-pupil iterations: about ${Math.round(expectedIterations * addPupilShare)}`,
                `Expected results enquiry iterations: about ${Math.round(expectedIterations * resultsEnquiryShare)}`,
                "Default mix is PROVISIONAL unless JOURNEY_WEIGHTS is set.",
            ];
        },
    };
}

/**
 * Resolves the profile from the environment. Throws on
 * any invalid configuration. Call at init, so the run
 * stops before any request is sent.
 */
export function resolveWorkload(): Workload {
    if (!/^https?:\/\/[^/\s]+/.test(BASE_URL)) {
        throw new Error(
            `BASE_URL must be an http or https URL, got "${BASE_URL}"`
        );
    }

    const profile = resolveProfile();
    const journey = env("JOURNEY");

    checkJourneyVariable(profile, journey);
    rejectLoadOnlyVariables(profile);

    switch (profile) {
        case "smoke":
            return fixedWorkload(
                "smoke",
                [findJourney(SMOKE_JOURNEY_ID)]
            );

        case "single":
            return fixedWorkload(
                "single",
                [findJourney(journey ?? "")]
            );

        case "regression":
            return fixedWorkload(
                "regression",
                [...journeyRegistry]
            );

        case "sequential":
            return sequentialWorkload();

        case "load":
            return loadWorkload();
    }
}
