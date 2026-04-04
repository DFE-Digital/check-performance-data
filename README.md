# Check Performance Data

## Getting started / Setup

Prerequisites

- [Git](https://git-scm.com/downloads) (for getting a copy of the source code and contributing changes)
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) (for building and running the C#/.NET web
  application)
- ~~[Node.js](https://nodejs.org/en/download/) (for building web artefacts: (S)CSS, JS, etc.)~~
- IDE/Editor of choice (e.g., Visual Studio, Visual Studio Code, JetBrains Rider, etc.)
- [Docker Desktop](https://docs.docker.com/desktop/) (for development time hosting of dependencies)
   
 

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

```sh
docker compose up --build -d
``` 