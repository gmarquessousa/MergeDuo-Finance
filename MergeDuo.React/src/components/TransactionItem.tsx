import { CATEGORY_META, type Transaction } from '../types';
import { CategoryIcon } from './CategoryIcon';
import { formatBRL } from '../utils';
import { useFinance } from '../store';
import { useValuesHidden } from '../valuesVisibilityContext';

function OwnerBadge({ owner, isCurrentUser }: { owner: string; isCurrentUser: boolean }) {
  if (isCurrentUser) return null;
  return (
    <span className="inline-flex items-center gap-1 h-[18px] rounded-full px-1.5 text-[10px] font-semibold bg-accent-invest/12 text-accent-invest border border-accent-invest/25">
      <svg width="8" height="8" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
        <circle cx="12" cy="7" r="4"/>
      </svg>
      Duo · {owner.split(' ')[0]}
    </span>
  );
}

interface Props {
  tx: Transaction;
  cardLabel?: string;
  onRemove?: (tx: Transaction) => void;
  onEdit?: (tx: Transaction) => void;
  onOpen?: (tx: Transaction) => void;
  removing?: boolean;
}

export function TransactionItem({
  tx,
  cardLabel,
  onRemove,
  onEdit,
  onOpen,
  removing = false,
}: Props) {
  const { currentUser } = useFinance();
  const hidden = useValuesHidden();
  const meta = CATEGORY_META[tx.category];
  const sign = meta.kind === 'in' ? '+' : meta.kind === 'out' ? '-' : '*';
  const isMine = tx.userId ? tx.userId === currentUser?.id : true;
  const isCurrentUserOwner = tx.userId
    ? tx.userId === currentUser?.id
    : tx.owner === currentUser?.name;
  const canRemove = onRemove && isMine && !tx.fixedRuleId && !tx.projected && !tx.aggregateOnly && !tx.installments;
  const canEdit = onEdit && isMine && !tx.projected && !tx.installments && !tx.localOnly && !tx.aggregateOnly;
  const isPartner = !isCurrentUserOwner && !!tx.owner;
  const iconBg = isPartner ? 'bg-accent-invest/10' : 'bg-paper-line';

  const content = (
    <>
      <div className={`w-9 h-9 rounded-2xl grid place-items-center shrink-0 ${iconBg} ${meta.color}`}>
        <CategoryIcon category={tx.category} size={15} strokeWidth={2.2} />
      </div>
      <div className="flex-1 min-w-0 text-left">
        <div className="text-sm font-medium text-ink truncate">{tx.description}</div>
        <div className="flex items-center gap-1.5 mt-0.5 flex-wrap">
          <span className="text-[11px] text-ink-muted">{meta.label}</span>
          {tx.installments && (
            <>
              <span aria-hidden className="w-px h-3 bg-paper-line rounded-full" />
              <span className="inline-flex items-center h-[18px] rounded-full bg-paper-line px-1.5 text-[10px] font-medium text-ink-muted">
                {tx.installments.index}/{tx.installments.total}
              </span>
            </>
          )}
          {tx.owner && (
            <>
              <span aria-hidden className="w-px h-3 bg-paper-line rounded-full" />
              <OwnerBadge owner={tx.owner} isCurrentUser={isCurrentUserOwner} />
            </>
          )}
          {tx.projected && (
            <>
              <span aria-hidden className="w-px h-3 bg-paper-line rounded-full" />
              <span className="inline-flex items-center h-[18px] rounded-full border border-dashed border-ink-muted/40 px-1.5 text-[10px] font-medium text-ink-muted">
                Previsto
              </span>
            </>
          )}
          {tx.pendingSync && (
            <>
              <span aria-hidden className="w-px h-3 bg-paper-line rounded-full" />
              <span className="inline-flex items-center h-[18px] rounded-full border border-amber-300/70 bg-amber-50 px-1.5 text-[10px] font-medium text-amber-700">
                Na fila
              </span>
            </>
          )}
          {!tx.pendingSync && tx.syncError && (
            <>
              <span aria-hidden className="w-px h-3 bg-paper-line rounded-full" />
              <span className="inline-flex items-center h-[18px] rounded-full border border-accent-neg/25 bg-accent-neg/8 px-1.5 text-[10px] font-medium text-accent-neg">
                Falha ao enviar
              </span>
            </>
          )}
          {!tx.projected && tx.fixedRuleId && (
            <>
              <span aria-hidden className="w-px h-3 bg-paper-line rounded-full" />
              <span className="inline-flex items-center h-[18px] rounded-full bg-paper-line px-1.5 text-[10px] font-medium text-ink-muted">
                Fixo
              </span>
            </>
          )}
          {tx.aggregateOnly && (
            <>
              <span aria-hidden className="w-px h-3 bg-paper-line rounded-full" />
              <span className="inline-flex items-center h-[18px] rounded-full bg-accent-invest/10 px-1.5 text-[10px] font-medium text-accent-invest">
                Aggregate
              </span>
            </>
          )}
          {cardLabel && (
            <>
              <span aria-hidden className="w-px h-3 bg-paper-line rounded-full" />
              <span className="inline-flex items-center gap-1 h-[18px] rounded-full bg-accent-neg/8 px-1.5 text-[10px] font-medium text-accent-neg">
                <svg width="9" height="9" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><rect x="2" y="5" width="20" height="14" rx="2"/><line x1="2" y1="10" x2="22" y2="10"/></svg>
                {cardLabel}
              </span>
            </>
          )}
        </div>
        {tx.tags && tx.tags.length > 0 && (
          <div className="flex flex-wrap items-center gap-1 mt-1.5">
            {tx.tags.map((tag) => (
              <span
                key={tag}
                className="inline-flex items-center h-[18px] rounded-full border border-paper-line bg-paper px-1.5 text-[10px] font-medium text-ink-muted"
              >
                #{tag}
              </span>
            ))}
          </div>
        )}
        {tx.notes && (
          <p className="mt-1 text-[11px] text-ink-muted/75 line-clamp-1">{tx.notes}</p>
        )}
      </div>
      <div className={`text-sm font-semibold tabular-nums shrink-0 ${meta.color}`}>
        {hidden ? 'R$ ••••' : `${sign} ${formatBRL(tx.amount)}`}
      </div>
    </>
  );

  return (
    <div className="flex items-center gap-1 py-2">
      {onOpen ? (
        <button
          type="button"
          onClick={() => onOpen(tx)}
          aria-label={`Ver detalhes de ${tx.description}`}
          className={`flex min-w-0 flex-1 items-center gap-3 rounded-2xl px-2 py-2 text-left transition tap-surface focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ink/15 ${
            isPartner ? 'hover:bg-accent-invest/[0.07]' : 'hover:bg-paper-card'
          }`}
        >
          {content}
        </button>
      ) : (
        <div className="flex min-w-0 flex-1 items-center gap-3 px-2 py-2">
          {content}
        </div>
      )}
      {canEdit && (
        <button
          type="button"
          onClick={() => onEdit(tx)}
          aria-label="Editar"
          className="text-ink-muted hover:text-ink p-2 rounded-xl hover:bg-paper-line transition-colors tap-surface shrink-0"
        >
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
        </button>
      )}
      {canRemove && (
        <button
          type="button"
          onClick={() => onRemove(tx)}
          disabled={removing}
          aria-label="Remover"
          className="text-ink-muted hover:text-accent-neg p-2 rounded-xl hover:bg-accent-neg/8 transition-colors tap-surface shrink-0 disabled:opacity-40"
        >
          {removing ? (
            <span className="text-[10px]">...</span>
          ) : (
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          )}
        </button>
      )}
    </div>
  );
}
