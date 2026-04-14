1️⃣ Install NSwag CLI

Bash

 


dotnet tool install --global NSwag.ConsoleCore

2️⃣ Generate C# Models and Client

Bash

 

Copy code

nswag openapi2csclient /input:swagger.json /output:ApiClient.cs /namespace:MyApi.Client

/input → Path or URL to your Swagger/OpenAPI file.

/output → Output .cs file containing models and client classes.

/namespace → Namespace for generated code.

Example:

 

Bash

 

for demonstration, we can use the Zendesk API definition from GitHub:
nswag openapi2csclient /input:https://raw.githubusercontent.com/troystaylor/SharingIsCaring/refs/heads/main/Zendesk/apiDefinition.swagger.json /output:ZendeskClient.cs /namespace:DfE.CheckPerformanceData.ZendeskClient

use the preprod endpoint to generate the client when authenticated:
eg https://esfa-preprod.zendesk.com/api/v2/swagger.json
nswag openapi2csclient /input:https://esfa-preprod.zendesk.com/api/v2/swagger.json /output:ZendeskClient.cs /namespace:DfE.CheckPerformanceData.ZendeskClient
#nswag openapi2csclient /input:https://petstore.swagger.io/v2/swagger.json /output:PetStoreClient.cs /namespace:PetStore

This will create:

 

Models (POCO classes) for request/response objects.

API client methods for calling endpoints.

To structure the genetated code use the config.nswag file

eg

nswag run config.nswag