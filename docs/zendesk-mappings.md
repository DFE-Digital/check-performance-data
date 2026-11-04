# Zendesk ticket mappings

This document lists each value that the worker writes to a Zendesk ticket, and where the value
comes from. `ZendeskConsumer` builds the ticket. `ZendeskTicketFieldOptions` holds the option
values. For the route from decision to ticket, see [rules-engine.md](./rules-engine.md).

## Key code

| File | Contents |
| --- | --- |
| `RulesEngineWorker/Consumers/ZendeskConsumer.cs` | Builds the ticket (`BuildTicket`) |
| `Application/ZendeskClient/ZendeskTicketFieldConstants.cs` | Zendesk field names |
| `Application/ZendeskClient/ZendeskTicketFieldOptions.cs` | Option values and the maps to them |
| `Application/ZendeskClient/ZendeskTicketFieldSettings.cs` | Field ids from configuration |
| `RulesEngineWorker/appsettings.json` | `ZendeskTicketFields` section: one `*Id` per field |

## General rules

- **A field id of 0 or no id leaves the field out.** `GetConfiguredFieldId` treats 0 as "not
  set". Zendesk rejects the whole ticket (422) for a custom field with id 0.
- **An empty value leaves the field out.** The worker does not send a blank or a 0.
- **A value with no option leaves the field out and logs a warning.** The ticket is still
  created. Search the worker logs for "skipping field" to find these.
- **An integer field gets only an integer.** Zendesk rejects the whole ticket for an invalid
  integer. The worker leaves out `CYPMD_ID` or `Pupil year group (CYPMD)` when the value does not
  parse.

## Request types

The request document's `RequestTypeCode` is the `WhatToChange` name. A Remove request adds the
reason option value from the flow file, for example `Remove - pupil-died`.

| `RequestTypeCode` | Journey |
| --- | --- |
| `Add` | Add a pupil (new learner) |
| `Merge` | Merge two pupils |
| `Include` | Include a pupil |
| `Remove - {reason}` | Remove a pupil |
| `IncorrectGrade`, `MissingQualification`, `ResultDoesNotBelong` | 16-19 results enquiries |

## Ticket properties

| Property | Scrutiny | AutoApproved | AutoRejected |
| --- | --- | --- | --- |
| Subject | `CPMD Requires Scrutiny: {outcome} ({ref})` | `CPMD Auto-Approved: {outcome} ({ref})` | `CPMD Auto-Rejected: {outcome} ({ref})` |
| Status | `new` | `solved` | `solved` |
| Priority | `high` | `normal` | `normal` |
| Type | `question` | `task` | `task` |
| Group | `SchoolCheckingExercise:ScrutinyGroupId` | `SchoolCheckingExercise:GroupId` | `SchoolCheckingExercise:GroupId` |
| Brand | `SchoolCheckingExercise:BrandId` | same | same |

`{outcome}` is the rules engine outcome key. `{ref}` is the request reference number. If
`ScrutinyGroupId` is not set or is 0, a Scrutiny ticket goes to `GroupId`.

The description holds the reference, window id and type, outcome, decision, matched rule and
each answer. An add-back removal also shows the pupil's Pincl. The description never holds the
rules engine trace.

The worker attaches each file that the school uploaded as evidence. A failed upload is logged.
It does not stop the ticket.

## Custom fields

The setting column is the key in the `ZendeskTicketFields` section. The id is the value in
`RulesEngineWorker/appsettings.json`. Production sets each id to 0 until the ids are confirmed.

