import type {
    Pupil,
} from "../types/pupil.ts";

/*
 * Removals and merges need an included pupil. The
 * INCLUDED stamp sets the inclusion code (see mapKs2Record).
 */
export function isIncluded(
    pupil: Pupil
): boolean {
    return pupil.inclusionCode === "INCLUDED";
}
