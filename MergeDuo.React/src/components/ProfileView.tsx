import { useState, type ChangeEvent, type ReactNode } from 'react';
import { uploadUserAvatar, type UserMeResponse } from '../api/identity';
import type { UserStatsResponse } from '../api/profile';

type StatsMeta = Pick<UserStatsResponse, 'source' | 'isStale'>;

export function ProfileView({
  user,
  accessToken,
  onUserChanged,
  statsMeta,
  statsRefreshing,
  statsError,
  onRefreshStats,
  onBack,
  darkMode,
  onToggleDark,
}: {
  user: UserMeResponse | null;
  accessToken: string;
  onUserChanged: (user: UserMeResponse) => void;
  statsMeta: StatsMeta | null;
  statsRefreshing: boolean;
  statsError: string | null;
  onRefreshStats: () => void;
  onBack: () => void;
  darkMode: boolean;
  onToggleDark: () => void;
}) {
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [avatarError, setAvatarError] = useState(false);

  async function handlePhotoChange(event: ChangeEvent<HTMLInputElement>) {
    const input = event.currentTarget;
    const file = input.files?.[0];
    if (!file || !user) return;

    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      setError('Use uma imagem JPG, PNG ou WebP.');
      input.value = '';
      return;
    }

    setUploading(true);
    setError(null);

    try {
      const uploaded = await uploadUserAvatar(accessToken, file);
      setAvatarError(false);
      onUserChanged({ ...user, avatarUrl: uploaded.avatarUrl });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Não foi possível atualizar a foto.');
    } finally {
      setUploading(false);
      input.value = '';
    }
  }

  if (!user) {
    return (
      <div className="pb-bottom-nav">
        <TopBar onBack={onBack} />
        <div className="px-5 mt-8 text-center text-sm text-ink-muted">
          Carregando perfil...
        </div>
      </div>
    );
  }

  return (
    <div className="pb-bottom-nav">
      <TopBar onBack={onBack} />

      <div className="px-4 sm:px-5">
        <div className="rounded-2xl bg-paper-card border border-paper-line shadow-soft-sm overflow-hidden">
          <div className="flex items-center gap-4 p-4">
            <div className="relative w-16 h-16 shrink-0">
              <div className="w-16 h-16 rounded-2xl bold-surface grid place-items-center text-lg font-semibold overflow-hidden">
                {user.avatarUrl && !avatarError ? (
                  <img
                    src={user.avatarUrl}
                    alt={user.name}
                    className="w-full h-full object-cover"
                    onError={() => setAvatarError(true)}
                  />
                ) : (
                  user.avatarInitials
                )}
              </div>
            </div>
            <div className="flex-1 min-w-0 text-left">
              <div className="text-base font-semibold text-ink truncate">{user.name}</div>
              <div className="text-xs text-ink-muted mt-0.5">{user.handle}</div>
              <label
                htmlFor="profile-photo-input"
                className={`mt-2 inline-flex items-center gap-1.5 h-7 px-3 rounded-full border border-paper-line text-[11px] font-medium text-ink-muted transition tap-surface hover:bg-paper-line hover:text-ink cursor-pointer ${
                  uploading ? 'pointer-events-none opacity-60' : ''
                }`}
              >
                <IconCamera />
                {uploading ? 'Enviando...' : 'Alterar foto'}
              </label>
              <input
                id="profile-photo-input"
                type="file"
                accept="image/jpeg,image/png,image/webp"
                onChange={handlePhotoChange}
                className="sr-only"
              />
            </div>
          </div>
          {error && (
            <div className="mx-4 mb-4 rounded-xl border border-accent-neg/20 bg-accent-neg/8 px-3 py-2 text-xs text-accent-neg">
              {error}
            </div>
          )}
        </div>
      </div>

      <Section title="Informações pessoais">
        <InfoRow icon={<IconMail />}     label="E-mail"       value={user.email} />
        <InfoRow icon={<IconPhone />}    label="Telefone"     value={user.phone ?? 'Não informado'} />
        <InfoRow icon={<IconCalendar />} label="Cadastro"     value={user.registeredAt} />
        <InfoRow icon={<IconStar />}     label="Membro desde" value={user.memberSince} />
        <InfoRow icon={<IconId />}       label="Usuário"      value={user.id} />
      </Section>

      <Section title="Preferências">
        <PrefRow label="Modo escuro" on={darkMode} onClick={onToggleDark} />
      </Section>

      <Section
        title="Resumo"
        action={
          <button
            onClick={onRefreshStats}
            disabled={statsRefreshing}
            title="Atualizar resumo"
            className={`inline-flex items-center gap-1.5 h-7 px-3 rounded-full border border-paper-line text-[11px] font-medium transition tap-surface ${
              statsRefreshing
                ? 'text-ink-muted opacity-60'
                : 'text-ink-muted hover:bg-paper-line hover:text-ink'
            }`}
          >
            <IconRefresh spinning={statsRefreshing} />
            {statsRefreshing ? 'Atualizando' : 'Atualizar'}
          </button>
        }
      >
        <InfoRow icon={<IconStar />} label="Transações" value={String(user.stats.transactionsTracked)} />
        <InfoRow icon={<IconCalendar />} label="Meses ativos" value={String(user.stats.activeMonths)} />
        <InfoRow
          icon={<IconClock />}
          label="Último cálculo"
          value={formatStatsDate(user.stats.lastRecomputedAt)}
        />
        <InfoRow
          icon={<IconPulse />}
          label="Status"
          value={statsMeta?.isStale ? 'Desatualizado' : statsMeta ? 'Atualizado' : 'Cache local'}
        />
        {statsError && (
          <div className="mt-3 rounded-xl border border-accent-neg/20 bg-accent-neg/10 px-3 py-2 text-xs text-accent-neg">
            {statsError}
          </div>
        )}
      </Section>

      <div className="px-5 mt-6 mb-4 text-center text-[10px] text-ink-muted/60">
        Merge Duo
      </div>
    </div>
  );
}

