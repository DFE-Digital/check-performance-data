import type {
    Pupil,
} from "../types/pupil.ts";

/*
 * For KS2 and 16 to 19 only. Their mappers set INCLUDED
 * (see mapKs2Record and mapPost16Record). 16 to 19 searches
 * offer every student, but removals and merges need an
 * included student. KS4 uses its own rule (ks4/shared.ts).
 */
export function isIncluded(
    pupil: Pupil
): boolean {
    return pupil.inclusionCode === "INCLUDED";
}
