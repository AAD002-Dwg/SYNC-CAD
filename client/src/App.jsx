import { useState, useEffect, useCallback } from 'react';
import { BrowserRouter, Routes, Route, NavLink, useLocation } from 'react-router-dom';
import {
  LayoutDashboard, FolderOpen, FileCode2,
  ChevronLeft, Sun, Moon, Wifi, WifiOff, Menu, X, Key
} from 'lucide-react';
import axios from 'axios';
import { io } from 'socket.io-client';
import DashboardPage from './pages/DashboardPage';
import ProjectsPage from './pages/ProjectsPage';
import FilesPage from './pages/FilesPage';
import './index.css';

export const SOCKET_URL = window.location.hostname === 'localhost'
  ? 'http://localhost:3001'
  : window.location.origin;

export const API_URL = `${SOCKET_URL}/api`;

// ── Helpers ───────────────────────────────────────────────────
function getStoredKey()   { return localStorage.getItem('cad_studio_key') || ''; }
function getStoredUser()  { return localStorage.getItem('cad_user') || `User-${Math.floor(Math.random() * 1000)}`; }

/** Apply the studio key as a default header for all subsequent axios requests. */
function applyAxiosHeader(key) {
  if (key) {
    axios.defaults.headers.common['x-studio-key'] = key;
  } else {
    delete axios.defaults.headers.common['x-studio-key'];
  }
}

// Apply on module load so pages don't need to worry about it.
applyAxiosHeader(getStoredKey());

// ── Nav items ─────────────────────────────────────────────────
const NAV_ITEMS = [
  { to: '/',          icon: LayoutDashboard, label: 'Dashboard'  },
  { to: '/proyectos', icon: FolderOpen,      label: 'Proyectos'  },
  { to: '/archivos',  icon: FileCode2,       label: 'Archivos'   },
];

function PageTitle() {
  const location = useLocation();
  const match = NAV_ITEMS.find(item =>
    item.to === '/' ? location.pathname === '/' : location.pathname.startsWith(item.to)
  );
  return <span className="app-topbar__title">{match?.label ?? 'CAD Sync'}</span>;
}

// ── Studio Key modal ──────────────────────────────────────────
function StudioKeyModal({ currentKey, onSave, onClose }) {
  const [draft, setDraft] = useState(currentKey);

  const handleSave = () => {
    const key = draft.trim().toUpperCase();
    onSave(key);
    onClose();
  };

  return (
    <div
      style={{
        position: 'fixed', inset: 0, zIndex: 200,
        background: 'rgba(0,0,0,0.5)',
        display: 'flex', alignItems: 'center', justifyContent: 'center'
      }}
      onClick={onClose}
    >
      <div
        className="ad-card"
        style={{ width: 320, padding: 24 }}
        onClick={e => e.stopPropagation()}
      >
        <div className="ad-card__header" style={{ marginBottom: 16 }}>
          <div>
            <div className="ad-card__title">Studio Key</div>
            <div className="ad-card__subtitle">
              Identifica tu estudio. Contacta al administrador si no la conoces.
            </div>
          </div>
        </div>
        <input
          className="ad-input"
          style={{ fontFamily: 'var(--font-mono)', letterSpacing: '1px', marginBottom: 14 }}
          placeholder="ESTUDIO_DEMO_01"
          value={draft}
          onChange={e => setDraft(e.target.value.toUpperCase())}
          onKeyDown={e => e.key === 'Enter' && handleSave()}
          autoFocus
        />
        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
          <button className="ad-btn ad-btn--ghost ad-btn--sm" onClick={onClose}>Cancelar</button>
          <button className="ad-btn ad-btn--primary ad-btn--sm" onClick={handleSave}>Guardar</button>
        </div>
      </div>
    </div>
  );
}

