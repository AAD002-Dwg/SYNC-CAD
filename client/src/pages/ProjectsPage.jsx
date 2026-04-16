import { useState, useEffect } from 'react';
import { Plus, Trash2, FolderOpen, Calendar, FileCode2, ExternalLink } from 'lucide-react';
import axios from 'axios';
import { API_URL } from '../App';

const PALETTE = [
  '#55AAFF', '#4CAF50', '#FFC107', '#F44336',
  '#9C27B0', '#FF5722', '#00BCD4', '#795548',
  '#E91E63', '#009688',
];

export default function ProjectsPage() {
  const [projects,  setProjects]  = useState([]);
  const [files,     setFiles]     = useState([]);
  const [fileMeta,  setFileMeta]  = useState({});
  const [creating,  setCreating]  = useState(false);
  const [newName,   setNewName]   = useState('');
  const [newColor,  setNewColor]  = useState(PALETTE[0]);
  const [loading,   setLoading]   = useState(false);

  const load = async () => {
    setLoading(true);
    try {
      const [projRes, filesRes, metaRes] = await Promise.all([
        axios.get(`${API_URL}/projects`),
        axios.get(`${API_URL}/files`),
        axios.get(`${API_URL}/files/meta`)
      ]);
      setProjects(projRes.data ?? []);
      setFiles(filesRes.data ?? []);
      setFileMeta(metaRes.data ?? {});
    } catch { /* ignore */ }
    setLoading(false);
  };

  useEffect(() => { load(); }, []);

  const handleCreate = async () => {
    if (!newName.trim()) return;
    setLoading(true);
    try {
      await axios.post(`${API_URL}/projects`, { name: newName.trim(), color: newColor });
      setNewName('');
      setNewColor(PALETTE[0]);
      setCreating(false);
      load();
    } catch { /* ignore */ }
    setLoading(false);
  };

  const handleDelete = async (id) => {
    if (!window.confirm('¿Eliminar este proyecto?')) return;
    await axios.delete(`${API_URL}/projects/${id}`).catch(() => {});
    load();
  };

  const handleCancel = () => {
    setCreating(false);
    setNewName('');
    setNewColor(PALETTE[0]);
  };

  return (
    <div className="page">
      <div className="page-header">
        <h1 className="page-title">Proyectos</h1>
        {!creating && (
          <button
            className="ad-btn ad-btn--primary ad-btn--sm"
            onClick={() => setCreating(true)}
            style={{ display: 'flex', alignItems: 'center', gap: 5 }}
          >
            <Plus size={14} /> Nuevo Proyecto
          </button>
        )}
      </div>

      {/* Create form */}
      {creating && (
        <div className="ad-card create-form animate-in">
          <div className="ad-card__title" style={{ marginBottom: 4 }}>Nuevo Proyecto</div>
          <div style={{ color: 'var(--text-secondary)', fontSize: 'var(--fs-sm)', marginBottom: 'var(--sp-md)' }}>
            Los proyectos te permiten agrupar y organizar tus archivos DWG.
          </div>

          <input
            className="ad-input"
            placeholder="Nombre del proyecto (ej: Edificio Central)"
            value={newName}
            onChange={e => setNewName(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') handleCreate(); if (e.key === 'Escape') handleCancel(); }}
            autoFocus
          />

          <div>
            <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--text-hint)', textTransform: 'uppercase', letterSpacing: '0.8px', fontWeight: 600, marginBottom: 'var(--sp-sm)' }}>
              Color
            </div>
            <div className="color-picker">
              {PALETTE.map(c => (
                <button
                  key={c}
                  className={`color-dot ${newColor === c ? 'selected' : ''}`}
                  style={{ background: c }}
                  onClick={() => setNewColor(c)}
                  title={c}
                />
              ))}
            </div>
          </div>

          <div style={{ display: 'flex', gap: 'var(--sp-sm)' }}>
            <button
              className="ad-btn ad-btn--primary ad-btn--sm"
              onClick={handleCreate}
              disabled={loading || !newName.trim()}
            >
              {loading ? 'Creando...' : 'Crear Proyecto'}
            </button>
            <button className="ad-btn ad-btn--ghost ad-btn--sm" onClick={handleCancel}>
              Cancelar
            </button>
          </div>
        </div>
      )}

      {/* Projects grid */}
      {projects.length === 0 ? (
        <div className="empty-page">
          <FolderOpen size={44} style={{ opacity: 0.15, marginBottom: 'var(--sp-sm)' }} />
          <p style={{ fontWeight: 600, color: 'var(--text-secondary)' }}>Todavía no hay proyectos</p>
          <p style={{ fontSize: 'var(--fs-sm)' }}>Creá el primero usando el botón de arriba.</p>
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--sp-lg)' }}>
          {projects.map(p => {
            const projectFiles = files.filter(f => {
              const filename = typeof f === 'string' ? f : f.name;
              return fileMeta[filename]?.projectId === p.id;
            });
            return (
              <div key={p.id} className="ad-card animate-in" style={{ padding: 0, overflow: 'hidden' }}>
                <div style={{ display: 'flex', alignItems: 'center', padding: 'var(--sp-md)', borderBottom: '1px solid var(--border)' }}>
                  <div style={{ width: 12, height: 12, borderRadius: '50%', background: p.color, marginRight: 12 }} />
                  <div style={{ flex: 1 }}>
                    <div style={{ fontWeight: 600, fontSize: 'var(--fs-md)' }}>{p.name}</div>
                    <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--text-secondary)', display: 'flex', alignItems: 'center', gap: 4 }}>
                      <Calendar size={10} />
                      {new Date(p.createdAt).toLocaleDateString('es-AR')}
                    </div>
                  </div>
                  <a
                    href={`https://drive.google.com/drive/folders/${p.id}`}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="ad-btn ad-btn--icon"
                    title="Abrir carpeta en Google Drive"
                    style={{ marginRight: 8, color: 'var(--accent)' }}
                  >
                    <ExternalLink size={14} />
                  </a>
                  <button
                    className="ad-btn ad-btn--icon"
                    onClick={() => handleDelete(p.id)}
                    title="Eliminar proyecto"
                  >
                    <Trash2 size={13} style={{ color: 'var(--text-hint)' }} />
                  </button>
                </div>
                
                <div style={{ background: 'var(--bg-card-alt)', padding: 'var(--sp-md)' }}>
                  {projectFiles.length === 0 ? (
                    <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--text-hint)' }}>
                      No hay archivos maestro de AutoCAD vinculados a este proyecto.
                    </div>
                  ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                      {projectFiles.map(f => {
                        const filename = typeof f === 'string' ? f : f.name;
                        return (
                          <div key={filename} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '6px 8px', background: 'var(--bg-main)', borderRadius: 'var(--radius-base)', border: '1px solid var(--border)' }}>
                            <FileCode2 size={14} style={{ color: p.color }} />
                            <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--fs-sm)' }}>{filename}</span>
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
