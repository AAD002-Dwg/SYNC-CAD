# Manual de Usuario - SYNC-CAD

Bienvenido a **SYNC-CAD**, el ecosistema que permite a los equipos de arquitectura trabajar de manera colaborativa y en tiempo real usando AutoCAD y Google Drive.

Este manual explica detalladamente cómo instalar, configurar y utilizar el plugin dentro de tu flujo de trabajo diario.

---

## 1. Instalación

1. Descarga el archivo **`CadSyncInstaller.zip`** de la última versión y extráelo en tu computadora.
2. Ejecuta el archivo **`CadSyncInstaller.exe`** incluido.
3. El instalador detectará automáticamente tus carpetas de plugins. Selecciona **Opción [1] Instalar / Reparar**.
4. ¡Listo! El complemento se iniciará automáticamente la próxima vez que abras AutoCAD.

> **💡 Nota de compatibilidad:** SYNC-CAD es compatible con AutoCAD 2022, 2025 y 2027.

---

## 2. Primera Configuración

La primera vez que abras AutoCAD luego de instalar, debes vincular tu plugin con el servidor de tu estudio.

### Comando: `CADSYNC_SETUP`
Escribe `CADSYNC_SETUP` en la línea de comandos de AutoCAD. Te pedirá los siguientes datos:
1. **Punto final del Servidor (Server URL):** URL donde está alojado el servidor (Ej: `https://sync-cad.onrender.com`).
2. **Nombre de Usuario (User Name):** Tu nombre real para que tus colegas te identifiquen (Ej: *Juan Pérez*).
3. **Clave del Estudio (Studio Key):** El identificador único de tu estudio y proyecto provisto por el administrador (Ej: `ESTUDIO_DEMO_01`).

### Comando: `CADSYNC_STATUS`
Puedes utilizar `CADSYNC_STATUS` en cualquier momento para verificar si el plugin está debidamente conectado al servidor y validado con las credenciales correctas. Verás la respuesta directo en la línea de comandos de AutoCAD.

---

## 3. Uso y Funcionalidad Básica

Para abrir el panel principal del complemento, escribe el comando maestro en la consola:
### Comando: `CADSYNC`

Se abrirá una paleta lateral dentro de AutoCAD. Esta paleta es tu centro de control para la sincronización.

### Sincronización Automática
SYNC-CAD sincroniza **"Capas"** individuales en lugar de generar versiones conflictivas del todo el archivo `.dwg`. 

- **Auto-Subida (Auto-Push):** Cada vez que terminas una acción importante modificando un elemento o geometría de una capa, el plugin lo detectará. Subirá silenciosamente esos cambios en un mini-archivo (delta) directo a Google Drive, para que estén disponibles para los demás casi instantáneamente.
- **Auto-Descarga (Auto-Pull):** Cuando uno de tus compañeros sube cambios de una de sus capas, tu plugin será notificado en tiempo real gracias a un WebSocket. Descargará mágicamente en tu plano activo los cambios recibidos y los integrará en tu modelo sin requerir tu intervención.

*(Esta configuración se puede activar o apagar desde los switches en el panel de CADSYNC)*.

---

## 4. Colaboración en Tiempo Real

Para evitar conflictos y superposiciones, SYNC-CAD incluye herramientas avanzadas de colaboración en vivo.

### A) Reserva de Capas (Layer Locking)
En un escenario colaborativo no deseamos que dos personas modifiquen detalles de la misma capa a la vez, o Google Drive perdería la versión de alguno.
- Si ves en el panel una capa con un candado 🔒 y el nombre de un colega *(Ej: "Estructura (Bloqueada por Juan)")*, **NO LA MODIFIQUES**. El plugin probablemente revierta tus cambios locales para alinearse con la versión de la nube de Juan.
- Para adueñarte de una capa, podés seleccionarla en el panel.

### B) Cursores Fantasma (Ghost Cursors)
Para que sientas que no estás trabajando solo:
- Activa el switch **"Mostrar Cursores Live"** en el panel principal.
- Verás pequeñas crucetas de colores con el nombre de tus colegas sobrepoladas en tu área de dibujo mientras ellos mueven su propio mouse.
- *Nota: Los cursores fantasma no guardan ningún tipo de residuo, línea falsa ni ensucian tu archivo. Es 100% visual.*

---

## 5. Panel de Solución de Problemas

- **El plugin no aparece o no funciona `CADSYNC`:** Asegúrate de que la variable `SECURELOAD` en AutoCAD está en `0` o `1` (si está en `1`, te preguntará si confías en el programa, diles "Cargar siempre").
- **No se suben mis cambios a Google Drive:** Fíjate si en la consola de AutoCAD el plugin disparó el mensaje: `Socket NO conectado — cursor no enviado`. Puede que la URL de tu servidor esté mal escrita en `CADSYNC_SETUP`.
- **Veo un error que menciona "403" / "Quota Limit":** Esto ocurre cuando hubo un error de configuración externa con la pasarela de Google OAuth. Notifica al administrador de tu sistema para que actualice los Access Tokens del lado del servidor (Render).
