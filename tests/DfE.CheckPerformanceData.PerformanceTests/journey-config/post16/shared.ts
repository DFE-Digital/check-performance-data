import type {
    Pupil,
} from "../../types/pupil.ts";

/*
 * 16 to 19 searches offer every student, but removals
 * and merges need an included student. The suggestions
 * default excludes non-included records.
 */
export function isIncluded(
    pupil: Pupil
): boolean {
    return pupil.inclusionCode === "INCLUDED";
}
