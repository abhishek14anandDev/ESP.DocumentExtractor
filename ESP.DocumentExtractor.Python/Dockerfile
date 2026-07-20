# Azure Functions Python v2 custom container with LibreDWG for DWG/DXF support.
FROM mcr.microsoft.com/azure-functions/python:4-python3.11 AS libredwg-builder

ARG LIBREDWG_VERSION=0.14
ARG LIBREDWG_SHA256=62ebb73b984f865960f20ed26619ea5f8789d5e3fd088fa40a2598384da81275

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        build-essential \
        ca-certificates \
        curl \
        libpcre2-dev \
        libxml2-dev \
        pkg-config \
        xz-utils \
    && curl --fail --location --show-error \
        --output /tmp/libredwg.tar.xz \
        "https://github.com/LibreDWG/libredwg/releases/download/${LIBREDWG_VERSION}/libredwg-${LIBREDWG_VERSION}.tar.xz" \
    && echo "${LIBREDWG_SHA256}  /tmp/libredwg.tar.xz" | sha256sum --check --strict \
    && mkdir /tmp/libredwg \
    && tar --extract --xz --strip-components=1 \
        --file /tmp/libredwg.tar.xz --directory /tmp/libredwg \
    && cd /tmp/libredwg \
    && ./configure --disable-bindings --enable-release --prefix=/usr/local \
    && make -j"$(nproc)" \
    && make install DESTDIR=/opt/libredwg \
    && test -x /opt/libredwg/usr/local/bin/dwgread \
    && test -x /opt/libredwg/usr/local/bin/dwg2dxf

FROM mcr.microsoft.com/azure-functions/python:4-python3.11

ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=python \
    FUNCTIONS_EXTENSION_VERSION=~4 \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

WORKDIR /home/site/wwwroot

RUN apt-get update \
    && apt-get install -y --no-install-recommends libpcre2-16-0 libxml2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=libredwg-builder /opt/libredwg/usr/local/ /usr/local/

RUN ldconfig \
    && command -v dwgread \
    && command -v dwg2dxf

COPY requirements.txt ./
RUN python -m pip install --no-cache-dir -r requirements.txt

COPY . .
