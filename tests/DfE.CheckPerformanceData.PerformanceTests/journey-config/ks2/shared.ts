import type {
    Pupil,
} from "../../types/pupil.ts";

/*
 * KS2 removals need an included pupil. The INCLUDED
 * stamp sets the inclusion code (see mapKs2Record).
 */
export function isIncluded(
    pupil: Pupil
): boolean {
    return pupil.inclusionCode === "INCLUDED";
}
