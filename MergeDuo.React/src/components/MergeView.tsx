import { useEffect, useState, type ReactNode } from 'react';
import {
  acceptInvite,
  createInvite,
  endPartnership,
  getCurrentPartnership,
  PartnershipApiError,
  previewInvite,
  revokeInvite,
  toMergePartnerInfo,
  type CreateInviteResponse,
  type InvitePreviewResponse,
} from '../api/partnership';
import { useFinance } from '../store';
import { useRefresh } from '../refreshContext';
import type { MergePartnerInfo } from '../types';

type MergeViewState = 'loading' | 'none' | 'pendingInvite' | 'acceptInvite' | 'active' | 'paused' | 'error';
type BusyAction = 'create' | 'preview' | 'accept' | 'revoke' | 'end' | null;

export function MergeView({
  accessToken,
  pendingInviteToken = null,
  onInviteHandled,
  onBack,
}: {
  accessToken: string;
  pendingInviteToken?: string | null;
  onInviteHandled?: () => void;
  onBack: () => void;
}) {
  const { partner, setPartnership, clearPartnership } = useFinance();
  const refreshCtx = useRefresh();
  const inviteToken = pendingInviteToken?.trim() || null;
  const [view, setView] = useState<MergeViewState>('loading');
  const [invite, setInvite] = useState<CreateInviteResponse | null>(null);
  const [preview, setPreview] = useState<InvitePreviewResponse | null>(null);
  const [busy, setBusy] = useState<BusyAction>(null);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    let cancelled = false;

    async function loadCurrentPartnership() {
      if (!inviteToken) {
        setView('loading');
      }

      setError(null);
      try {
        const current = await getCurrentPartnership(accessToken);
        if (cancelled) return;

        const next = current.partnership ? toMergePartnerInfo(current.partnership) : null;
        if (next) {
          setPartnership(next);
        } else {
          clearPartnership();
        }

        if (!inviteToken) {
          setView(viewForPartnership(next));
        }
      } catch (err) {
        if (cancelled || inviteToken) return;
        setError(toPartnershipMessage(err));
        setView('error');
      }
    }

    void loadCurrentPartnership();

    return () => {
      cancelled = true;
    };
  }, [accessToken, clearPartnership, inviteToken, setPartnership]);

  useEffect(() => {
    if (!inviteToken) {
      setPreview(null);
      return;
    }

    const token = inviteToken;
    let cancelled = false;

    async function loadInvitePreview() {
      setView('acceptInvite');
      setBusy('preview');
      setError(null);
      setPreview(null);

      try {
        const response = await previewInvite(token);
        if (!cancelled) {
          setPreview(response);
        }
      } catch (err) {
        if (!cancelled) {
          setError(toPartnershipMessage(err));
        }
      } finally {
        if (!cancelled) {
          setBusy(null);
        }
      }
    }

    void loadInvitePreview();

    return () => {
      cancelled = true;
    };
  }, [inviteToken]);

  async function refreshCurrentPartnership() {
    const current = await getCurrentPartnership(accessToken);
    const next = current.partnership ? toMergePartnerInfo(current.partnership) : null;

    if (next) {
      setPartnership(next);
    } else {
      clearPartnership();
    }

    setView(viewForPartnership(next));
    return next;
  }

  async function handleCreateInvite() {
    setBusy('create');
    setError(null);

    try {
      const response = await createInvite(accessToken, 'link');
      setInvite(response);
      setView('pendingInvite');
    } catch (err) {
      if (err instanceof PartnershipApiError && err.code === 'partnership_already_exists') {
        await refreshCurrentPartnership().catch(() => {
          setError(toPartnershipMessage(err));
          setView('error');
        });
      } else {
        setError(toPartnershipMessage(err));
        setView('none');
      }
    } finally {
      setBusy(null);
    }
  }

  async function handleRevokeInvite() {
    if (!invite) return;

    setBusy('revoke');
    setError(null);
    try {
      await revokeInvite(accessToken, invite.token);
      setInvite(null);
      setView('none');
    } catch (err) {
      setError(toPartnershipMessage(err));
    } finally {
      setBusy(null);
    }
  }

  async function handleAcceptInvite() {
    if (!inviteToken) return;

    setBusy('accept');
    setError(null);
    try {
      await acceptInvite(accessToken, inviteToken);
      await refreshCurrentPartnership();
      refreshCtx?.refreshAll();
      onInviteHandled?.();
    } catch (err) {
      setError(toPartnershipMessage(err));
      setView('acceptInvite');
    } finally {
      setBusy(null);
    }
  }

  async function handleEndPartnership(partnership: MergePartnerInfo) {
    setBusy('end');
    setError(null);
    try {
      await endPartnership(accessToken, partnership.id);
      clearPartnership();
      setInvite(null);
      setView('none');
      refreshCtx?.refreshAll();
    } catch (err) {
      setError(toPartnershipMessage(err));
    } finally {
      setBusy(null);
    }
  }

  function handleDismissInvite() {
    setPreview(null);
    setError(null);
    onInviteHandled?.();
    setView(viewForPartnership(partner));
  }

  function copyText(text: string) {
    navigator.clipboard?.writeText(text).catch(() => {});
    setCopied(true);
    window.setTimeout(() => setCopied(false), 2200);
  }

  const currentPartner = partner?.status === 'active' || partner?.status === 'paused'
    ? partner
    : null;

  if (view === 'loading') {
    return (
      <div className="pb-bottom-nav">
        <TopBar onBack={onBack} />
        <CenteredState label="Carregando Merge..." />
      </div>
    );
  }

  if (view === 'acceptInvite') {
    return (
      <InviteAcceptView
        preview={preview}
        loading={busy === 'preview'}
        accepting={busy === 'accept'}
        error={error}
        onAccept={handleAcceptInvite}
        onCancel={handleDismissInvite}
        onBack={onBack}
      />
    );
  }

  if ((view === 'active' || view === 'paused') && currentPartner) {
    return (
      <ConnectedView
        partner={currentPartner}
        ending={busy === 'end'}
        error={error}
        onBack={onBack}
        onLeaveMerge={() => void handleEndPartnership(currentPartner)}
      />
    );
  }

  return (
    <InviteCreateView
      invite={view === 'pendingInvite' ? invite : null}
      copied={copied}
      busy={busy}
      error={error}
      onBack={onBack}
      onCreateInvite={() => void handleCreateInvite()}
      onCopyInvite={copyText}
      onRevokeInvite={() => void handleRevokeInvite()}
    />
  );
}

