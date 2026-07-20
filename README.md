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

- `.github/workflows/build-push-python-function-container.yml` builds and pushes the
  Python Azure Function container image to
  `espdocumentextractoracr.azurecr.io/dwg-geojson-function`.
- `.github/workflows/deploy-function-app.yml` is a **manual-only legacy package
  deploy** workflow and should not be used for `.dwg` production deployments.

Repository secrets required for the container workflow:

- `ACR_USERNAME`
- `ACR_PASSWORD`

Published image tags:

- `espdocumentextractoracr.azurecr.io/dwg-geojson-function:latest`
- `espdocumentextractoracr.azurecr.io/dwg-geojson-function:<sha>`

Configure the Azure Function App as a Linux custom container and set:

- `AzureWebJobsStorage`
- `FUNCTIONS_WORKER_RUNTIME=python`
- `COSMOS_CONNECTION_STRING`
- `COSMOS_DATABASE_NAME=esp-document-extractor`
- `COSMOS_CONTAINER_NAME=cad-geojson`
