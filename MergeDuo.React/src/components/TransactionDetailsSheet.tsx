import { useEffect, useRef } from 'react';
import { useFocusTrap } from '../useFocusTrap';
import { CATEGORY_META, type Transaction } from '../types';
import { useFinance } from '../store';
import { formatBRL } from '../utils';
import { CategoryIcon } from './CategoryIcon';

interface Props {
  open: boolean;
  tx: Transaction | null;
  onClose: () => void;
  onEdit?: (tx: Transaction) => void;
  onRemove?: (tx: Transaction) => void;
  onDeleteGroup?: (tx: Transaction) => void;
  deleting?: boolean;
  deletingGroup?: boolean;
  onCreateFixedFromTransaction?: (tx: Transaction) => void;
}

function formatDate(iso?: string) {
  if (!iso) return null;
  const [year, month, day] = iso.split('-').map(Number);
  if (!year || !month || !day) return iso;
  return new Date(year, month - 1, day).toLocaleDateString('pt-BR', {
    weekday: 'short',
    day: '2-digit',
    month: 'long',
    year: 'numeric',
  });
}

function formatDateTime(iso?: string) {
  if (!iso) return null;
  const dt = new Date(iso);
  if (Number.isNaN(dt.getTime())) return iso;
  return new Intl.DateTimeFormat('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(dt);
}

function transactionStatusLabels(tx: Transaction) {
  const labels: string[] = [];
  if (tx.projected) labels.push('Previsto');
  if (tx.pendingSync) labels.push('Na fila');
  if (!tx.pendingSync && tx.syncError) labels.push('Falha ao enviar');
  if (!tx.projected && tx.fixedRuleId) labels.push('Fixo');
  if (tx.aggregateOnly) labels.push('Aggregate');
  if (tx.localOnly) labels.push('Somente local');
  return labels;
}

function DetailRow({ label, value }: { label: string; value?: string | null }) {
  if (!value) return null;

  return (
    <div className="rounded-2xl border border-paper-line bg-paper px-4 py-3">
      <div className="text-[10px] uppercase tracking-wider text-ink-muted">{label}</div>
      <div className="mt-1 text-sm text-ink">{value}</div>
    </div>
  );
}

export function TransactionDetailsSheet({
  open,
  tx,
  onClose,
  onEdit,
  onRemove,
  onDeleteGroup,
  deleting = false,
  deletingGroup = false,
  onCreateFixedFromTransaction,
}: Props) {
  const { cards, currentUser } = useFinance();
  const panelRef = useRef<HTMLDivElement>(null);
  useFocusTrap(panelRef, open);

  useEffect(() => {
    if (!open) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
    };
  }, [open]);

  if (!open || !tx) return null;

  const meta = CATEGORY_META[tx.category];
  const amountSign = meta.kind === 'in' ? '+' : '-';
  const cardLabel = tx.cardId
    ? (tx.cardTitle
        ?? cards.find((card) => card.id === tx.cardId)?.title
        ?? null)
    : null;
  const ownerLabel = tx.owner
    ? tx.owner === currentUser?.name
      ? 'Você'
      : tx.owner
    : 'Você';
  const statusLabels = transactionStatusLabels(tx);
  const isMine = tx.userId ? tx.userId === currentUser?.id : true;
  const canEdit = !!onEdit && isMine && !tx.projected && !tx.installments && !tx.localOnly && !tx.aggregateOnly;
  const canRemove = !!onRemove && isMine && !tx.fixedRuleId && !tx.projected && !tx.aggregateOnly;
  const canDeleteGroup = !!onDeleteGroup && isMine && !!tx.installments && !tx.fixedRuleId && !tx.projected && !tx.aggregateOnly;
  const canCreateFixed =
    !!onCreateFixedFromTransaction &&
    isMine &&
    !tx.fixedRuleId &&
    !tx.projected &&
    !tx.localOnly &&
    !tx.aggregateOnly &&
    !tx.installments;

  return (
    <div
      className="fixed inset-0 z-50 flex items-end justify-center"
      role="dialog"
      aria-modal="true"
      aria-labelledby="tx-details-title"
    >
      <div className="absolute inset-0 bg-black/50 animate-[fadeIn_150ms_ease]" onClick={onClose} />
      <div ref={panelRef} className="relative flex max-h-[92vh] w-full max-w-md flex-col rounded-t-3xl bg-paper-card shadow-elevated animate-[slideUp_240ms_cubic-bezier(0.22,1,0.36,1)]">
        <div className="shrink-0 px-5 pt-2">
          <div className="mx-auto mb-3 h-1 w-10 rounded-full bg-paper-line" />
          <div className="flex items-start justify-between gap-3 pb-3">
            <div className="min-w-0">
              <div className="text-[11px] uppercase tracking-[0.18em] text-ink-muted">
                Detalhes do lançamento
              </div>
              <h2 id="tx-details-title" className="mt-1 truncate text-base font-semibold text-ink">
                {tx.description}
              </h2>
            </div>
            <button
              type="button"
              onClick={onClose}
              aria-label="Fechar"
              className="grid h-11 w-11 place-items-center rounded-full text-ink-muted transition hover:bg-paper-line"
            >
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
            </button>
          </div>
        </div>

        <div className="space-y-4 overflow-y-auto px-5 pb-6">
          <div className="rounded-3xl border border-paper-line bg-paper px-4 py-4">
            <div className="flex items-start gap-3">
              <div className={`grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-paper-line ${meta.color}`}>
                <CategoryIcon category={tx.category} size={18} strokeWidth={2} />
              </div>
              <div className="min-w-0 flex-1">
                <div className="text-[11px] uppercase tracking-wider text-ink-muted">
                  {meta.label}
                </div>
                <div className={`mt-1 text-2xl font-semibold tabular-nums ${meta.color}`}>
                  {amountSign} {formatBRL(tx.amount)}
                </div>
                {statusLabels.length > 0 && (
                  <div className="mt-3 flex flex-wrap gap-1.5">
                    {statusLabels.map((label) => (
                      <span
                        key={label}
                        className="inline-flex items-center rounded-full bg-paper-line px-2 py-1 text-[10px] font-medium text-ink-muted"
                      >
                        {label}
                      </span>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </div>

          <div className="grid gap-3">
            <DetailRow
              label={tx.category === 'credit_card' ? 'Data de impacto' : 'Data'}
              value={formatDate(tx.date)}
            />
            {tx.purchaseDate && tx.purchaseDate !== tx.date && (
              <DetailRow label="Data da compra" value={formatDate(tx.purchaseDate)} />
            )}
            <DetailRow label="Responsável" value={ownerLabel} />
            <DetailRow label="Cartão" value={cardLabel} />
            <DetailRow
              label="Parcela"
              value={tx.installments ? `${tx.installments.index} de ${tx.installments.total}` : null}
            />
            <DetailRow label="Criado em" value={formatDateTime(tx.createdAt)} />
            <DetailRow label="Atualizado em" value={formatDateTime(tx.updatedAt)} />
          </div>

          {tx.notes && (
            <div className="rounded-2xl border border-paper-line bg-paper px-4 py-3">
              <div className="text-[10px] uppercase tracking-wider text-ink-muted">Observações</div>
              <p className="mt-1 whitespace-pre-wrap text-sm text-ink">{tx.notes}</p>
            </div>
          )}

          {tx.tags && tx.tags.length > 0 && (
            <div className="rounded-2xl border border-paper-line bg-paper px-4 py-3">
              <div className="text-[10px] uppercase tracking-wider text-ink-muted">Tags</div>
              <div className="mt-2 flex flex-wrap gap-1.5">
                {tx.tags.map((tag) => (
                  <span
                    key={tag}
                    className="inline-flex items-center rounded-full bg-paper-line px-2 py-1 text-[10px] font-medium text-ink-muted"
                  >
                    {tag}
                  </span>
                ))}
              </div>
            </div>
          )}

          {(canEdit || canCreateFixed || canDeleteGroup || canRemove) && (
            <div className="sticky bottom-0 -mx-5 border-t border-paper-line bg-paper-card/95 px-5 pb-1 pt-3 backdrop-blur">
              <div className="grid gap-2">
                {canEdit && (
                  <button
                    type="button"
                    onClick={() => onEdit?.(tx)}
                    className="h-11 rounded-xl bold-surface text-sm font-medium"
                  >
                    Editar lançamento
                  </button>
                )}
                {canCreateFixed && (
                  <button
                    type="button"
                    onClick={() => {
                      onCreateFixedFromTransaction?.(tx);
                      onClose();
                    }}
                    className="h-11 rounded-xl border border-paper-line text-sm font-medium text-ink hover:bg-paper-line transition"
                  >
                    Criar fixo a partir deste lançamento
                  </button>
                )}
                {canDeleteGroup && (
                  <button
                    type="button"
                    onClick={() => onDeleteGroup?.(tx)}
                    disabled={deletingGroup}
                    className="h-11 rounded-xl border border-accent-neg/30 bg-accent-neg/5 text-sm font-medium text-accent-neg disabled:opacity-50"
                  >
                    {deletingGroup ? 'Excluindo compra...' : `Excluir compra parcelada (${tx.installments?.total} parcelas)`}
                  </button>
                )}
                {canRemove && (
                  <button
                    type="button"
                    onClick={() => onRemove?.(tx)}
                    disabled={deleting}
                    className="h-11 rounded-xl border border-accent-neg/30 text-sm font-medium text-accent-neg hover:bg-accent-neg/8 disabled:opacity-50 transition"
                  >
                    {deleting ? 'Excluindo...' : 'Excluir lançamento'}
                  </button>
                )}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
