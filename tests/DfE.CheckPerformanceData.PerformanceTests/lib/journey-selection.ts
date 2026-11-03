import {
    journeyRegistry,
} from "../journey-config/registry.ts";

import type {
    JourneyConfig,
} from "../types/journey-config.ts";

import type {
    WindowKey,
} from "../types/window.ts";

/* Upper bound on each weight and on the total, so the schedule stays small. */
const MAX_WEIGHT = 1000;
const MAX_TOTAL_WEIGHT = 10000;

/**
 * Returns the journey with the given id.
 */
export function findJourney(
    id: string
): JourneyConfig {
    const journey =
        journeyRegistry.find(
            (candidate) =>
                candidate.id === id
        );

    if (!journey) {
        const available =
            journeyRegistry
                .map((candidate) => candidate.id)
                .join(", ");

        throw new Error(
            `Unknown journey "${id}". ` +
            `Available: ${available}`
        );
    }

    return journey;
}

/**
 * Results Enquiry journeys can be submitted as many
 * times as needed, so they are not cleaned up.
 */
export function isResultsEnquiry(
    journey: JourneyConfig
): boolean {
    return journey.journeyType === "ResultsEnquiry";
}

/**
 * Add Pupil creates a pupil record whose surname is
 * generated from RUN_ID and the iteration. It can be
 * submitted as many times as needed, so it is not
 * cleaned up.
 */
export function isAddPupil(
    journey: JourneyConfig
): boolean {
    return journey.journeyType === "Add";
}

/**
 * Amendment journeys need cleanup. The application
 * allows only one amendment request per pupil, so each
 * submitted amendment is cleaned up to let the same
 * pupil be amended again.
 */
export function needsCleanup(
    journey: JourneyConfig
): boolean {
    return !isResultsEnquiry(journey) && !isAddPupil(journey);
}

/**
 * Returns the checking window of a journey. Journeys
 * without a window run in KS4.
 */
export function windowOf(
    journey: JourneyConfig
): WindowKey {
    return journey.window ?? "ks4";
}

/**
 * Default load mix. Gives weight 1 to every amendment
 * journey that needs cleanup and 0 to Add Pupil and
 * Results Enquiry, which are excluded from the default
 * mix.
 *
 * PROVISIONAL: a starting point, not an agreed traffic
 * distribution. Override with JOURNEY_WEIGHTS.
 */
export function defaultLoadWeights():
    Record<string, number> {

    const weights: Record<string, number> = {};

    for (const journey of journeyRegistry) {
        weights[journey.id] =
            needsCleanup(journey) ? 1 : 0;
    }

    return weights;
}

/**
 * Parses JOURNEY_WEIGHTS, a JSON object of journey id to
 * integer weight. It replaces the default mix: journeys
 * not listed get weight 0.
 */
export function parseJourneyWeights(
    text: string
): Record<string, number> {
    let parsed: unknown;

    try {
        parsed = JSON.parse(text);
    } catch {
        throw new Error(
            "JOURNEY_WEIGHTS must be a JSON object, for example " +
            '{"remove-pupil-died":2,"include-pupil":1}'
        );
    }

    if (
        typeof parsed !== "object" ||
        parsed === null ||
        Array.isArray(parsed)
    ) {
        throw new Error(
            "JOURNEY_WEIGHTS must be a JSON object of journey id to weight"
        );
    }

    const weights: Record<string, number> = {};

    for (const journey of journeyRegistry) {
        weights[journey.id] = 0;
    }

    for (const [id, value] of Object.entries(parsed)) {
        findJourney(id);

        if (
            typeof value !== "number" ||
            !Number.isInteger(value) ||
            value < 0 ||
            value > MAX_WEIGHT
        ) {
            throw new Error(
                `Weight for "${id}" must be an integer from 0 to ${MAX_WEIGHT}`
            );
        }

        weights[id] = value;
    }

    return weights;
}

/**
 * Expands weights into a schedule. Iteration i runs
 * schedule[i % schedule.length]. The order follows the
 * registry, so the same weights always give the same
 * schedule.
 */
export function buildSchedule(
    weights: Record<string, number>
): JourneyConfig[] {
    const schedule: JourneyConfig[] = [];

    for (const journey of journeyRegistry) {
        const weight = weights[journey.id] ?? 0;

        for (let index = 0; index < weight; index++) {
            schedule.push(journey);
        }
    }

    const total = schedule.length;

    if (total === 0) {
        throw new Error(
            "The journey mix is empty: at least one journey needs a weight above 0"
        );
    }

    if (total > MAX_TOTAL_WEIGHT) {
        throw new Error(
            `The journey mix totals ${total}; the maximum is ${MAX_TOTAL_WEIGHT}`
        );
    }

    return schedule;
}