function InviteCreateView({
  invite,
  copied,
  busy,
  error,
  onBack,
  onCreateInvite,
  onCopyInvite,
  onRevokeInvite,
}: {
  invite: CreateInviteResponse | null;
  copied: boolean;
  busy: BusyAction;
  error: string | null;
  onBack: () => void;
  onCreateInvite: () => void;
  onCopyInvite: (text: string) => void;
  onRevokeInvite: () => void;
}) {
  return (
    <div className="pb-bottom-nav">
      <TopBar onBack={onBack} />

      <div className="px-5">
        <div className="rounded-2xl bg-paper-card border border-paper-line p-5 shadow-soft text-center">
          <div className="text-base font-semibold text-ink">Convide seu parceiro</div>
          <div className="text-xs text-ink-muted mt-1 leading-relaxed">
            Gere um convite real pelo Partnership API. Quando a outra pessoa aceitar, o Merge fica ativo.
          </div>

          {error && <ErrorMessage message={error} />}

          {invite ? (
            <>
              <div className="flex justify-center mt-5 mb-4">
                <div className="p-3 rounded-xl border border-paper-line bg-white inline-block shadow-soft">
                  <MockQRCode seed={invite.qrPayload} size={168} />
                </div>
              </div>

              <div className="flex items-center gap-2 rounded-xl border border-paper-line bg-paper px-3 h-10 text-left">
                <div className="flex-1 min-w-0 text-xs text-ink truncate">{invite.inviteUrl}</div>
                <button
                  onClick={() => onCopyInvite(invite.inviteUrl)}
                  className="shrink-0 text-[11px] font-medium text-ink-muted hover:text-ink transition"
                >
                  {copied ? 'Copiado' : 'Copiar'}
                </button>
              </div>

              <div className="mt-2 text-[11px] text-ink-muted">
                Expira em {formatDateTime(invite.expiresAt)}
              </div>

              <button
                onClick={() => onCopyInvite(invite.inviteUrl)}
                className="mt-3 w-full h-10 rounded-full bold-surface text-sm font-medium flex items-center justify-center gap-2"
              >
                <IconShare />
                Compartilhar link
              </button>

              <button
                onClick={onRevokeInvite}
                disabled={busy === 'revoke'}
                className={`mt-2 w-full h-10 rounded-full border border-accent-neg/20 bg-accent-neg/10 text-accent-neg text-xs font-medium ${
                  busy === 'revoke' ? 'opacity-60' : ''
                }`}
              >
                {busy === 'revoke' ? 'Revogando...' : 'Revogar convite'}
              </button>
            </>
          ) : (
            <button
              onClick={onCreateInvite}
              disabled={busy === 'create'}
              className={`mt-5 w-full h-10 rounded-full bold-surface text-sm font-medium ${
                busy === 'create' ? 'opacity-60' : ''
              }`}
            >
              {busy === 'create' ? 'Gerando...' : 'Gerar convite'}
            </button>
          )}
        </div>
      </div>

      <div className="px-5 mt-4">
        <div className="rounded-2xl bg-paper-card border border-paper-line p-4 shadow-soft space-y-3">
          <InfoItem
            icon={<IconPeople />}
            title="Apenas um convidado"
            desc="O Merge conecta você com uma outra pessoa por vez."
          />
          <div className="h-px bg-paper-line" />
          <InfoItem
            icon={<IconEye />}
            title="Visão financeira compartilhada"
            desc="Quando o Merge está ativo você pode alternar entre você, seu parceiro e a soma dos dois."
          />
          <div className="h-px bg-paper-line" />
          <InfoItem
            icon={<IconShield />}
            title="Você controla"
            desc="O convite pode ser revogado e o compartilhamento pode ser encerrado."
          />
        </div>
      </div>
    </div>
  );
}

