# Egress reads its values from the Zendesk ticket

Date: 2026-11-04

Status: Accepted

## Context

The LDS data egress writes one row for each approved request. It read only the decision from
Zendesk. It worked out every other value from CYPMD's own data: the request row, the pupil record
and the journey answers.

The worker also writes most of these values to the Zendesk ticket. The two calculations were in
different code, so they could disagree. For example, the ticket sent `8_31` for an add-back
pupil, but egress sent the code of the journey reason. An agent can also change a value on a
scrutiny ticket. Egress did not see that change.

## Decision

The Zendesk ticket is the truth. Egress reads each LDS column that has a Zendesk field from the
ticket. It reads CYPMD's data only for the columns that have no Zendesk field: ULN, Year_Group on
New learners, Removal_Year_0..2 and Post_Code.

A missing required ticket value fails the record. Egress never works the value out instead. A
field whose id is not configured stops the pull.

The worker writes the values that LDS needs. It sets the cycle year and month from the checking
window's start date, and the 16-19 not-on-roll correction codes from the LDS specification.

## Consequences

* Each value has one calculation, in the worker. A change to a ticket value changes the egress
  file with no change to egress.
* Egress needs every egress field id to be configured. Production sets the ids to 0 until they
  are known, so egress cannot pull in production until they are set.
* The dev egress seeder must write every egress field to its outbox tickets.
* The Zendesk "Correction reason (31)" field must have an option for each code that the worker
  sends. The 16-19 options `326_31`, `328_31` and `331_31` exist (checked 2026-11-04).
