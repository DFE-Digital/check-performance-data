# Check Performance Data

## Getting started / Setup

Prerequisites

- [Git](https://git-scm.com/downloads) (for getting a copy of the source code and contributing changes)
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) (for building and running the C#/.NET web
  application)
- [Node.js](https://nodejs.org/en/download/) (for building web artefacts: (S)CSS, JS, etc.)
- IDE/Editor of choice (e.g., Visual Studio, Visual Studio Code, JetBrains Rider, etc.)
- [Docker Desktop](https://docs.docker.com/desktop/) (for development time hosting of dependencies)
- Local environment configured to authenticate to the GitHub NuGet feed
    - Create a classic [personal access token (PAT)](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens) with `read:packages` scope from your GitHub account (fine-grained tokens [do not support package scopes](https://github.com/github/roadmap/issues/558)).
    - Add the NuGet source to your local environment by filling in the placeholders and running:
    ```sh
    dotnet nuget add source --username <YOUR_GITHUB_USERNAME> --password <YOUR_PERSONAL_ACCESS_TOKEN> --store-password-in-clear-text --name dfedigital "https://nuget.pkg.github.com/DFE-Digital/index.json"
    ```

Clone the repository
```sh
git clone https://github.com/DFE-Digital/check-performance-data
```

Build the C#/.NET solution
```sh
dotnet build
```

Confirm tests are passing locally
```sh
dotnet test
```