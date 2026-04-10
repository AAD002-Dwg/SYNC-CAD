import { useState, useEffect } from 'react';
import axios from 'axios';
import { io } from 'socket.io-client';
import './App.css';

// URL dinámica: Si estamos en producción, usamos el mismo host.
const SOCKET_URL = window.location.hostname === 'localhost' ? 'http://localhost:3001' : window.location.origin;
const API_URL = `${SOCKET_URL}/api`;

function App() {
  const [history, setHistory] = useState([]);
  const [locks, setLocks] = useState({});
  const [file, setFile] = useState(null);
  const [user, setUser] = useState(() => localStorage.getItem('cad_user') || `User-${Math.floor(Math.random() * 1000)}`);
  const [status, setStatus] = useState('Conectado');
  const [serverInfo, setServerInfo] = useState({ ip: '...', url: SOCKET_URL });

  useEffect(() => {
    localStorage.setItem('cad_user', user);
    
    // Cargar status inicial
    axios.get(`${API_URL}/status`)
      .then(res => {
        setHistory(res.data.history);
        setLocks(res.data.locks);
        setServerInfo({ ip: res.data.serverIp, url: SOCKET_URL });
      })
      .catch(err => console.error("Error:", err));

    const socket = io(SOCKET_URL);
    socket.on('connect', () => setStatus('Sincronizado'));
    socket.on('sync_update', (newEntry) => setHistory(prev => [newEntry, ...prev]));
    socket.on('lock_update', (newLocks) => setLocks(newLocks));

    return () => socket.disconnect();
  }, [user]);

  const handleUpload = async () => {
    if (!file) return;
    const formData = new FormData();
    formData.append('file', file);
    formData.append('user', user);
    formData.append('project', 'Cloud-Sync');
    try {
      await axios.post(`${API_URL}/sync`, formData);
      setFile(null);
    } catch (err) { alert(err.message); }
  };

  const layers = ['ARQUITECTURA', 'ESTRUCTURA', 'ELECTRICIDAD', 'FONTANERIA'];

  const toggleLock = async (layer) => {
    const isLockedByMe = locks[layer]?.user === user;
    if (isLockedByMe) {
      await axios.post(`${API_URL}/unlock`, { layer, user });
    } else {
      try {
        await axios.post(`${API_URL}/lock`, { layer, user });
      } catch (err) {
        alert(err.response?.data?.error || "Error al bloquear");
      }
    }
  };

  return (
    <div className="App">
      <header>
        <h1>CAD Sync Cloud</h1>
        <div style={{ background: 'rgba(56, 189, 248, 0.1)', padding: '0.5rem', borderRadius: '8px', fontSize: '0.8rem' }}>
          Configuración Plugin: <strong>{serverInfo.url}</strong> (IP: {serverInfo.ip})
        </div>
      </header>

      <div className="card">
        <section style={{ marginBottom: '2rem' }}>
          <h3>Panel de Capas (Modelo A)</h3>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px' }}>
            {layers.map(l => (
              <div key={l} style={{ 
                padding: '1rem', 
                borderRadius: '12px', 
                background: locks[l] ? 'rgba(239, 68, 68, 0.1)' : 'rgba(34, 197, 94, 0.1)',
                border: `1px solid ${locks[l] ? '#ef4444' : '#22c55e'}`,
                display: 'flex', justifyContent: 'space-between', alignItems: 'center'
              }}>
                <span>{l}</span>
                <button 
                  onClick={() => toggleLock(l)}
                  style={{ 
                    padding: '4px 12px', fontSize: '0.8rem',
                    background: locks[l]?.user === user ? '#ef4444' : (locks[l] ? '#475569' : '#22c55e')
                  }}
                  disabled={locks[l] && locks[l].user !== user}
                >
                  {locks[l]?.user === user ? 'Liberar' : (locks[l] ? 'Ocupado' : 'Reservar')}
                </button>
              </div>
            ))}
          </div>
        </section>

        <section>
          <h3>Subir Nueva Versión</h3>
          <input type="file" accept=".dwg,.dxf" onChange={(e) => setFile(e.target.files[0])} />
          <button onClick={handleUpload} disabled={!file}>Sincronizar Plano</button>
        </section>

        <section style={{ marginTop: '2rem' }}>
          <h3 style={{ borderBottom: '1px solid rgba(255,255,255,0.1)', paddingBottom: '0.5rem' }}>Historial</h3>
          <div style={{ maxHeight: '200px', overflowY: 'auto' }}>
            {history.map((item, index) => (
              <div key={index} className="history-item">
                <span>{item.filename} por <strong>{item.user}</strong></span>
                <span className="timestamp">{new Date(item.timestamp).toLocaleTimeString()}</span>
              </div>
            ))}
          </div>
        </section>
      </div>
    </div>
  );
}

export default App;
