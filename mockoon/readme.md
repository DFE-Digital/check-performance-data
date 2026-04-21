# Mockoon

## Requirements;
    - Mockoon (can be installed from https://mockoon.com)

## Setup;

Import (File -> Open Local Environmnet) the dsi.json into Mockoon and start.

Update the appSettings.json to point to Mockoon;

```
  "DfeSignIn": {
    "BaseUrl": "http://localhost:8000/",
    "ApiClientSecret": "mockoon-api-secret-which-needs-to-be-long",
    "ClientId": "CheckPerformanceData",
    "Audience": "signin.education.gov.uk",
    "MetadataAddress": "http://localhost:8000/.well-known/openid-configuration",
    "ClientSecret": "eye-am-mockoon-authentication-which-needs-to-be-long",
    "RequireHttpsMetadata": false
  }
```

The import part is the 8000 should be the port the Mockoon api is running on.

## Token

If you need to change the token this is found in Mockoon Routes, "/connect/token"

You can use a service like https://www.jwt.io to decode and encode the a token.8000

## Changing Organisations

In Mockoon select the "Data" tab, this should list the available Organisations and age ranges.
Follow the same format to add new Organisations.

To login as a specific Organisations, on the route for "users/:userId/organisations" change the data to the required Organisation. 

Don't forget to refresh Mockon for the changes to take effect.

Any changes can also be added to git and pushed in the normal manner.


