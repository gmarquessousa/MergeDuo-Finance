import type { Transaction } from '../types';
import { TransactionItem } from './TransactionItem';
import { useFinance } from '../store';

interface Props {
  transactions: Transaction[];
  onNew: () => void;
  onRemove: (tx: Transaction) => void;
  onEdit?: (tx: Transaction) => void;
  onOpen?: (tx: Transaction) => void;
  removingId?: string | null;
}

export function DayDetails({ transactions, onNew, onRemove, onEdit, onOpen, removingId }: Props) {
  const { cards, currentUser, partner } = useFinance();

  const myTxs = transactions.filter((t) =>
    t.userId ? t.userId === currentUser?.id : !t.owner || t.owner === currentUser?.name,
  );
  const duoTxs = transactions.filter((t) =>
    t.userId ? t.userId !== currentUser?.id : !!(t.owner && t.owner !== currentUser?.name),
  );
  const showGroups = myTxs.length > 0 && duoTxs.length > 0;
  const duoName = partner?.name?.split(' ')[0] ?? 'Duo';

  function resolveCardLabel(t: Transaction): string | undefined {
    if (!t.cardId) return undefined;
    // Prefer the title baked into the transaction (set by backend for partner cards)
    if (t.cardTitle) return t.cardTitle;
    return cards.find((c) => c.id === t.cardId)?.title;
  }

  function renderItem(t: Transaction) {
    return (
      <TransactionItem
        key={t.id}
        tx={t}
        cardLabel={resolveCardLabel(t)}
        onRemove={onRemove}
        onEdit={onEdit}
        onOpen={onOpen}
        removing={removingId === t.id}
      />
    );
  }

  return (
    <div className="px-4 pb-4 bg-paper-card border-b border-paper-line sm:px-5 md:px-8 lg:px-10">
      {transactions.length === 0 ? (
        <div className="py-4 text-sm text-ink-muted text-center">
          Nenhuma movimentação neste dia
        </div>
      ) : showGroups ? (
        <>
          {/* My transactions */}
          <div className="pt-1">
            <div className="px-2 pt-0.5 pb-1 text-[10px] font-semibold uppercase tracking-wider text-ink-muted">
              Meus
            </div>
            <div className="divide-y divide-paper-line">
              {myTxs.map(renderItem)}
            </div>
          </div>
          {/* Partner transactions */}
          <div className="mt-2 pt-2 border-t-2 border-accent-invest/15">
            <div className="px-2 pb-1 flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-wider text-accent-invest">
              <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                <circle cx="12" cy="7" r="4"/>
              </svg>
              {duoName}
            </div>
            <div className="divide-y divide-paper-line">
              {duoTxs.map(renderItem)}
            </div>
          </div>
        </>
      ) : (
        <div className="divide-y divide-paper-line">
          {transactions.map(renderItem)}
        </div>
      )}
      <button
        onClick={onNew}
        className="mt-3 w-full h-11 rounded-xl border border-dashed border-ink/20 text-sm font-medium text-ink-muted hover:text-ink hover:bg-paper hover:border-ink/40 transition flex items-center justify-center gap-2 tap-surface"
      >
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
        Novo lançamento
      </button>
    </div>
  );
}