| Field | Setting | Field id | Value | Sent on |
| --- | --- | --- | --- | --- |
| Decision status | `DecisionStatusId` | 19056253670034 | See [Decision status](#decision-status) | Each ticket |
| School URN | `SchoolUrnId` | 17207989310226 | `School.Urn` | Each ticket |
| CYPMD_ID | `CypmdId` | 20433125966866 | `Pupil.CypmdId`, only when it is an integer | Each ticket |
| UPN | `UpnId` | 17207966711570 | `Pupil.Upn` in upper case | Each ticket |
| Surname (CYPMD) | `SurnameCypmdId` | 19058409672594 | `Pupil.Surname` in upper case | When present |
| Forename (CYPMD) | `ForenameCypmdId` | 19058507283218 | `Pupil.Firstname` in upper case | When present |
| Date of birth (CYPMD) | `DateOfBirthCypmdId` | 17208002901906 | `Pupil.DateOfBirth` (`dd/MM/yyyy`) as `yyyy-MM-dd` | When it parses |
| Sex | `SexId` | 19058550118802 | See [Sex](#sex) | When it maps |
| DCI Ref (CYPMD) | `DciRefCypmdId` | 28557688031250 | The request reference number | Each ticket |
| Age (CYPMD) | `AgeCypmdId` | 28557798992018 | `Pupil.Age` | When above 0. An Add has no age. |
| Cycle year | `CycleYearId` | 19058058434322 | Year of the checking window start date | When the start date is set |
| Cycle month | `CycleMonthId` | 19058091622546 | Month of the checking window start date (1 to 12) | When the start date is set |
| Key stage | `KeyStageId` | 19058126549778 | See [Key stage](#key-stage) | When it maps |
| Correction type | `CorrectionTypeId` | 19056595594898 | See [Correction type](#correction-type) | Add, Merge, Include, Remove |
| Reason for removal | `ReasonForRemovalId` | 19381440546322 | See [Reason for removal](#reason-for-removal) | Add, Merge, Include, Remove |
| Correction reason (31) | `CorrectionReason31Id` | 19058912556690 | See [Correction reason (31)](#correction-reason-31) | Remove |
| Correction reason (21) | `CorrectionReason21Id` | 19058760113298 | `5_21` | Merge |
| LDS matched pupil ID | `LdsMatchedPupilIdId` | 17207993784978 | `Pupil.MatchRef` | When above 0 |
| LDS matched pupil ID 2 | `LdsMatchedPupilId2Id` | 19269521160978 | `MatchedPupil.MatchRef`, the second pupil | Merge, when above 0 |
| Pupil year group (CYPMD) | `PupilYearGroupCypmdId` | 19269588914066 | The year group that a year group change moves the pupil to | When it is an integer |
| Additional comments (CYPMD) | `AdditionalCommentsCypmdId` | 28923073020818 | Each `TextArea` answer, with a blank line between them | When there is text |
| DfE Establishment Number | `DfeEstablishmentNumberId` | 17207944800146 | `School.Laestab` as digits only (`933/4070` → `9334070`) | When present |
| Admission date | `AdmissionDateId` | 17208027233554 | The `admission-date` answer, else `Pupil.EntryDate`, as `yyyy-MM-dd` | When it parses |
| Decision Reason - Approved | `DecisionReasonApprovedId` | 19056477269010 | See [Decision reason](#decision-reason) | AutoApproved |
| Decision Reason - Rejected | `DecisionReasonRejectedId` | 19197936168210 | See [Decision reason](#decision-reason) | AutoRejected, when `PopulateDecisionReasonForAutoRejected` is true |

"Type of Organisation" (`TypeOfOrganisationId`, 360013574700) has an option map, but the worker
does not send it.

## Option maps

### Decision status

| Decision | Value |
| --- | --- |
| `Scrutiny` | `scrutiny` |
| `AutoApproved` | `auto_approved` |
| `AutoRejected` | `auto_rejected` |

The field also has `approved` and `rejected`. An agent sets these. The worker does not.

### Key stage

The value comes from the checking window type.

| Window type | Value |
| --- | --- |
| `KS2` | `ks2` |
| `KS4June`, `KS4Autumn` | `ks4` |
| `Post16` | `16-18` |

### Sex

| Pupil sex | Value |
| --- | --- |
| `M`, `male` | `m` |
| `F`, `female` | `f` |

Any other value (for example `U`) leaves the field out.

### Correction type

| Request | Value | LDS meaning |
| --- | --- | --- |
| `Add` | `10_` | New learner |
| `Include` | `11_` | Inclusion |
| `Merge` | `21_` | Merge learners |
| `Remove - {reason}` | `31_` | Remove from this cohort and never add back |

The Zendesk field also has `30_` (remove from this cohort and add back in a future cohort). The
worker does not send it. See [Open questions](#open-questions).

Results enquiries get no correction type, reason for removal or correction reason.

### Reason for removal

Add, Merge and Include requests get a fixed value. Reports count cases by this field, and the
supplier assigns cases to advisors by it.

| Request | Value |
| --- | --- |
| `Add` | `addition` |
| `Merge` | `merge_pupils` |
| `Include` | `inclusion` |

A Remove request resolves the value in this order:

1. **An add-back pupil** (P_INCL 403) gets `add_back_removal`, whatever the reason.
2. **`other`** gets `other_-_with_evidence` when the school uploaded a file. It gets
   `other_-_evidence_not_required` when the journey asked for a file and the school gave none.
   With no file question, it gets `other_-_with_evidence`.
3. **`not-on-roll`** on 16-19 uses the `not-on-roll-reason` answer (see the table below). KS4 has
   no sub-reason and gets `not_on_roll`. An unknown sub-reason also gets `not_on_roll`.
4. **Each other reason** uses this map:

| Removal reason (flow value) | Flows | Value |
| --- | --- | --- |
| `permanent-exclusion` | KS2, KS4 | `admitted_following_permanent_exclusion` |
| `english-not-first-language` | KS2, KS4 | `admitted_from_abroad_with_english_not_first_language` |
| `child-missing-education` | KS2, KS4 | `pupil_missing_in_education` |
| `elective-home-education` | KS2, KS4 | `elective_home_education` |
| `assessments-deferred` | KS2 | `one_or_more_end_of_key_stage_assessments_deferred_by_a_year` |
| `permanently-excluded` | KS2, KS4 | `permanently_excluded_from_current_school` |
| `permanently-left-england` | KS2, KS4 | `permanently_left_england` |
| `added-after-summer-term` | KS2 | `pupil_added_to_school_roll_after_start_of_summer_term` |
| `pupil-died` | KS2, KS4 | `deceased` |
| `social-care-involvement` | KS2, KS4 | `social_care_involvement_including_police_prison` |
| `life-limiting-illness` | KS2, KS4 | `terminal_critical_illness` |
| `completed-ks4-elsewhere` | KS4 | `add_back_removal` |
| `dual-registered-moved` | KS4 | `moved_school_dual_registration` |
| `year-group-change` | KS4 | `year_group_change` |
| `student-died` | 16-19 | `deceased` |
| `not-at-end-of-16-19-study` | 16-19 | `not_at_end_of_16_to_18_study` |

16-19 `not-on-roll` sub-reasons:

| `not-on-roll-reason` answer | Value |
| --- | --- |
| `apprentice` | `not_on_roll_apprentice` |
| `external-candidate` | `not_on_roll_external` |
| `international-student` | `not_on_roll_international_student` |
| `other`, with evidence | `not_on_roll_other_with_evidence` |
| `other`, no evidence | `not_on_roll_other_evidence_not_required` |

### Correction reason (31)

The value is the LDS CYPMD correction code with the suffix `_31`. Egress removes the suffix.

The worker first resolves "Reason for removal". These values decide the code:

| Reason for removal | Value |
| --- | --- |
| `add_back_removal` | `8_31` |
| `other_-_with_evidence`, `not_on_roll_other_with_evidence` | `329_31` |
| `other_-_evidence_not_required`, `not_on_roll_other_evidence_not_required` | `330_31` |
| `not_on_roll_international_student` | `326_31` |
| `not_on_roll_external` | `328_31` |
| `not_on_roll_apprentice` | `331_31` |

Each other removal reason uses this map:

| Removal reason (flow value) | Value | LDS meaning |
| --- | --- | --- |
| `english-not-first-language` | `2_31` | Admitted from abroad with English not first language |
| `permanently-left-england` | `3_31` | Permanently left England |
| `pupil-died`, `student-died` | `4_31` | Deceased |
| `not-on-roll` (KS4) | `6_31` | Not on roll |
| `permanent-exclusion` | `13_31` | Admitted following permanent exclusion |
| `year-group-change` | `17_31` | Year group change |
| `elective-home-education` | `27_31` | Elective home education |
| `assessments-deferred` | `41_31` | One or more end of key stage assessments deferred by a year |
| `added-after-summer-term` | `109_31` | Pupil added to school roll after start of summer term |
| `not-at-end-of-16-19-study` | `325_31` | Not at end of 16-18 study |
| `dual-registered-moved` | `332_31` | Moved school (dual registration) |
| `permanently-excluded` | `500_31` | Permanently excluded from current school |
| `child-missing-education` | `501_31` | Pupil missing in education |
| `social-care-involvement` | `502_31` | Social care involvement, including police or prison |
| `life-limiting-illness` | `503_31` | Terminal or critical illness |

The codes come from the LDS CYPMD data specification (the `Correction_Reason` rows). The 16-19
codes (325 to 332) are in LDS spec v2.4.

The Zendesk field also has `120_31`, `322_31` and `333_31`. The worker does not send them.

### Correction reason (21)

Each Merge ticket gets `5_21` (merge pupils). This is the only option on the field.

### Decision reason

The key is the rules engine outcome key. An AutoApproved ticket gets the "Approved" value. An
AutoRejected ticket gets the "Rejected" value when `PopulateDecisionReasonForAutoRejected` is
true. A Scrutiny ticket gets neither.

| Outcome key | Decision Reason - Approved | Decision Reason - Rejected |
| --- | --- | --- |
| `Inclusion` | `inclusion_criteria_met` | `inclusion_criteria_not_met` |
| `MergePupils` | `merge_pupils_criteria_met` | `merge_pupils_criteria_not_met` |
| `AdmittedFollowingPermanentExclusion` | `admitted_following_permanent_exclusion_criteria_met` | `admitted_following_permanent_exclusion_criteria_not_met` |
| `AdmittedFromAbroadEal` | `admitted_from_abroad_with_english_not_first_language_criteria_met` | `admitted_from_abroad_with_english_not_first_language_criteria_not_met` |
| `CompletedKs4Elsewhere` | `add_back_removal_criteria_met` | `add_back_removal_criteria_not_met` |
| `SocialCareInvolvement` | `social_care_involvement_including_police/prison_-_criteria_met` | `social_care_involvement_including_police_prison_criteria_not_met` |
| `TerminalCriticalIllness` | `terminal/critical_illness_-_criteria_met` | `terminal_critical_illness_criteria_not_met` |
| `YearGroupChange` | `year_group_changed_to_year_10` | `year_group_change_criteria_not_met` |
| `Deceased` | `deceased_criteria_met` | `deceased_criteria_not_met` |
| `ElectiveHomeEducation` | `elective_home_education_criteria_met` | `elective_home_education_criteria_not_met` |
| `MovedSchoolDualRegistration` | `moved_school_dual_registration_criteria_met` | `moved_school_dual_registration_criteria_not_met` |
| `NotOnRoll` | `not_on_roll_criteria_met` | `not_on_roll_criteria_not_met` |
| `PermanentlyExcludedFromCurrentSchool` | `permanently_excluded_from_current_school_criteria_met` | `permanently_excluded_from_current_school_criteria_not_met` |
| `PermanentlyLeftEngland` | `permanently_left_england_criteria_met` | `permanently_left_england_criteria_not_met` |
| `PupilMissingInEducation` | `pupil_missing_in_education_-_criteria_met` | `pupil_missing_in_education_criteria_not_met` |
| `AssessmentsDeferred` | `one_or_more_end_of_key_stage_assessments_deferred_by_a_year_criteria_met` | `one_or_more_end_of_key_stage_assessments_deferred_by_a_year_criteria_not_met` |
| `PupilAddedAfterSummerTerm` | `pupil_added_to_school_roll_after_start_of_summer_term_criteria_met` | `pupil_added_to_school_roll_after_start_of_summer_term_criteria_not_met` |
| `PupilNotOnJuneList` | `pupil_not_on_june_list_criteria_met` | `pupil_not_on_june_list_criteria_not_met` |
| `NotAtEndOf16To18Study` | `not_at_end_of_16_to_18_study_criteria_met` | `not_at_end_of_16_to_18_study_criteria_not_met` |

The values are not regular. They are copied from the Zendesk API exactly. Do not generate them.

`AddPupil` and `Other` have no value. Their rules always send the request to Scrutiny.

## Egress reads the ticket

The LDS data egress reads its values from the ticket, not from CYPMD's data. An agent can change
a value on a Scrutiny ticket, and egress sends the changed value. For the column map, see
[data-egress.md](./data-egress.md#2-where-the-data-comes-from).

## Open questions

- **Year group change with add-back.** The LDS spec "Remove (Addback)" tab gives correction type
  30 with reason 17. The worker always sends `31_` with `17_31`. The business must confirm which
  type a year group change takes.
- **Pupil not on June pupil list (120).** The rules engine has a `PupilNotOnJuneList` outcome, but
  no flow has this reason yet. When a flow adds it, add `120_31` to the correction reason map.

## Check the mappings against Zendesk

Read a field's options with `GET /api/v2/ticket_fields/{id}.json`. Each `custom_field_options`
`value` must match a value in `ZendeskTicketFieldOptions` exactly. A value that Zendesk does not
know makes Zendesk reject the ticket.

## Change a mapping

1. Change the constant and the map in `ZendeskTicketFieldOptions.cs`.
2. Add or change the case in `ZendeskTicketFieldOptionsTests` or
   `ZendeskConsumerTicketCompositionTests`.
3. Update this document in the same pull request.

For a new field, also add the name to `ZendeskTicketFieldConstants`, the id to
`ZendeskTicketFieldSettings` and each `appsettings.json`, and a mapper in `ZendeskConsumer`.
