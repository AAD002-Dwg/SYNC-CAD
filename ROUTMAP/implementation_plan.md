# Multi-Tenant Architecture for SYNC-CAD

Currently, SYNC-CAD is hard-coded to a single Google Drive folder via environment variables. To support multiple independent architecture studios, we need to implement a "Multi-Tenant" approach where the server identifies the studio for each request and routes the data to the correct storage location and sync room.

## User Review Required

> [!IMPORTANT]
> **Studio Identification Strategy**: We will use a unique "Studio Key" for each office. This key must be entered once into the AutoCAD plugin and handled by the server to isolate data.
> 
> **Storage Model**: Initially, I recommend a **Shared Service Account** approach using different root folders for each studio. This is the fastest and most cost-effective way to scale. If a studio eventually requires its own dedicated Google Cloud project, the architecture will support swapping credentials per studio.

## Proposed Changes

We will modify both the Node.js server and the AutoCAD plugin.

### Server Path (Node.js)

#### [NEW] `studios.json`(file:///g:/SYNC-CAD/server/studios.json)
Create a registration file to map Studio Keys to Google Drive Folder IDs.
```json
{
  "ESTUDIO_DEMO_01": {
    "name": "Estudio Demo",
    "folderId": "1Tzed82o87YyLbLr_bt9EmSpqugROHWYo"
  }
}
```

#### [MODIFY] `index.js`(file:///g:/SYNC-CAD/server/index.js)
- Update global state (`syncHistory`, `layerLocks`) to be maps keyed by `studioId`.
- Implement a middleware to extract `x-studio-key` from headers.
- Update Socket.io to use rooms so that `sync_update` events only reach members of the same studio.

#### [MODIFY] `googleDriveService.js`(file:///g:/SYNC-CAD/server/googleDriveService.js)
- Ensure all functions continue receiving `folderId` as a parameter (already implemented, but verify usage).

---

### Plugin Path (C# / WPF)

#### [MODIFY] `CadSyncControl.xaml`(file:///g:/SYNC-CAD/plugin/CadSyncControl.xaml)
- Add a text box or a settings button to enter the "Studio Key".

#### [MODIFY] `CadSyncPlugin.cs`(file:///g:/SYNC-CAD/plugin/CadSyncPlugin.cs)
- Store the Studio Key in local settings.
- Add the `x-studio-key` header to all `HttpClient` requests.
- Update Socket.io connection to send the Studio Key during handshake so the server can put the client in the correct room.

## Verification Plan

### Automated Tests
- Scripted HTTP requests to `/api/files` with different Studio Keys to ensure isolation (Studio A should never see Studio B's files).
- Socket.io test script to verify room-restricted broadcasting.

### Manual Verification
- Run two instances of AutoCAD (or mock them) with different Studio Keys.
- Verify that a "Push" in Studio A does not show up in the history/UI of Studio B.
- Verify that a "Lock" in Studio A does not prevent Studio B from editing the same layer name.
