import type {
    Pupil,
} from "../types/pupil";

export function getPupilForCurrentIteration(
    pupils: Pupil[],
    iteration: number,
): Pupil {

    if (pupils.length === 0) {
        throw new Error(
            "No pupils available",
        );
    }

    const index =
        iteration %
        pupils.length;

    return pupils[index];
}

/**
 * Returns the pupil that follows the iteration's pupil
 * in the list. Used by journeys that select two
 * different pupils. Requires at least two pupils.
 */
export function getSecondPupilForCurrentIteration(
    pupils: Pupil[],
    iteration: number,
): Pupil {

    if (pupils.length < 2) {
        throw new Error(
            "At least two pupils are required",
        );
    }

    const index =
        iteration %
        pupils.length;

    return pupils[(index + 1) % pupils.length];
}

/**
 * Returns the pupil's name as shown in amendment
 * request tables.
 */
export function pupilFullName(
    pupil: Pupil
): string {
    return `${pupil.firstName} ${pupil.surname}`;
}