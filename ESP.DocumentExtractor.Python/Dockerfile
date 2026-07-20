# Custom Azure Functions Python image bundling LibreDWG so .dwg files can be
# converted in the cloud (the stock Functions image has no DWG tooling).
FROM mcr.microsoft.com/azure-functions/python:4-python3.11

ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=python

WORKDIR /home/site/wwwroot

RUN apt-get update \
    && apt-get install -y --no-install-recommends libredwg0 libredwg-tools \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt /tmp/requirements.txt
RUN python -m pip install --no-cache-dir -r /tmp/requirements.txt \
    && rm /tmp/requirements.txt

COPY . /home/site/wwwroot
