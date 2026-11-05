import type {
    Pupil,
} from "../../types/pupil.ts";

export function isNotAddBack(pupil: Pupil): boolean {
    return pupil.inclusionCode !== "403";
}