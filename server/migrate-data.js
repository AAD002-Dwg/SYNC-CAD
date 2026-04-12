#!/usr/bin/env node
/**
 * migrate-data.js
 *
 * Migra el fichero legacy `app-data.json` (global, pre-multi-tenant) al nuevo
 * formato por estudio en `data/app-data-{studioId}.json`.
 *
 * Uso:
 *   node migrate-data.js [studioId]
 *
 * Si no se especifica studioId se usan todas las entradas de studios.json.
 * El fichero original NO se borra; se renombra a app-data.backup.json.
 */

const fs   = require('fs');
const path = require('path');

const LEGACY_FILE  = path.join(__dirname, 'app-data.json');
const STUDIOS_FILE = path.join(__dirname, 'studios.json');
const DATA_DIR     = path.join(__dirname, 'data');

// ── Load studios ──────────────────────────────────────────────
if (!fs.existsSync(STUDIOS_FILE)) {
    console.error('❌  studios.json no encontrado. Crea el registro primero.');
    process.exit(1);
}
const studios = JSON.parse(fs.readFileSync(STUDIOS_FILE, 'utf8'));

// ── Target studio(s) ─────────────────────────────────────────
const targetId = process.argv[2];
const studioIds = targetId
    ? [targetId]
    : Object.keys(studios);

if (studioIds.length === 0) {
    console.error('❌  No hay estudios en studios.json.');
    process.exit(1);
}

// ── Load legacy data ──────────────────────────────────────────
if (!fs.existsSync(LEGACY_FILE)) {
    console.log('ℹ️   No existe app-data.json. Nada que migrar.');
    process.exit(0);
}
const legacy = JSON.parse(fs.readFileSync(LEGACY_FILE, 'utf8'));

if (!legacy.projects?.length && !Object.keys(legacy.fileMeta ?? {}).length) {
    console.log('ℹ️   app-data.json está vacío. Nada que migrar.');
    process.exit(0);
}

// ── Ensure data dir ───────────────────────────────────────────
if (!fs.existsSync(DATA_DIR)) fs.mkdirSync(DATA_DIR);

// ── Migrate ───────────────────────────────────────────────────
for (const studioId of studioIds) {
    if (!studios[studioId]) {
        console.warn(`⚠️   Studio '${studioId}' no encontrado en studios.json. Saltando.`);
        continue;
    }

    const destFile = path.join(DATA_DIR, `app-data-${studioId}.json`);

    // Merge with existing if present
    let existing = { projects: [], fileMeta: {} };
    if (fs.existsSync(destFile)) {
        try { existing = JSON.parse(fs.readFileSync(destFile, 'utf8')); } catch {}
    }

    const merged = {
        projects: [...(existing.projects ?? []), ...(legacy.projects ?? [])],
        fileMeta: { ...(existing.fileMeta ?? {}), ...(legacy.fileMeta ?? {}) }
    };

    fs.writeFileSync(destFile, JSON.stringify(merged, null, 2));
    console.log(`✅  Migrado → ${destFile}  (${merged.projects.length} proyectos, ${Object.keys(merged.fileMeta).length} archivos)`);
}

// ── Rename legacy ─────────────────────────────────────────────
const backupFile = path.join(__dirname, 'app-data.backup.json');
fs.renameSync(LEGACY_FILE, backupFile);
console.log(`\n📦  app-data.json renombrado a app-data.backup.json (conservado como respaldo).`);
console.log('\n✔  Migración completada. Reinicia el servidor.');
