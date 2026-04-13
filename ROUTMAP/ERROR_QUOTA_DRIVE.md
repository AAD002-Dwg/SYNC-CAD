# Solución: Error de Cuota en Google Drive (Service Account)

> **Fecha del incidente:** 12 de abril de 2026  
> **Entorno:** Render (Cloud) — Node.js 22.22.0  
> **Archivo afectado:** `ARQUITECTURA.dwg`

---

## 1. El Problema

El log de Render muestra el siguiente error repetido (4 reintentos fallidos):

```
Error Sync Drive: GaxiosError: Service Accounts do not have storage quota.
Leverage shared drives, or use OAuth delegation instead.
```

- **Código HTTP:** `403 Forbidden`
- **Endpoint afectado:** `POST https://www.googleapis.com/upload/drive/v3/files` (creación de archivos nuevos)
- **Operación en el código:** `drive.files.create()` en `googleDriveService.js`, línea 78

### ¿Por qué sucede?

Aunque compartas una carpeta de tu Google Drive personal con el email de la Cuenta de Servicio, existe una regla crítica en la API de Google:

1. **Propiedad Diferida:** Por defecto, cualquier archivo que la Cuenta de Servicio sube (upload) a una carpeta compartida le "pertenece" legalmente a la Cuenta de Servicio, no al dueño de la carpeta.
2. **Cuota Cero:** Las Cuentas de Servicio tienen exactamente **0 bytes** de espacio de almacenamiento propio.
3. **El Rechazo:** Al intentar subir un archivo (como `ARQUITECTURA.dwg`), Google detecta que el "dueño" (la Service Account) no tiene espacio y cancela la operación con un error 403 (Forbidden).

### Detalle importante: `create` vs `update`

Los logs muestran que el error ocurre en `[DRIVE] Creando nuevo archivo`, es decir, cuando el archivo **no existe aún** en Drive. 

- **`files.create`** → ❌ Falla siempre (la Service Account sería la dueña del archivo nuevo).
- **`files.update`** → ✅ Podría funcionar si el archivo ya existe y fue subido originalmente por un usuario real (el dueño sigue siendo el usuario, no la SA).

**Workaround temporal:** Si suben manualmente los archivos `.dwg` a la carpeta de Drive la primera vez, las actualizaciones posteriores desde el plugin podrían funcionar. Esto NO es una solución permanente.

---

## 2. Tres Caminos para Solucionarlo

Dependiendo de si tu cuenta de Google es Personal (`@gmail.com`) o de Empresa (`Google Workspace`):

### Opción A: Usar "Unidades Compartidas" (Solo para Google Workspace)

Esta es la solución más profesional. Las Unidades Compartidas (Shared Drives) tienen una cuota colectiva de la organización.

1. Crea una **Unidad Compartida** (no una carpeta normal en "Mi Unidad").
2. Agrega el email de la Service Account como **Administrador** de esa unidad.
3. Usa el `FolderID` de una carpeta dentro de esa Unidad Compartida en `studios.json`.
4. En `googleDriveService.js`, agrega `supportsAllDrives: true` a todas las llamadas de la API.

**Cambio en código requerido:**
```javascript
// En files.create:
return drive.files.create({
    resource: fileMetadata,
    media: media,
    fields: 'id, name',
    supportsAllDrives: true,   // ← agregar
});

// En files.update:
return drive.files.update({
    fileId: fileId,
    media: media,
    fields: 'id, name',
    supportsAllDrives: true,   // ← agregar
});

// En files.list:
const res = await drive.files.list({
    q: query,
    fields: '...',
    supportsAllDrives: true,           // ← agregar
    includeItemsFromAllDrives: true,   // ← agregar
});
```

**Resultado:** El archivo se sube usando la cuota de la empresa, no de la SA.

### Opción B: Cambiar a OAuth2 (Recomendado para @gmail.com)

Si no tienes una cuenta de empresa, la Cuenta de Servicio no es la herramienta ideal para subir archivos.

1. Ir a la [Google Cloud Console](https://console.cloud.google.com/apis/credentials).
2. Crear un **OAuth 2.0 Client ID** (tipo "Web Application").
3. Generar un **Refresh Token** usando el flujo de consentimiento (una sola vez).
4. Configurar en Render las variables de entorno: `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `GOOGLE_REFRESH_TOKEN`.
5. Modificar `googleDriveService.js` para usar `OAuth2Client` en vez de `GoogleAuth`.

**Resultado:** Los archivos que suba el servidor aparecerán en tu Drive como si los hubieras subido tú mismo, usando tu cuota de 15GB.

### Opción C: Delegación de Dominio (Solo para Administradores de Google Workspace)

Si eres el administrador de tu organización:

1. Activa "Domain-wide Delegation" para la Service Account en la Consola de Google Cloud.
2. Autoriza los scopes de Drive en la consola de Admin de Google Workspace.
3. En el código (`googleDriveService.js`), configura el cliente para que "suplante" tu email:

```javascript
auth = new google.auth.JWT({
    email: keys.client_email,
    key: keys.private_key,
    scopes: SCOPES,
    subject: 'tu-email@empresa.com'   // ← suplantación
});
```

**Resultado:** La Service Account actúa como un usuario real y usa su cuota.

---

## 3. Resumen de Opciones

| Opción | Requiere | Cambios en código | Ideal para |
|--------|----------|-------------------|------------|
| **A. Shared Drive** | Google Workspace | Agregar `supportsAllDrives: true` | Empresas/Estudios con Google Workspace |
| **B. OAuth2** | Generar refresh token | Reescribir `getDriveService()` | Cuentas personales @gmail.com |
| **C. Delegación** | Admin de Google Workspace | Agregar `subject` al JWT | Empresas que ya usan la SA |

---

## 4. Siguientes Pasos

1. **Decidir qué tipo de cuenta se está usando** (personal vs empresa).
2. **Workaround inmediato:** Subir manualmente `ARQUITECTURA.dwg` a la carpeta de Drive para que futuras sincronizaciones usen `files.update` en vez de `files.create`.
3. **Solución permanente:** Implementar la opción A, B o C según corresponda.
