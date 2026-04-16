import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { FolderOpen, FileCode2, Lock, Clock, ArrowRight, Key, Copy, Check } from 'lucide-react';
import axios from 'axios';
import { io } from 'socket.io-client';
import { API_URL, SOCKET_URL } from '../App';

function KPICard({ label, value, icon: Icon, color }) {
  return (
    <div className="ad-card kpi-card" style={{ '--kpi-color': color }}>
      <div className="kpi-card__icon" style={{ color }}>
        <Icon size={18} />
      </div>
      <div className="kpi-card__label">{label}</div>
      <div className="kpi-card__value">{value ?? '—'}</div>
    </div>
  );
}

export default function DashboardPage() {
  const navigate = useNavigate();
  const [history,  setHistory]  = useState([]);
  const [locks,    setLocks]    = useState({});
  const [files,    setFiles]    = useState([]);
  const [projects, setProjects] = useState([]);
  const [desktopToken, setDesktopToken] = useState('');
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    axios.get(`${API_URL}/status`).then(r => {
      setHistory(r.data.history ?? []);
      setLocks(r.data.locks ?? {});
    }).catch(() => {});

    axios.get(`${API_URL}/files`).then(r => setFiles(r.data)).catch(() => {});
    axios.get(`${API_URL}/projects`).then(r => setProjects(r.data)).catch(() => {});

    const socket = io(SOCKET_URL);
    socket.on('sync_update', entry => setHistory(h => [entry, ...h].slice(0, 50)));
    socket.on('lock_update', newLocks => setLocks(newLocks));
    return () => socket.disconnect();
  }, []);

  const activeLocks = Object.keys(locks).length;
  const lastSync    = history[0];

  const handleGenerateToken = async () => {
    try {
      const res = await axios.post(`${API_URL}/auth/desktop-token`);
      setDesktopToken(res.data.desktopToken);
    } catch {
      alert("Error al generar token de escritorio");
    }
  };

  const handleCopy = () => {
    navigator.clipboard.writeText(desktopToken);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="page">
      <div className="page-header">
        <h1 className="page-title">Dashboard</h1>
      </div>

      {/* KPIs */}
      <div className="kpi-grid">
        <KPICard
          label="Proyectos"
          value={projects.length}
          icon={FolderOpen}
          color="var(--accent)"
        />
        <KPICard
          label="Archivos DWG"
          value={files.length}
          icon={FileCode2}
          color="var(--success)"
        />
        <KPICard
          label="Capas en uso"
          value={activeLocks}
          icon={Lock}
          color={activeLocks > 0 ? 'var(--warning)' : 'var(--text-disabled)'}
        />
        <KPICard
          label="Última sincronización"
          value={lastSync
            ? new Date(lastSync.timestamp).toLocaleTimeString('es-AR', { hour: '2-digit', minute: '2-digit' })
            : '—'
          }
          icon={Clock}
          color="var(--text-secondary)"
        />
      </div>

      {/* Bottom row */}
      <div className="dashboard-bottom">

        {/* Activity feed */}
        <div className="ad-card" style={{ flex: 2, minWidth: 0 }}>
          <div className="ad-card__header">
            <div className="ad-card__title">Actividad Reciente</div>
            <button
              className="ad-btn ad-btn--ghost ad-btn--sm"
              onClick={() => navigate('/archivos')}
              style={{ display: 'flex', alignItems: 'center', gap: 4 }}
            >
              Ver archivos <ArrowRight size={12} />
            </button>
          </div>
          <div className="ad-card__content">
            {history.length === 0 ? (
              <div className="empty-state">Sin actividad reciente</div>
            ) : (
              <div className="activity-list">
                {history.slice(0, 10).map((item, i) => (
                  <div key={i} className="activity-item">
                    <div className="activity-item__dot" />
                    <div className="activity-item__info">
                      <span className="activity-item__filename">{item.filename}</span>
                      <span className="activity-item__meta">
                        por <strong>{item.user}</strong>
                        {item.layer ? ` · capa ${item.layer}` : ''}
                      </span>
                    </div>
                    <span className="activity-item__time">
                      {new Date(item.timestamp).toLocaleTimeString('es-AR', { hour: '2-digit', minute: '2-digit' })}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Active locks */}
        <div className="ad-card" style={{ flex: 1, minWidth: 200 }}>
          <div className="ad-card__header">
            <div className="ad-card__title">Capas Activas</div>
            {activeLocks > 0 && (
              <span className="ad-badge ad-badge--warning">{activeLocks}</span>
            )}
          </div>
          <div className="ad-card__content">
            {activeLocks === 0 ? (
              <div className="empty-state" style={{ padding: 'var(--sp-lg) 0' }}>
                <Lock size={24} style={{ opacity: 0.2, marginBottom: 6 }} />
                Ninguna capa reservada
              </div>
            ) : (
              <div className="locks-list">
                {Object.entries(locks).map(([layer, info]) => (
                  <div key={layer} className="lock-item">
                    <span className="lock-item__badge">{layer}</span>
                    <span className="lock-item__user">{info.user}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

      </div>

      {/* Plugin Configuration */}
      <div className="ad-card" style={{ marginTop: 'var(--sp-lg)' }}>
        <div className="ad-card__header">
          <div className="ad-card__title">Vinculación de AutoCAD</div>
        </div>
        <div className="ad-card__content" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <p style={{ fontSize: 'var(--fs-sm)', color: 'var(--text-secondary)' }}>
            Para vincular tu plugin de AutoCAD a tu identidad de usuario actual, genera un Token de Escritorio y pégalo en la configuración del plugin.
          </p>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <button className="ad-btn ad-btn--primary ad-btn--sm" onClick={handleGenerateToken}>
              <Key size={14} style={{ marginRight: 6 }} /> Generar Token
            </button>
            {desktopToken && (
               <div style={{ display: 'flex', gap: 4, alignItems: 'center' }}>
                 <input 
                   readOnly 
                   className="ad-input" 
                   value={desktopToken} 
                   style={{ width: 300, fontFamily: 'var(--font-mono)', fontSize: 'var(--fs-xs)' }} 
                 />
                 <button className="ad-btn ad-btn--icon" onClick={handleCopy}>
                   {copied ? <Check size={14} color="var(--success)" /> : <Copy size={14} />}
                 </button>
               </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
