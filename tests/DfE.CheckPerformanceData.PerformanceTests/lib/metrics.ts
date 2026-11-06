import {
    Counter,
    Rate,
    Trend,
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

/*
 * Business metrics, shown at the top of the HTML report.
 *
 * Journey duration covers the journey steps from the first
 * request to the submit or save. It excludes authentication,
 * pupil selection and cleanup. One sample per successful
 * journey.
 */
export const journeyDuration =
    new Trend("journey_duration");

/*
 * Incremented once for each request that is submitted.
 */
export const submissions =
    new Counter("submissions");

/*
 * One sample for each pupil search. A merge journey adds two
 * samples, one for each search.
 */
export const pupilSearchDuration =
    new Trend("pupil_search_duration");

export const evidenceUploadDuration =
    new Trend("evidence_upload_duration");

export const submissionDuration =
    new Trend("submission_duration");

/*
 * Runs fn and adds its elapsed time, in milliseconds, to the
 * trend. A sample is recorded only when fn returns, so a
 * failed step adds no sample.
 */
export function timed<T>(
    trend: Trend,
    fn: () => T
): T {
    const started = Date.now();

    const result = fn();

    trend.add(Date.now() - started);

    return result;
}