function InviteAcceptView({
  preview,
  loading,
  accepting,
  error,
  onAccept,
  onCancel,
  onBack,
}: {
  preview: InvitePreviewResponse | null;
  loading: boolean;
  accepting: boolean;
  error: string | null;
  onAccept: () => void;
  onCancel: () => void;
  onBack: () => void;
}) {
  return (
    <div className="pb-bottom-nav">
      <TopBar onBack={onBack} />

      <div className="px-5">
        <div className="rounded-2xl bg-paper-card border border-paper-line p-5 shadow-soft text-center">
          <div className="text-base font-semibold text-ink">Aceitar convite Merge</div>
          <div className="text-xs text-ink-muted mt-1 leading-relaxed">
            Confira quem enviou o convite antes de conectar os perfis.
          </div>

          {loading ? (
            <CenteredState label="Carregando convite..." compact />
          ) : preview ? (
            <div className="mt-5 rounded-xl border border-paper-line bg-paper p-4 text-left">
              <div className="flex items-center gap-3">
                <div className="w-12 h-12 rounded-full bold-surface grid place-items-center text-sm font-semibold">
                  {preview.inviter.initials}
                </div>
                <div className="min-w-0">
                  <div className="text-sm font-semibold text-ink truncate">
                    {preview.inviter.name}
                  </div>
                  <div className="text-xs text-ink-muted">{preview.inviter.handle}</div>
                  <div className="mt-1 text-[10px] text-ink-muted">
                    Expira em {formatDateTime(preview.expiresAt)}
                  </div>
                </div>
              </div>
            </div>
          ) : null}

          {error && <ErrorMessage message={error} />}

          <button
            onClick={onAccept}
            disabled={!preview || accepting || loading}
            className={`mt-5 w-full h-10 rounded-full bold-surface text-sm font-medium ${
              !preview || accepting || loading ? 'opacity-60' : ''
            }`}
          >
            {accepting ? 'Aceitando...' : 'Aceitar convite'}
          </button>
          <button
            onClick={onCancel}
            className="mt-2 w-full h-10 rounded-full border border-paper-line text-xs font-medium text-ink-muted hover:text-ink transition"
          >
            Cancelar
          </button>
        </div>
      </div>
    </div>
  );
}

