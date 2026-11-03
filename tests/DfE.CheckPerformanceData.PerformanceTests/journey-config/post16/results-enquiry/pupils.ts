import type {
    Pupil,
} from "../../../types/pupil.ts";

/*
 * Enquiry pupils are the pupils with a result in the results file.
 * Their result keys are set when the pupils are loaded.
 */
export function isEnquiryPupil(pupil: Pupil): boolean {
    return pupil.resultKey !== undefined;
}

export function enquiryResultKey(
    pupil: Pupil | undefined
): string {
    if (!pupil?.resultKey) {
        throw new Error(
            "Selected pupil has no enquiry result key"
        );
    }

    return pupil.resultKey;
}
