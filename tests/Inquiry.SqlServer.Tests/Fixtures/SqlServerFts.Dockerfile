FROM mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04@sha256:c1aa8afe9b06eab64c9774a4802dcd032205d1be785b1fd51e1c0151e7586b74

USER root

ARG MSSQL_PACKAGE_VERSION=16.0.4135.4-3

RUN set -eux; \
    test "$(dpkg --print-architecture)" = "amd64"; \
    install -m 0644 /etc/apt/trusted.gpg.d/microsoft-prod.gpg /usr/share/keyrings/microsoft-prod.gpg; \
    printf '%s\n' 'deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/ubuntu/22.04/mssql-server-2022 jammy main' > /etc/apt/sources.list.d/mssql-server-2022.list; \
    apt-get update; \
    ACCEPT_EULA=Y DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        "mssql-server=${MSSQL_PACKAGE_VERSION}" \
        "mssql-server-fts=${MSSQL_PACKAGE_VERSION}"; \
    test "$(dpkg-query -W -f='${Version}' mssql-server)" = "${MSSQL_PACKAGE_VERSION}"; \
    test "$(dpkg-query -W -f='${Version}' mssql-server-fts)" = "${MSSQL_PACKAGE_VERSION}"; \
    rm -rf /var/lib/apt/lists/*

USER mssql
