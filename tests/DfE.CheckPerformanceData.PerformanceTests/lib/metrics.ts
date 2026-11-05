import {
    Counter,
    Rate,
} from "k6/metrics";

/*
 * One sample per journey iteration. True when the journey
 * completed and, for amendment journeys, the request was
 * cleaned up. False when the iteration failed.
 *
 * Samples carry the journey, journey_type and window tags
 * set in tests/journey.ts.
 */
export const journeyCompletion =
    new Rate("journey_completion");

/*
 * Incremented once per completed journey. Used for the
 * exact-count threshold in fixed profiles.
 */
export const journeysCompleted =
    new Counter("journeys_completed");
