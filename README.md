# ESP.DocumentExtractor

Azure Functions-based document ingestion and invoice extraction service.

## Build

```bash
dotnet restore ESP.DocumentExtractor.sln
dotnet build ESP.DocumentExtractor.sln --configuration Release
```

## Test

```bash
dotnet test ESP.DocumentExtractor.sln --configuration Release
```

## Deployment

GitHub Actions workflow:

- [.github/workflows/deploy-function-app.yml](/Users/abhishekanand/RiderProjects/ESP.DocumentExtractor/.github/workflows/deploy-function-app.yml)

Repository secret required:

- `AZURE_FUNCTIONAPP_PUBLISH_PROFILE_PYDATAEXTRACTOR`

The workflow deploys the Python Function App `pydataextractor` on pushes to `main`
when `ESP.DocumentExtractor.Python` changes, and on manual dispatch.
