import type {
    Pupil,
} from "../../../types/pupil.ts";

/*
 * Grades that the revised-grade picker offers for each QAN in the
 * seeded results. They come from
 * Web/Data/QualificationReference/qualification-reference.json,
 * without the status codes (Q, R, U, X).
 */
const REVISABLE_GRADES: Readonly<Record<string, readonly string[]>> = {
    "60146084": ["1", "2", "3", "4", "5", "6", "7", "8", "9"],
    "60148366": ["1", "2", "3", "4", "5", "6", "7", "8", "9"],
    "60149589": ["*", "A", "B", "C", "D", "E", "N"],
    "60172186": ["*", "D", "F", "M", "P"],
    "10025480": ["A", "B", "C", "D", "E"],
};

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

function revisableGrades(
    pupil: Pupil | undefined
): string[] {
    const scale =
        REVISABLE_GRADES[pupil?.resultQan ?? ""] ?? [];

    return scale.filter(
        (grade) => grade !== pupil?.resultGrade
    );
}

/*
 * An incorrect-grade enquiry needs a QAN with a known grade scale,
 * and a grade on that scale that differs from the current one.
 */
export function isRevisableEnquiryPupil(pupil: Pupil): boolean {
    return isEnquiryPupil(pupil) && revisableGrades(pupil).length > 0;
}

export function revisedGradeFor(
    pupil: Pupil | undefined
): string {
    const [grade] = revisableGrades(pupil);

    if (!grade) {
        throw new Error(
            "Selected pupil has no revisable grade"
        );
    }

    return grade;
}