// ── Main App Layout ───────────────────────────────────────────
function AppLayout({ theme, setTheme }) {
  const [collapsed,    setCollapsed]    = useState(false);
  const [mobileOpen,   setMobileOpen]   = useState(false);
  const [connected,    setConnected]    = useState(false);
  const [showKeyModal, setShowKeyModal] = useState(false);
  const [studioKey,    setStudioKey]    = useState(getStoredKey);
  const [user]                          = useState(getStoredUser);

  // Create socket with studioKey in auth handshake
  useEffect(() => {
    const socket = io(SOCKET_URL, {
      auth: { studioKey, user }
    });
    socket.on('connect',    () => setConnected(true));
    socket.on('disconnect', () => setConnected(false));
    return () => socket.disconnect();
  }, [studioKey, user]);

  const handleSaveKey = useCallback((key) => {
    localStorage.setItem('cad_studio_key', key);
    applyAxiosHeader(key);
    setStudioKey(key);
  }, []);

  const closeMobile = () => setMobileOpen(false);

  return (
    <div className="app-layout">
      {/* Studio Key Modal */}
      {showKeyModal && (
        <StudioKeyModal
          currentKey={studioKey}
          onSave={handleSaveKey}
          onClose={() => setShowKeyModal(false)}
        />
      )}

      {/* Mobile overlay */}
      {mobileOpen && <div className="sidebar-overlay" onClick={closeMobile} />}

      {/* Sidebar */}
      <aside className={`ad-sidebar ${collapsed ? 'collapsed' : ''} ${mobileOpen ? 'mobile-open' : ''}`}>
        <div className="ad-sidebar__header">
          <div className="ad-sidebar__logo">CS</div>
          <span className="ad-sidebar__brand">CAD Sync</span>
          <button
            className="ad-btn ad-btn--icon"
            onClick={closeMobile}
            style={{ marginLeft: 'auto', display: mobileOpen ? 'flex' : 'none' }}
          >
            <X size={15} />
          </button>
        </div>

        <nav className="ad-sidebar__nav">
          {NAV_ITEMS.map(({ to, icon: Icon, label }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              className={({ isActive }) => `ad-sidebar__item ${isActive ? 'active' : ''}`}
              onClick={closeMobile}
            >
              <span className="ad-sidebar__item-icon"><Icon size={16} /></span>
              <span className="ad-sidebar__item-label">{label}</span>
            </NavLink>
          ))}
        </nav>

        {/* Studio key indicator in sidebar footer */}
        <div className="ad-sidebar__footer">
          {!collapsed && studioKey && (
            <div style={{
              marginBottom: 8, padding: '4px 8px',
              background: 'var(--accent-subtle)', borderRadius: 'var(--radius-base)',
              border: '1px solid rgba(85,170,255,0.2)'
            }}>
              <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--text-hint)', marginBottom: 2 }}>
                ESTUDIO
              </div>
              <div style={{
                fontFamily: 'var(--font-mono)', fontSize: 'var(--fs-xs)',
                color: 'var(--accent)', letterSpacing: '0.5px',
                overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap'
              }}>
                {studioKey}
              </div>
            </div>
          )}
          <button
            className="ad-sidebar__toggle"
            onClick={() => setCollapsed(c => !c)}
            title={collapsed ? 'Expandir' : 'Colapsar'}
          >
            <ChevronLeft
              size={15}
              style={{ transform: collapsed ? 'rotate(180deg)' : 'none', transition: 'transform 0.25s' }}
            />
          </button>
        </div>
      </aside>

      {/* Main area */}
      <div className="app-content">
        <header className="app-topbar">
          {/* Mobile hamburger */}
          <button className="topbar-menu-btn" onClick={() => setMobileOpen(true)}>
            <Menu size={18} />
          </button>

          <PageTitle />
          <span className="app-topbar__badge">v1.2</span>

          <div className="topbar-spacer" />

          {/* Connection status */}
          <span
            className="topbar-status"
            style={{ color: connected ? 'var(--success)' : 'var(--error)' }}
          >
            {connected ? <Wifi size={12} /> : <WifiOff size={12} />}
            <span style={{ fontSize: 'var(--fs-xs)' }}>{connected ? 'Conectado' : 'Sin conexión'}</span>
          </span>

          {/* Studio Key button */}
          <button
            className="ad-btn ad-btn--ghost ad-btn--sm"
            onClick={() => setShowKeyModal(true)}
            title={studioKey ? `Estudio: ${studioKey}` : 'Configurar Studio Key'}
            style={{ color: studioKey ? 'var(--accent)' : 'var(--text-secondary)' }}
          >
            <Key size={13} />
          </button>

          {/* Theme toggle */}
          <button
            className="ad-btn ad-btn--ghost ad-btn--sm"
            onClick={() => setTheme(t => t === 'dark' ? 'light' : 'dark')}
            title={theme === 'dark' ? 'Cambiar a tema claro' : 'Cambiar a tema oscuro'}
          >
            {theme === 'dark' ? <Sun size={14} /> : <Moon size={14} />}
          </button>

          {/* User chip */}
          <span className="topbar-user" title={user}>{user}</span>
        </header>

        <main className="app-main">
          {!studioKey ? (
            <div className="empty-page" style={{ gap: 16 }}>
              <Key size={36} style={{ color: 'var(--text-hint)' }} />
              <div style={{ fontWeight: 600, color: 'var(--text-main)' }}>Studio Key no configurada</div>
              <div style={{ fontSize: 'var(--fs-sm)', textAlign: 'center' }}>
                Necesitas una Studio Key para acceder a los archivos de tu estudio.
              </div>
              <button
                className="ad-btn ad-btn--primary"
                onClick={() => setShowKeyModal(true)}
              >
                Configurar Studio Key
              </button>
            </div>
          ) : (
            <Routes>
              <Route path="/"          element={<DashboardPage />} />
              <Route path="/proyectos" element={<ProjectsPage />}  />
              <Route path="/archivos"  element={<FilesPage />}     />
            </Routes>
          )}
        </main>
      </div>
    </div>
  );
}

export default function App() {
  const [theme, setTheme] = useState(() => localStorage.getItem('cad_theme') || 'dark');

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('cad_theme', theme);
  }, [theme]);

  return (
    <BrowserRouter>
      <AppLayout theme={theme} setTheme={setTheme} />
    </BrowserRouter>
  );
}
