# Extensión de Visual Studio para gestión de PR en Bitbucket

---
*El objetivo de la extensión es la gestión de PR y HU de Jira y Bitbucket desde el propio Visual Studio*

---

## Arquitectura:
    - CommentExtensionBitbucket.slnx
        - BitbucketAdapter.csproj -> Adaptador a la API de bitbucket
            - Domain/*
            - Application/*
            - Infrastructure/*
        - RequestAdapter.csproj -> Adaptador de cliente HTTP
            - Domain/*
            - Application/*
            - Infrastructure/*
        - JiraAdapter.csproj -> Adaptador a la API de JIRA
            - Domain/*
            - Application/*
            - Infrastructure/*
        - ClientExtensión.csproj -> Extensión de Visual Studio
---
## Integraciones

- `Jira`: Versión Cloud y Data Center por medio de `API REST`
- `Bitbucket`: Versión Cloud y Data Center por medio de `API REST`

----
## Información: 

**AUTORES:** `S.Sotelo` y `E.Benitez`

**VERSIÓN:** 0.1.0

---