function ConnectedView({
  partner,
  ending,
  error,
  onBack,
  onLeaveMerge,
}: {
  partner: MergePartnerInfo;
  ending: boolean;
  error: string | null;
  onBack: () => void;
  onLeaveMerge: () => void;
}) {
  const active = partner.status === 'active';

  return (
    <div className="pb-bottom-nav">
      <TopBar onBack={onBack} />

      <div className="px-5">
        <div className="rounded-2xl bg-paper-card border border-paper-line p-4 shadow-soft">
          <div className="flex items-center gap-3">
            <div className="w-12 h-12 rounded-full bold-surface grid place-items-center text-sm font-semibold">
              {partner.initials}
            </div>
            <div className="flex-1 min-w-0 text-left">
              <div className="text-sm font-semibold text-ink">{partner.name}</div>
              <div className="text-xs text-ink-muted">{partner.handle}</div>
              <div className="flex items-center gap-1.5 mt-0.5">
                <span className={`w-1.5 h-1.5 rounded-full ${active ? 'bg-accent-pos' : 'bg-ink-muted'}`} />
                <span className="text-[10px] text-ink-muted">
                  Merge {active ? 'ativo' : 'pausado'} desde {formatDateTime(partner.mergedSince)}
                </span>
              </div>
            </div>
            <div className="px-2 h-6 rounded-full bold-surface text-[10px] font-medium grid place-items-center uppercase tracking-wider">
              {active ? 'Ativo' : 'Pausado'}
            </div>
          </div>
        </div>
      </div>

      <div className="px-5 mt-4">
        <div className="rounded-2xl bg-paper-card border border-paper-line p-4 shadow-soft">
          <div className="text-xs font-semibold text-ink">Vínculo real integrado</div>
          <div className="text-[11px] text-ink-muted mt-1 leading-relaxed">
            Convites, aceite, status e encerramento usam o MergeDuo.Partnership. Os resumos
            financeiros são consolidados em tempo real pelo MergeDuo.Aggregates.
          </div>
        </div>
      </div>

      {error && (
        <div className="px-5">
          <ErrorMessage message={error} />
        </div>
      )}

      <div className="px-5 mt-4">
        <button
          onClick={onLeaveMerge}
          disabled={ending}
          className={`w-full h-10 rounded-full border border-accent-neg/20 bg-accent-neg/10 text-accent-neg text-xs font-medium flex items-center justify-center gap-2 transition hover:bg-accent-neg/20 ${
            ending ? 'opacity-60' : ''
          }`}
        >
          <IconLeaveMerge />
          {ending ? 'Encerrando...' : 'Sair do Merge'}
        </button>
      </div>
    </div>
  );
}

function TopBar({ onBack }: { onBack: () => void }) {
  return (
    <div className="px-5 pt-2 pb-3 flex items-center gap-3">
      <button
        onClick={onBack}
        className="w-9 h-9 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line transition"
        aria-label="Voltar"
      >
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
      </button>
      <div className="text-sm font-semibold tracking-tight text-ink">Merge</div>
    </div>
  );
}

function CenteredState({ label, compact = false }: { label: string; compact?: boolean }) {
  return (
    <div className={`flex flex-col items-center justify-center gap-4 ${compact ? 'py-6' : 'min-h-[18rem]'}`}>
      <div className="w-12 h-12 rounded-full border-2 border-ink border-t-transparent animate-spin" />
      <div className="text-sm text-ink-muted">{label}</div>
    </div>
  );
}

function ErrorMessage({ message }: { message: string }) {
  return (
    <div className="mt-4 rounded-xl border border-accent-neg/20 bg-accent-neg/10 px-3 py-2 text-xs text-accent-neg text-left">
      {message}
    </div>
  );
}

function InfoItem({ icon, title, desc }: { icon: ReactNode; title: string; desc: string }) {
  return (
    <div className="flex items-start gap-3">
      <div className="w-8 h-8 rounded-full bg-paper-line grid place-items-center text-ink-muted shrink-0">
        {icon}
      </div>
      <div className="min-w-0 text-left">
        <div className="text-xs font-semibold text-ink">{title}</div>
        <div className="text-[11px] text-ink-muted mt-0.5 leading-relaxed">{desc}</div>
      </div>
    </div>
  );
}

