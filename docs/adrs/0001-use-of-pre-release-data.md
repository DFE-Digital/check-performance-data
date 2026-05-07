# Use of data in the checking process

Date: 2026-05-06

Status: Under review

## Context

The checking process makes use of data that has not yet been released to the public as an input. Access to this information needs to be controlled until the checking process has been completed and the results released. After this point the information effectively becomes public.

Testing environment deployments do have suitable security controls that are likely to be adequete for storing and using pre-release data.

## Decision

When testing the system with pre-release data this must be done in Production environments. User permissions should be consistent across the deployments and inline with the production environment.

All testing in the non-production environments (Testing) must make use of synthetic data instead.

## Consequences

* The process by which suitable synthetic data is created must be prioritised to ensure adequete testing
* There is an emphasis on manual restraints on data loading and user creation. We may need to revisit this consequence and see if there is a better way to manage user permissions.
* Prior to a key stage checking exercise the team may need to spin up a dedicated testing deployment with a suitable dataset in the Production environment.