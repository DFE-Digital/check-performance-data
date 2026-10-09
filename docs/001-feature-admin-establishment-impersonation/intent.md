# Feature: Admin establishment impersonation

## Problem

Authorised administrators need to view an establishment's data and experience without submitting changes on its behalf, to investigate support issues by seeing the data and pages that establishment users see (D-2, D-10).

The login supplies the roles, establishment and URN. The application does not separately manage individual user permissions, so viewing as an establishment is sufficient. The feature must safely override the establishment identifiers for authorised impersonation without opening up the system to ordinary users changing to another establishment (D-10, D-11).

## Who it is for

Administrators who hold both the admin role and the impersonation role (D-3). An admin role alone does not grant access to this feature.

## What success looks like

- An authorised administrator can select the impersonation option in the existing admin section (D-4).
- They initially enter the LAESTAB and URN of the establishment they want to mimic (D-5).
- They can view that establishment's data with read-only access; anything that submits changes is blocked (D-6).
- Only an administrator with both required roles can override the establishment identifiers. Ordinary users continue to access only the establishment supplied by their login and cannot change to another establishment by altering inputs or requests (D-11).
- A prominent red banner at the top clearly identifies impersonation mode and shows the LAESTAB and URN of the establishment being viewed (D-7).
- They can clearly leave impersonation and return to their own admin context; this is a proposed supporting behaviour pending confirmation (D-8).

## Out of scope

- Making, saving or submitting changes on behalf of an establishment.
- Impersonating a specific person's account; this increment concerns viewing as an establishment selected by its LAESTAB and URN.
- Granting impersonation access to users without both required roles.
- Replacing the service's authentication system or promoting development impersonation helpers into production access controls.
- Role-management changes beyond what is needed to recognise the required impersonation permission; a wider role-management feature would be a separate increment.

The spec stage will settle identifier validation, protection of the establishment override, which journeys are available in read-only mode, how to distinguish viewing actions from changes, leaving and expiry behaviour, and how impersonation activity remains attributable to the administrator. These details are not yet agreed.

## Biggest assumption resolved

The user confirmed that an establishment's LAESTAB and URN are sufficient to reproduce the relevant experience: the login supplies the roles and establishment identifiers, and the application does not separately manage individual user permissions (D-10, superseding D-9). The override must retain the authorised administrator's identity and restrict establishment switching to this feature (D-11).
