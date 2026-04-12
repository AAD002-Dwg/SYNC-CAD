# Guía de Configuración: Google Drive Storage para SYNC-CAD

Esta guía explica cómo conectar el servidor de SYNC-CAD con Google Drive utilizando una **Service Account**.

## 1. Crear Proyecto en Google Cloud
1. Ve a [Google Cloud Console](https://console.cloud.google.com/).
2. Crea un nuevo proyecto llamado `SYNC-CAD-Storage`.
3. En el buscador superior, escribe "Google Drive API" y haz clic en **Habilitar (Enable)**.

## 2. Crear Service Account
1. Ve a **APIs y Servicios > Credenciales**.
2. Haz clic en **+ Crear Credenciales > Cuenta de servicio**.
3. Ponle nombre (ej: `sync-cad-bot`) y haz clic en **Crear y Continuar**.
4. (Opcional) Salta el paso de roles y haz clic en **Listo**.
5. En la lista de "Cuentas de servicio", haz clic en el email de la cuenta creada.
6. Ve a la pestaña **Claves (Keys) > Agregar clave > Crear clave nueva**.
7. Selecciona formato **JSON** y se descargará un archivo.

## 3. Configurar el Servidor
1. Renombra el archivo descargado a `credentials.json`.
2. Muévelo a la carpeta `server/` de este proyecto.
3. Abre el archivo y copia el valor de `"client_email"` (ej: `sync-cad-bot@proyecto.iam.gserviceaccount.com`).

## 4. Vincular Carpeta de Drive
1. Ve a tu Google Drive (el del estudio).
2. Crea una carpeta nueva (ej: `Proyectos SYNC-CAD`).
3. Haz clic derecho en la carpeta > **Compartir**.
4. Pega el email de la Service Account que copiaste en el paso anterior y dale permiso de **Editor**.
5. Copia el **ID de la carpeta** desde la URL del navegador. 
   - Ejemplo: `drive.google.com/drive/folders/1abc123...` -> El ID es `1abc123...`.

## 5. Editar Archivo .env
1. Crea un archivo `.env` en la carpeta `server/` (puedes copiar el `.env.example`).
2. Pega el ID de tu carpeta:
   ```env
   GOOGLE_DRIVE_FOLDER_ID=1abc123...
   ```

## 6. Reiniciar Servidor
Ejecuta `npm start` en la carpeta server. ¡Listo!
