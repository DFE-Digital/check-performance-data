# CYPMD — C4 Level 1: System Context

Check Your Pupil / Performance Data (**CYPMD**) allows school users to check the
data the DfE holds on them and request any changes.

A C4 **System Context** diagram is the highest level of the C4 model. It shows
CYPMD as a single system and the people and external systems it interacts with —
deliberately hiding all internal detail (see the
[Container diagram](./c4-container.md) for the level below).

Source: `HLD.json` (Lucidchart).

```mermaid
C4Context
    title System Context diagram for CYPMD

    Person(schoolUser, "School User", "A user at the school, registered with DfE SignIn")
    Person(adminUser, "Admin User", "A user at the DfE")
    Person(scrutinyUser, "Scrutiny User", "A user who scrutinises requests that are not auto-approved or rejected")

    System(cypmd, "CYPMD", "Allows school users to check the data the DfE holds on them and request any changes.")

    System_Ext(dfeSignIn, "DfE SignIn", "The DfE authorization service")
    System_Ext(lds, "LDS", "System of record for the learner and school data")
    System_Ext(zendesk, "Zendesk", "CRM used to manage queries and non-automatically resolved requests")
    System_Ext(govNotify, "Gov Notify", "Central service to send emails / SMS")

    Rel(schoolUser, cypmd, "Views school data and requests changes using")
    Rel(schoolUser, dfeSignIn, "Authorizes with")
    Rel(adminUser, cypmd, "Administers the application")
    Rel(adminUser, dfeSignIn, "Authorizes with")
    Rel(scrutinyUser, zendesk, "Accesses tickets in")

    Rel(dfeSignIn, cypmd, "Provides authorization to")
    Rel(lds, cypmd, "Provides school and learner data to")
    Rel(cypmd, lds, "Provides approved changes to")
    Rel(cypmd, zendesk, "Creates and updates tickets in")
    Rel(cypmd, govNotify, "Sends email via")
    Rel(govNotify, schoolUser, "Sends notifications to")

    UpdateLayoutConfig($c4ShapeInRow="3", $c4BoundaryInRow="1")
```

## People

| Actor | Description |
|---|---|
| **School User** | A user at the school, registered with DfE SignIn. Checks pupil/performance data and raises amendment requests. |
| **Admin User** | A user at the DfE who administers the service. |
| **Scrutiny User** | A DfE user who reviews requests that are not automatically approved or rejected. |

## External systems

| System | Description | Interaction |
|---|---|---|
| **DfE SignIn** | The DfE authorization (OIDC) service. | Authenticates School and Admin users and provides authorization to CYPMD. |
| **LDS** | System of record for learner and school data. | Supplies data into CYPMD and receives approved changes back. |
| **Zendesk** | CRM used to manage queries and requests needing manual handling. | CYPMD creates/updates tickets; Scrutiny users work those tickets. |
| **Gov Notify** | GOV.UK central notifications service. | CYPMD sends email/SMS notifications to users via Notify. |

## Notes

- This context view is a faithful transcription of `HLD.json` and matches the
  current service: DfE SignIn OIDC auth, a Zendesk integration, GOV.UK Notify
  for notifications, and an LDS data exchange.
- The LDS data exchange is shown here as a single logical relationship. The
  containers that implement it (ingress/egress workers and interface storage)
  are shown — and annotated as planned where not yet built — in the
  [Container diagram](./c4-container.md).
