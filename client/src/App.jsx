import { useState, useEffect } from 'react';
import { BrowserRouter, Routes, Route, NavLink, useLocation } from 'react-router-dom';
import { LayoutDashboard, FolderOpen, FileCode2, ChevronLeft, Sun, Moon, Wifi, WifiOff, Menu, X } from 'lucide-react';
import { io } from 'socket.io-client';
import DashboardPage from './pages/DashboardPage';
import ProjectsPage from './pages/ProjectsPage';
import FilesPage from './pages/FilesPage';
import './index.css';

export const SOCKET_URL = window.location.hostname === 'localhost'
  ? 'http://localhost:3001'
  : window.location.origin;

export const API_URL = `${SOCKET_URL}/api`;

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

function AppLayout({ theme, setTheme }) {
  const [collapsed, setCollapsed]       = useState(false);
  const [mobileOpen, setMobileOpen]     = useState(false);
  const [connected, setConnected]       = useState(false);
  const [user]                          = useState(
    () => localStorage.getItem('cad_user') || `User-${Math.floor(Math.random() * 1000)}`
  );

  useEffect(() => {
    const socket = io(SOCKET_URL);
    socket.on('connect',    () => setConnected(true));
    socket.on('disconnect', () => setConnected(false));
    return () => socket.disconnect();
  }, []);

  const closeMobile = () => setMobileOpen(false);

  return (
    <div className="app-layout">

      {/* Mobile overlay */}
      {mobileOpen && (
        <div className="sidebar-overlay" onClick={closeMobile} />
      )}

      {/* Sidebar */}
      <aside className={`ad-sidebar ${collapsed ? 'collapsed' : ''} ${mobileOpen ? 'mobile-open' : ''}`}>
        <div className="ad-sidebar__header">
          <div className="ad-sidebar__logo">CS</div>
          <span className="ad-sidebar__brand">CAD Sync</span>
          {/* Close button on mobile */}
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

        <div className="ad-sidebar__footer">
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
          <Routes>
            <Route path="/"          element={<DashboardPage />} />
            <Route path="/proyectos" element={<ProjectsPage />}  />
            <Route path="/archivos"  element={<FilesPage />}     />
          </Routes>
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
