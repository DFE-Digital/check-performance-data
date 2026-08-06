# Data Egress Spike

## Aims

To investigate the Zendesk API and find out how to get the ticket information out to generate Egress csv files to pass back to LDS

## Background

### Creating a ticket

When a user makes a change request in the Portal, a record is created in the Portal Database in a table called ChangeRequests. This holds information related to the request such as (but not limited to):

- Who made it
- The time it was made
- What type of request (Include, Remove etc)
- The request reference 
- The Checking Window
- Request Status (Ready to submit, Submitted-Uncommitted, Submitted-Committed etc)
- The ID of the ticket created in Zendesk (initially blank)

Example ChangeRequest record after submission (illustrative, not the real fields):

| Reference      | Window  | Type    | Status                | TicketId |
|----------------|---------|---------|-----------------------|----------|
| KS4JUNE_ABC123 | KS4June | Remove  | Submitted-Uncommitted | _null_   |
| KS4JUNE_ABC124 | KS4June | Include | Submitted-Uncommitted | _null_   |

When the Checking Window is closed, all of the records for that Window that have a status of Submitted-Uncommitted are sent to Zendesk and the ChangeRequests table is updated with the Zendesk Ticket Ids and the status of the tickets are updated to Submitted-Committed.

Example ChangeRequest record after the Window closed (illustrative, not the real fields):

| Reference      | Window  | Type    | Status                | TicketId |
|----------------|---------|---------|-----------------------|----------|
| KS4JUNE_ABC123 | KS4June | Remove  | Submitted-Committed   | 88856    |
| KS4JUNE_ABC124 | KS4June | Include | Submitted-Committed   | 89000    |

At this point we have a complete list in the database of all of the Amendment Requests that have been sent through to Zendesk.

**The Portal Database is the single source of truth for Amendment Requests.**

## Egress

Once tickets have been scrutinised in Zendesk and approved / rejected as appropriate, there is an Egress procedure that needs to happen to take the approved requests and send them to LDS.

### Tactical (current) service
In the Tactical Service, egress involves running reports from Zendesk and then a LOT of post-processing of the Excel files before manually dropping a csv in the LDS storage account.

### Proposed Strategic Service
For the Strategic Egress solution, the aim is to make the process as simple and automated as possible. Firstly, the API does not have an endpoint to run Zendesk reports, so these will no longer be used. Although even if we _could_ run the reports from the API, we wouldn't as we'd be back to all the post-processing.  

These are the steps we will go through:   _Please note that this process as listed may be split up into separate stages as per the design in ADO.  This is purely a technical proof of concept_

- Call the Zendesk API with all of the TicketIds that we have stored in the database for the required Window and the required Amendment Type. (for example, KS4June - Remove).  This gives us back the Zendesk Ticket information for requested tickets.  This is a snipped / truncated example of the data that the API gives us per ticket.

```
  "tickets": [
    {
      "id": 88856,
      "custom_fields": [
        {
          "id": 17207944800146,
          "value": "8734603"
        },
        {
          "id": 17207993784978,
          "value": "10000011"
        },
        {
          "id": 17208002901906,
          "value": "2007-06-01"
        },
        {
          "id": 19056253670034,
          "value": "auto_approved"
        },
        {
          "id": 19056595594898,
          "value": "31_"
        },
        {
          "id": 19058058434322,
          "value": "2026"
        },
        {
          "id": 19058091622546,
          "value": "6"
        },
        {
          "id": 19058126549778,
          "value": "ks4"
        },
        {
          "id": 19058409672594,
          "value": "Bellingham"
        },
        {
          "id": 19058507283218,
          "value": "Jude"
        },
```
- Filter out any of the returned tickets that are not either "approved" or "auto_approved" (the example above is "auto_approved")
- Generate a csv in the appropriate LDS format with the filtered ticket data, converting the data as appropriate (in this example `8734603` into `873` for the LA, `4603` into the Establishment number, `31_` into `31` for the Correction_Type etc.)
- Upload the csv into the Storage account for LDS to be able to pick up.  For example, for Removals:

| Correction_ID | Correction_Type | Correction_Reason | Key_Stage | Establishment_Number | Surname    | Forename | Sex | Date_of_Birth | Cycle_Year | Cycle_Month | Local_Authority | Learner_ID |
| ------------- | --------------- | ----------------- | --------- | -------------------- | ---------- | -------- | --- | ------------- | ---------- | ----------- | --------------- | ---------- |
| 88856         | 31              | 4                 | KS4       | 4603                 | Bellingham | Jude     | M   | 2007-06-01    | 2026       | 6           | 873             | 10000011   |

- We will also at this point update the ChangeRequests table with the status of the Amendment Request from Zendesk (ie, from scrutiny to approve / reject as appropriate.)

## Summary

We can completely eliminate the manual processing and need for stored reports in Zendesk by using the API in this way.  The only thing that should be noted is that:

 **The Strategic Service's database is the _single_ source of truth for Amendment Requests.  It is from this database that the egress files are generated (using the Zendesk API).  Therefore, any Amendment Requests added to Zendesk outside of the Portal process _will not be included in egress_.**