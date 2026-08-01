# PROBLEMA: Al dar a conectar con OAuth2.0 no se abre modal

**Descripción:** La extensión desarrollada tiene 2 formas de integrarse con `Bitbucket`, por `OAuth2.0` y por `Basic`, la integración prioritaria es `OAuth2.0`,
ninguna de las 2 abren modal ni navegador al darle a sus botones correspondientes en la extensión cuando usamos `Visual Studio` en 'Expedimental'.
Debemos corregir de forma prioritaria el `OAuth2.0` primero.


## Tareas
- [ ] Analizar la implementación existente para ver donde falla la integración
- [ ] Terminar de ajustar la implementación de `OAuth2.0` para poder guardar el token

## Referencias
**Proyecto:**

     `Ubicación del proyecto`: "C:\Users\ezet4\RiderProjects\CommentExtensionBitbucket"
     `Documentación de bitbucket`: "https://developer.atlassian.com/cloud/bitbucket/rest/intro/#authentication"
     `Documentación de Microsoft`: "https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/get-started/debug-extensions?view=visualstudio"

