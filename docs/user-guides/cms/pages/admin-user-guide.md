---
title: The full admin user guide
subtitle: Everything in the admin area that is not the CMS
parent: for-administrators
order: 6
keywords: admin user guide pdf dashboard windows egress rules engine queues
---

The CMS is one group of tools on the Administration page. The page lists nine entries in all. The others are Dashboard, System administration, Messages, Manage windows, Data egress, Audit log, View as an establishment and Danger zone.

The [admin user guide (PDF)](https://github.com/DFE-Digital/check-performance-data/blob/main/docs/user-guides/admin-user-guide.pdf) explains every group and how to use each screen. It is kept in the service's code repository and updated with the service.

## What the other groups do

- **Dashboard**: school engagement and amendment request figures for an open checking window.
- **System administration**: the rules engine (pipeline dashboard, queues, decision rules), system settings, application logs and role settings.
- **Messages**: search feedback from users and the dead-letter queue.
- **Manage windows**: create and manage checking windows and their checking exercises, load and validate data files, close exercises and review requests. It is a single link, not a group.
- **Data egress**: pull approved decisions from Zendesk, prepare them and transfer them to LDS.
- **Audit log**: a record of administrative activity with CSV export.
- **View as an establishment**: see the service as one school or college sees it, in read-only mode. Only the `cypmd_admin` role can use it. It is a single link, not a group, and it is not a section you can give to a role.
- **Danger zone**: reset seed data (not in production) and the blob storage browser.

## Where the two guides meet

| Topic | This guide | Admin user guide |
|---|---|---|
| Writing and publishing pages | In full | In outline |
| Content blocks, content staging | In full | In outline |
| Search analytics and search feedback | [In full](search-analytics.md) | In full |
| System settings, role settings, logs | [The parts that affect the CMS](settings-and-site-assets.md) | In full |
| Checking windows, data egress, the rules engine, viewing as an establishment | Not covered | In full |
