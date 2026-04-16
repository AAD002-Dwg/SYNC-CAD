import { useState, useEffect, useRef } from 'react';
import { Download, Upload, FileCode2, RefreshCw, Tag, HardDrive } from 'lucide-react';
import axios from 'axios';
import { API_URL } from '../App';

// ── Helpers ───────────────────────────────────────────
function formatBytes(bytes) {
  if (!bytes || bytes === '0') return '—';
  const b = parseInt(bytes, 10);
  if (isNaN(b) || b === 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(b) / Math.log(1024));
  return `${(b / Math.pow(1024, i)).toFixed(i > 0 ? 1 : 0)} ${units[i]}`;
}

export default function FilesPage() {
  const [files,    setFiles]    = useState([]);
  const [fileMeta, setFileMeta] = useState({});
  const [projects, setProjects] = useState([]);
  const [file,     setFile]     = useState(null);
  const [selProj,  setSelProj]  = useState('');
  const [uploading,setUploading]= useState(false);
  const [loading,  setLoading]  = useState(false);
  const [downloading, setDownloading] = useState(null);
  const [assigning,   setAssigning]   = useState(null); // filename being reassigned
  const fileInputRef = useRef();

  const [user] = useState(() => localStorage.getItem('cad_user') || 'Usuario');

  const load = async () => {
    setLoading(true);
    try {
      const [filesRes, metaRes, projRes] = await Promise.all([
        axios.get(`${API_URL}/files`),
        axios.get(`${API_URL}/files/meta`),
        axios.get(`${API_URL}/projects`),
      ]);
      setFiles(filesRes.data ?? []);
      setFileMeta(metaRes.data ?? {});
      setProjects(projRes.data ?? []);
    } catch { /* ignore */ }
    setLoading(false);
  };

  useEffect(() => { load(); }, []);

  const handleUpload = async () => {
    if (!file) return;
    setUploading(true);
    const formData = new FormData();
    formData.append('file', file);
    formData.append('user', user);
    if (selProj) formData.append('projectId', selProj);
    try {
      await axios.post(`${API_URL}/sync`, formData);
      setFile(null);
      setSelProj('');
      if (fileInputRef.current) fileInputRef.current.value = '';
      load();
    } catch (err) {
      alert(err.response?.data?.error ?? err.message);
    }
    setUploading(false);
  };

  const handleDownload = async (filename) => {
    setDownloading(filename);
    try {
      const response = await axios.get(
        `${API_URL}/download/${encodeURIComponent(filename)}`,
        { responseType: 'blob' }
      );
      const url = window.URL.createObjectURL(response.data);
      const a   = document.createElement('a');
      a.href     = url;
      a.download = filename;
      a.click();
      window.URL.revokeObjectURL(url);
    } catch {
      alert('Error al descargar el archivo.');
    }
    setDownloading(null);
  };

  const handleAssignProject = async (filename, projectId) => {
    setAssigning(filename);
    try {
      await axios.post(`${API_URL}/files/meta`, { filename, projectId });
      setFileMeta(prev => ({
        ...prev,
        [filename]: { ...(prev[filename] ?? {}), projectId }
      }));
    } catch { /* ignore */ }
    setAssigning(null);
  };

  return (
    <div className="page">
      <div className="page-header">
        <h1 className="page-title">Archivos DWG</h1>
        <button
          className="ad-btn ad-btn--ghost ad-btn--sm"
          onClick={load}
          disabled={loading}
          style={{ display: 'flex', alignItems: 'center', gap: 5 }}
        >
          <RefreshCw size={13} className={loading ? 'spin' : ''} />
          Actualizar
        </button>
      </div>

      {/* Upload panel */}
      <div className="ad-card upload-card">
        <div className="ad-card__title" style={{ marginBottom: 'var(--sp-sm)' }}>Subir Plano</div>
        <div className="upload-row">
          {/* Hidden real input */}
          <input
            ref={fileInputRef}
            type="file"
            accept=".dwg,.dxf"
            style={{ display: 'none' }}
            onChange={e => setFile(e.target.files[0] ?? null)}
          />
          <button
            className="ad-btn ad-btn--ghost ad-btn--sm"
            onClick={() => fileInputRef.current?.click()}
            style={{ display: 'flex', alignItems: 'center', gap: 5 }}
          >
            <FileCode2 size={13} />
            {file ? file.name : 'Elegir archivo .dwg/.dxf'}
          </button>

          <select
            className="ad-select"
            value={selProj}
            onChange={e => setSelProj(e.target.value)}
          >
            <option value="">Sin proyecto</option>
            {projects.map(p => (
              <option key={p.id} value={p.id}>{p.name}</option>
            ))}
          </select>

          <button
            className="ad-btn ad-btn--primary ad-btn--sm"
            onClick={handleUpload}
            disabled={!file || uploading}
            style={{ display: 'flex', alignItems: 'center', gap: 5 }}
          >
            <Upload size={13} />
            {uploading ? 'Subiendo...' : 'Sincronizar'}
          </button>
        </div>
      </div>

      {/* Files table */}
      <div className="ad-card" style={{ flex: 1, padding: 0, overflow: 'hidden' }}>
        <div className="ad-card__header" style={{ padding: 'var(--sp-lg)', paddingBottom: 0 }}>
          <div className="ad-card__title">Archivos en Drive</div>
          <span className="ad-badge ad-badge--accent">{files.length} archivos</span>
        </div>

        {loading ? (
          <div className="empty-state" style={{ padding: 'var(--sp-xxl)' }}>
            <RefreshCw size={22} className="spin" style={{ marginBottom: 8, opacity: 0.4 }} />
            Cargando archivos...
          </div>
        ) : files.length === 0 ? (
          <div className="empty-state" style={{ padding: 'var(--sp-xxl)' }}>
            <FileCode2 size={36} style={{ opacity: 0.15, marginBottom: 'var(--sp-sm)' }} />
            <p style={{ fontWeight: 600, color: 'var(--text-secondary)' }}>No hay archivos en Drive</p>
            <p style={{ fontSize: 'var(--fs-sm)' }}>Subí tu primer plano usando el panel de arriba.</p>
          </div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table className="ad-table">
              <thead>
              <tr>
                  <th>Archivo</th>
                  <th>Tamaño</th>
                  <th>Proyecto</th>
                  <th>Subido por</th>
                  <th>Fecha</th>
                  <th style={{ width: 80 }}></th>
                </tr>
              </thead>
              <tbody>
                {files.map(fileObj => {
                  // Support both old (string) and new (object) formats
                  const filename = typeof fileObj === 'string' ? fileObj : fileObj.name;
                  const fileSize = typeof fileObj === 'object' ? fileObj.size : null;
                   const meta = { ...(fileObj.meta || {}), ...(fileMeta[filename] || {}) };
                  const proj = projects.find(p => p.id === meta.projectId);

                  return (
                    <tr key={filename}>
                      {/* Filename */}
                      <td>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                          <FileCode2 size={14} style={{ color: 'var(--accent)', flexShrink: 0 }} />
                          <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--fs-sm)' }}>
                            {filename}
                          </span>
                        </div>
                      </td>

                      {/* Size */}
                      <td style={{ color: 'var(--text-secondary)', fontFamily: 'var(--font-mono)', fontSize: 'var(--fs-xs)' }}>
                        {formatBytes(fileSize)}
                      </td>

                      {/* Project — inline select to reassign */}
                      <td>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                          {proj && (
                            <span
                              className="ad-badge"
                              style={{
                                background: proj.color + '22',
                                color: proj.color,
                                border: `1px solid ${proj.color}44`,
                              }}
                            >
                              {proj.name}
                            </span>
                          )}
                          <select
                            className="ad-select"
                            value={meta.projectId ?? ''}
                            onChange={e => handleAssignProject(filename, e.target.value || null)}
                            disabled={assigning === filename}
                            style={{
                              width: 'auto',
                              minWidth: proj ? 28 : 130,
                              maxWidth: proj ? 28 : 180,
                              padding: proj ? '3px 20px 3px 6px' : '3px 20px 3px 6px',
                              fontSize: 'var(--fs-xs)',
                              opacity: assigning === filename ? 0.5 : 1,
                            }}
                            title="Cambiar proyecto"
                          >
                            <option value="">{proj ? '—' : 'Sin proyecto'}</option>
                            {projects.map(p => (
                              <option key={p.id} value={p.id}>{p.name}</option>
                            ))}
                          </select>
                        </div>
                      </td>

                      {/* Uploader */}
                      <td style={{ color: 'var(--text-secondary)' }}>
                        {meta.uploadedBy ?? '—'}
                      </td>

                      {/* Date */}
                      <td style={{ color: 'var(--text-secondary)', fontFamily: 'var(--font-mono)', fontSize: 'var(--fs-xs)' }}>
                        {meta.uploadedAt
                          ? new Date(meta.uploadedAt).toLocaleDateString('es-AR')
                          : '—'
                        }
                      </td>

                      {/* Download */}
                      <td>
                        <button
                          className="ad-btn ad-btn--ghost ad-btn--sm"
                          onClick={() => handleDownload(filename)}
                          disabled={downloading === filename}
                          style={{ display: 'flex', alignItems: 'center', gap: 4 }}
                        >
                          {downloading === filename
                            ? <RefreshCw size={11} className="spin" />
                            : <Download size={11} />
                          }
                          {downloading === filename ? '...' : 'Bajar'}
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
