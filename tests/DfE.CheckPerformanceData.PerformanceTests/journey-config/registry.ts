import type {
    JourneyConfig,
} from "../types/journey-config.ts";

import {
    removePupilDied,
} from "./ks4/remove/remove-pupil-died.ts";

import {
    removeDualRegisteredMoved,
} from "./ks4/remove/remove-dual-registered-moved.ts";

import {
    removeYearGroupChangeHigher,
} from "./ks4/remove/remove-year-group-change-higher.ts";

import {
    removeYearGroupChangeLower,
} from "./ks4/remove/remove-year-group-change-lower.ts";

import {
    removeChildMissingEducation,
} from "./ks4/remove/remove-child-missing-education.ts";

import {
    removeLifeLimitingIllness,
} from "./ks4/remove/remove-life-limiting-illness.ts";

import {
    removeSocialCareInvolvement,
} from "./ks4/remove/remove-social-care-involvement.ts";

import {
    removePermanentlyLeftEngland,
} from "./ks4/remove/remove-permanently-left-england.ts";

import {
    removeElectiveHomeEducation,
} from "./ks4/remove/remove-elective-home-education.ts";

import {
    removeAdmittedFollowingExclusion,
} from "./ks4/remove/remove-admitted-following-exclusion.ts";

import {
    removeAdmittedFromAbroad,
} from "./ks4/remove/remove-admitted-from-abroad.ts";

import {
    removePermanentlyExcluded,
} from "./ks4/remove/remove-permanently-excluded.ts";

import {
    mergeDuplicatePupils,
} from "./ks4/merge/merge-duplicate-pupils.ts";

import {
    includePupil,
} from "./ks4/include/include-pupil.ts";

import {
    addPupil,
} from "./ks4/add/add-pupil.ts";

import {
    post16RemoveStudentDied,
} from "./post16/remove/remove-student-died.ts";

import {
    post16RemoveNotAtEnd,
} from "./post16/remove/remove-not-at-end.ts";

import {
    post16RemoveNotOnRoll,
} from "./post16/remove/remove-not-on-roll.ts";

import {
    post16RemoveOther,
} from "./post16/remove/remove-other.ts";

import {
    post16MergeDuplicateStudents,
} from "./post16/merge/merge-duplicate-students.ts";

import {
    post16ResultsEnquiryIncorrectGradeNotWholeCohort,
} from "./post16/results-enquiry/incorrect-grade-not-whole-cohort.ts";

import {
    post16ResultsEnquiryIncorrectGradeWholeCohort,
} from "./post16/results-enquiry/incorrect-grade-whole-cohort.ts";

import {
    post16ResultsEnquiryMissingQualificationNotWholeCohort,
} from "./post16/results-enquiry/missing-qualification-not-whole-cohort.ts";

import {
    post16ResultsEnquiryMissingQualificationWholeCohort,
} from "./post16/results-enquiry/missing-qualification-whole-cohort.ts";

import {
    post16ResultsEnquiryResultDoesNotBelong,
} from "./post16/results-enquiry/result-does-not-belong.ts";

/*
 * Every journey that can be run. Add new journeys here.
 *
 * PROFILE selects how journeys are run (see lib/workload.ts).
 * regression runs each journey once in this order. single
 * runs the id given in JOURNEY. load draws from a weighted
 * mix (JOURNEY_WEIGHTS). Journeys are only run by id.
 */
export const journeyRegistry: JourneyConfig[] = [
    removePupilDied,
    removeDualRegisteredMoved,
    removeYearGroupChangeHigher,
    removeYearGroupChangeLower,
    removeChildMissingEducation,
    removeLifeLimitingIllness,
    removeSocialCareInvolvement,
    removePermanentlyLeftEngland,
    removeElectiveHomeEducation,
    removeAdmittedFollowingExclusion,
    removeAdmittedFromAbroad,
    removePermanentlyExcluded,
    mergeDuplicatePupils,
    includePupil,
    addPupil,
    post16RemoveStudentDied,
    post16RemoveNotAtEnd,
    post16RemoveNotOnRoll,
    post16RemoveOther,
    post16MergeDuplicateStudents,
    post16ResultsEnquiryIncorrectGradeNotWholeCohort,
    post16ResultsEnquiryIncorrectGradeWholeCohort,
    post16ResultsEnquiryMissingQualificationNotWholeCohort,
    post16ResultsEnquiryMissingQualificationWholeCohort,
    post16ResultsEnquiryResultDoesNotBelong,
];
