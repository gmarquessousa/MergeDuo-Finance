/* eslint-disable react-refresh/only-export-components */
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { registerSW } from 'virtual:pwa-register';
import './index.css';
import App from './App.tsx';
import { validateProductionConfig } from './api/config';
import { installPreconnects } from './preconnect';

registerSW({ immediate: true });
installPreconnects();
const root = createRoot(document.getElementById('root')!);
const validation = validateProductionConfig();

if (!validation.ok) {
  root.render(
    <StrictMode>
      <ConfigErrorScreen missing={validation.missing} />
    </StrictMode>,
  );
} else {
  root.render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

function ConfigErrorScreen({ missing }: { missing: string[] }) {
  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '2rem',
        background: '#0b0b0c',
        color: '#f4f4f5',
        fontFamily: 'ui-sans-serif, system-ui, sans-serif',
      }}
    >
      <div style={{ maxWidth: 480 }}>
        <h1 style={{ fontSize: 18, fontWeight: 600, marginBottom: 8 }}>
          Configuração incompleta
        </h1>
        <p style={{ fontSize: 13, opacity: 0.8, marginBottom: 12 }}>
          O Merge Duo não pode iniciar em produção sem as variáveis de ambiente abaixo.
          Defina todas elas antes de fazer build novamente.
        </p>
        <ul
          style={{
            fontSize: 12,
            background: '#18181b',
            border: '1px solid #27272a',
            borderRadius: 12,
            padding: '0.75rem 1rem',
            listStyle: 'disc inside',
          }}
        >
          {missing.map((name) => (
            <li key={name} style={{ fontFamily: 'ui-monospace, SFMono-Regular, monospace' }}>
              {name}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