function TopBar({ onBack }: { onBack: () => void }) {
  return (
    <div className="px-4 sm:px-5 pt-3 pb-3 flex items-center gap-3">
      <button
        onClick={onBack}
        className="w-9 h-9 rounded-full grid place-items-center text-ink-muted hover:bg-paper-card border border-transparent hover:border-paper-line transition-all tap-surface"
        aria-label="Voltar"
      >
        <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
      </button>
      <div className="text-base font-semibold tracking-tight text-ink">Perfil</div>
    </div>
  );
}

function Section({
  title,
  action,
  children,
}: {
  title: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className="px-4 sm:px-5 mt-5">
      <div className="mb-2 px-1 flex items-center justify-between gap-3">
        <div className="text-[10px] uppercase tracking-[0.18em] font-semibold text-ink-muted">
          {title}
        </div>
        {action}
      </div>
      <div className="rounded-2xl bg-paper-card border border-paper-line shadow-soft-sm overflow-hidden">
        {children}
      </div>
    </div>
  );
}

function InfoRow({ icon, label, value }: { icon: ReactNode; label: string; value: string }) {
  return (
    <div className="flex items-center gap-3 px-4 py-3 border-b border-paper-line last:border-b-0">
      <div className="w-8 h-8 rounded-xl bg-paper-line grid place-items-center text-ink-muted shrink-0">
        {icon}
      </div>
      <div className="flex-1 min-w-0">
        <div className="text-[10px] uppercase tracking-wider text-ink-muted font-medium">{label}</div>
        <div className="text-sm text-ink truncate mt-0.5">{value}</div>
      </div>
    </div>
  );
}

function PrefRow({ label, on, onClick }: { label: string; on: boolean; onClick?: () => void }) {
  return (
    <div className="flex items-center justify-between px-4 py-3.5 border-b border-paper-line last:border-b-0">
      <div className="text-sm font-medium text-ink">{label}</div>
      <button
        onClick={onClick}
        role="switch"
        aria-checked={on}
        className={`w-11 h-6 rounded-full relative transition-colors tap-surface focus:outline-none ${
          on ? 'bold-surface' : 'bg-paper-line'
        } ${onClick ? 'cursor-pointer' : 'cursor-default opacity-60'}`}
      >
        <span
          className={`absolute top-0.5 w-5 h-5 rounded-full bg-white shadow-soft-sm transition-all duration-200 ${
            on ? 'left-[22px]' : 'left-0.5'
          }`}
        />
      </button>
    </div>
  );
}

const S = { width: 14, height: 14, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const };
function IconMail()     { return <svg {...S}><rect x="3" y="5" width="18" height="14" rx="2"/><polyline points="3 7 12 13 21 7"/></svg>; }
function IconPhone()    { return <svg {...S}><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.8 19.8 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.8 19.8 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.37 1.9.72 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.35 1.85.59 2.81.72A2 2 0 0 1 22 16.92z"/></svg>; }
function IconCalendar() { return <svg {...S}><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>; }
function IconId()       { return <svg {...S}><rect x="3" y="5" width="18" height="14" rx="2"/><circle cx="9" cy="12" r="2.5"/><line x1="14" y1="10" x2="18" y2="10"/><line x1="14" y1="14" x2="18" y2="14"/></svg>; }
function IconStar()     { return <svg {...S}><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>; }
function IconCamera()   { return <svg {...S}><path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"/><circle cx="12" cy="13" r="4"/></svg>; }
function IconClock()    { return <svg {...S}><circle cx="12" cy="12" r="9"/><polyline points="12 7 12 12 15 15"/></svg>; }
function IconPulse()    { return <svg {...S}><path d="M3 12h4l2-6 4 12 2-6h6"/></svg>; }
function IconRefresh({ spinning }: { spinning: boolean }) {
  return (
    <svg
      {...S}
      className={spinning ? 'animate-spin' : undefined}
    >
      <path d="M21 12a9 9 0 0 1-15.5 6.2" />
      <polyline points="3 18 5.5 18.2 5.8 15.7" />
      <path d="M3 12A9 9 0 0 1 18.5 5.8" />
      <polyline points="21 6 18.5 5.8 18.2 8.3" />
    </svg>
  );
}

function formatStatsDate(value: string | null) {
  if (!value) return 'Ainda não calculado';

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Ainda não calculado';

  return new Intl.DateTimeFormat('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);
}
