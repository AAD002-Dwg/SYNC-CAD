import React, { useState } from 'react';
import { GoogleLogin } from '@react-oauth/google';
import axios from 'axios';
import { API_URL } from '../App';
import { User, LogIn, HardDrive } from 'lucide-react';

export default function LoginModal({ studioKey, onLoginSuccess, onClose, adminMode = false }) {
  const [guestName, setGuestName] = useState('');
  const [adminPin, setAdminPin] = useState('');
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(false);

  const handleGoogleSuccess = async (credentialResponse) => {
    setLoading(true);
    setError(null);
    try {
      if (adminMode) {
        // Admin linking Drive
        // Need to use useGoogleLogin for authorization code flow if we want refresh token
      } else {
        // User login
        const res = await axios.post(`${API_URL}/auth/google/user-login`, {
          credential: credentialResponse.credential,
          studioId: studioKey
        });
        onLoginSuccess(res.data.token, res.data.user.name, res.data.user);
      }
    } catch (err) {
      setError(err.response?.data?.error || 'Error al iniciar sesión');
    } finally {
      setLoading(false);
    }
  };

  const handleGuestLogin = () => {
    if (!guestName.trim()) {
      setError('Ingresa un nombre para continuar como invitado');
      return;
    }
    // We can issue a fake JWT for guests on the server or just use legacy mode
    // For simplicity, guests can just bypass JWT locally and use legacy standard
    // (In a real app, you'd request a guest JWT from the server)
    onLoginSuccess(null, `Invitado: ${guestName.trim().substring(0, 15)}`, null);
  };

  return (
    <div
      style={{
        position: 'fixed', inset: 0, zIndex: 200,
        background: 'rgba(0,0,0,0.6)', backdropFilter: 'blur(4px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center'
      }}
    >
      <div className="ad-card" style={{ width: 340, padding: 24 }}>
        <div className="ad-card__header" style={{ marginBottom: 20, textAlign: 'center', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
          <div style={{ background: 'var(--accent-subtle)', padding: 12, borderRadius: 50, marginBottom: 12, color: 'var(--accent)' }}>
            <LogIn size={24} />
          </div>
          <div className="ad-card__title">Identidad del Usuario</div>
          <div className="ad-card__subtitle" style={{ marginTop: 4 }}>
            Estudio: <strong style={{ color: 'var(--text-main)' }}>{studioKey}</strong>
          </div>
        </div>

        {error && (
          <div style={{ background: 'var(--error-subtle)', color: 'var(--error)', padding: '8px 12px', fontSize: 'var(--fs-sm)', borderRadius: 'var(--radius-base)', marginBottom: 16 }}>
            {error}
          </div>
        )}

        {/* -- Google Login -- */}
        <div style={{ marginBottom: 20, display: 'flex', justifyContent: 'center' }}>
          <GoogleLogin
            onSuccess={handleGoogleSuccess}
            onError={() => setError('Login con Google falló')}
            theme="filled_blue"
            shape="rectangular"
            text="signin_with"
            width="290"
          />
        </div>

        <div style={{ display: 'flex', alignItems: 'center', margin: '16px 0' }}>
           <div style={{ flex: 1, height: 1, background: 'var(--border)' }} />
           <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--text-hint)', padding: '0 12px' }}>O COMO INVITADO</div>
           <div style={{ flex: 1, height: 1, background: 'var(--border)' }} />
        </div>

        {/* -- Guest Login -- */}
        <div style={{ marginBottom: 16 }}>
          <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--text-secondary)', marginBottom: 6 }}>
            Nombre de Invitado
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <input
              className="ad-input"
              style={{ flex: 1 }}
              placeholder="Ej. Arquitecto Externo"
              value={guestName}
              onChange={e => setGuestName(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleGuestLogin()}
              disabled={loading}
            />
            <button 
                className="ad-btn ad-btn--primary" 
                onClick={handleGuestLogin}
                disabled={loading}
            >
              Entrar
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