function MockQRCode({ seed = 'mergeduo', size = 168 }: { seed?: string; size?: number }) {
  const cells = 21;
  const cellSize = size / cells;

  let h = 5381;
  for (let i = 0; i < seed.length; i++) h = ((h * 33) ^ seed.charCodeAt(i)) >>> 0;

  function isBlack(r: number, c: number): boolean {
    if (r < 7 && c < 7) return finder(r, c);
    if (r < 7 && c >= 14) return finder(r, c - 14);
    if (r >= 14 && c < 7) return finder(r - 14, c);
    if (r === 7 && (c < 8 || c > 12)) return false;
    if (c === 7 && (r < 8 || r > 12)) return false;
    if (r < 8 && c === 13) return false;
    if (r === 13 && c < 8) return false;
    if (r === 6 && c > 7 && c < 13) return c % 2 === 0;
    if (c === 6 && r > 7 && r < 13) return r % 2 === 0;
    if (r >= 16 && r <= 18 && c >= 16 && c <= 18) {
      return r === 16 || r === 18 || c === 16 || c === 18 || (r === 17 && c === 17);
    }
    const idx = r * cells + c;
    const val = (h ^ (idx * 2654435761)) >>> 0;
    return (val & 0x80000000) !== 0;
  }

  const rects: ReactNode[] = [];
  for (let r = 0; r < cells; r++) {
    for (let c = 0; c < cells; c++) {
      if (isBlack(r, c)) {
        rects.push(
          <rect
            key={`${r}-${c}`}
            x={c * cellSize}
            y={r * cellSize}
            width={cellSize - 0.3}
            height={cellSize - 0.3}
            fill="#1a1a1a"
          />,
        );
      }
    }
  }

  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} xmlns="http://www.w3.org/2000/svg">
      <rect width={size} height={size} fill="white" />
      {rects}
    </svg>
  );
}

function finder(r: number, c: number) {
  if (r === 0 || r === 6 || c === 0 || c === 6) return true;
  return r >= 2 && r <= 4 && c >= 2 && c <= 4;
}

function viewForPartnership(partnership: MergePartnerInfo | null): MergeViewState {
  if (!partnership) return 'none';
  if (partnership.status === 'paused') return 'paused';
  if (partnership.status === 'active') return 'active';
  return 'none';
}

function formatDateTime(value: string | null | undefined) {
  if (!value) return '-';

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;

  return new Intl.DateTimeFormat('pt-BR', {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(date);
}

function toPartnershipMessage(err: unknown) {
  if (err instanceof PartnershipApiError) {
    if (err.code === 'invite_not_found') return 'Convite não encontrado.';
    if (err.code === 'invalid_invite_token') return 'Token de convite inválido.';
    if (err.code === 'invite_expired') return 'Este convite expirou.';
    if (err.code === 'invite_revoked') return 'Este convite foi revogado.';
    if (err.code === 'invite_already_accepted') return 'Este convite já foi aceito.';
    if (err.code === 'self_invite_not_allowed') return 'Você não pode aceitar o próprio convite.';
    if (err.code === 'partnership_already_exists') return 'Você já possui um Merge ativo ou pausado.';
    if (err.code === 'partnership_not_found') return 'Merge não encontrado.';
    if (err.code === 'partnership_mirror_missing') return 'Não foi possível sincronizar o outro lado do Merge.';
    if (err.code === 'rate_limited') return 'Aguarde um pouco antes de tentar novamente.';
    if (err.code === 'unauthorized') return 'Sua sessão expirou. Entre novamente.';
    return err.message;
  }

  return 'Não foi possível concluir a ação do Merge.';
}

const IS = { width: 14, height: 14, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const };
function IconPeople() { return <svg {...IS}><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>; }
function IconEye() { return <svg {...IS}><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>; }
function IconShield() { return <svg {...IS}><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>; }
function IconLeaveMerge() { return <svg {...IS}><path d="M7 7l10 10"/><path d="M17 7L7 17"/><circle cx="12" cy="12" r="9"/></svg>; }
function IconShare() { return <svg {...IS}><circle cx="18" cy="5" r="3"/><circle cx="6" cy="12" r="3"/><circle cx="18" cy="19" r="3"/><line x1="8.59" y1="13.51" x2="15.42" y2="17.49"/><line x1="15.41" y1="6.51" x2="8.59" y2="10.49"/></svg>; }
