import { useEffect, useMemo, useState } from 'react';
import {
  getTransactionTags,
  toTransaction,
  type TagSummaryResponse,
} from '../api/transactions';
import { useFinance } from '../store';
import type { Transaction } from '../types';
import { formatBRL } from '../utils';
import { TransactionItem } from './TransactionItem';

interface Props {
  accessToken: string;
  onBack: () => void;
}

type LoadStatus = 'loading' | 'ready' | 'error';
type SortMode = 'total' | 'count' | 'alpha';

export function TagsView({ accessToken, onBack }: Props) {
  const {
    currentUser,
    partner,
    cards,
    setKnownTags,
  } = useFinance();
  const [items, setItems] = useState<TagSummaryResponse[]>([]);
  const [status, setStatus] = useState<LoadStatus>('loading');
  const [error, setError] = useState<string | null>(null);
  const [expandedTag, setExpandedTag] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [sortMode, setSortMode] = useState<SortMode>('total');

  useEffect(() => {
    const controller = new AbortController();

    void getTransactionTags(accessToken, true, { signal: controller.signal })
      .then((response) => {
        setError(null);
        setItems(response.items);
        setKnownTags(response.tags);
        setStatus('ready');
      })
      .catch((err) => {
        if (isAbortError(err)) return;
        setError(err instanceof Error ? err.message : 'Não foi possível carregar as tags.');
        setStatus('error');
      });

    return () => controller.abort();
  }, [accessToken, setKnownTags]);

  const totals = useMemo(() => ({
    expenses: items.reduce((sum, item) => sum + item.expensesTotal, 0),
    transactions: items.reduce((sum, item) => sum + item.transactionCount, 0),
  }), [items]);

  const visibleItems = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    const filtered = normalizedQuery
      ? items.filter((item) => item.tag.toLowerCase().includes(normalizedQuery))
      : items;
    const sorted = [...filtered];
    sorted.sort((a, b) => {
      if (sortMode === 'alpha') return a.tag.localeCompare(b.tag, 'pt-BR');
      if (sortMode === 'count') return b.transactionCount - a.transactionCount;
      return b.expensesTotal - a.expensesTotal;
    });
    return sorted;
  }, [items, query, sortMode]);

  function mapTransactions(item: TagSummaryResponse): Transaction[] {
    return (item.transactions ?? []).map((tx) => toTransaction(tx, {
      currentUserId: currentUser?.id,
      partnerUserId: partner?.partnerUserId,
      partnerName: partner?.name,
    }));
  }

  function cardLabel(tx: Transaction): string | undefined {
    if (!tx.cardId) return undefined;
    if (tx.cardTitle) return tx.cardTitle;
    return cards.find((card) => card.id === tx.cardId)?.title;
  }

  return (
    <div className="pb-bottom-nav">
      <div className="mx-auto flex w-full max-w-5xl items-center gap-3 px-4 pb-3 pt-2 sm:px-5 md:px-8 lg:px-10">
        <button
          type="button"
          onClick={onBack}
          className="w-9 h-9 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line transition"
          aria-label="Voltar"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
        </button>
        <div>
          <div className="text-sm font-semibold tracking-tight text-ink">Tags</div>
          <div className="text-[11px] text-ink-muted">
            {items.length} tags - {totals.transactions} lançamentos
          </div>
        </div>
      </div>

      <div className="mx-auto w-full max-w-5xl px-4 sm:px-5 md:px-8 lg:px-10">
        <div className="grid gap-3 sm:grid-cols-3">
          <SummaryTile label="Tags" value={String(items.length)} />
          <SummaryTile label="Gastos por tags" value={formatBRL(totals.expenses)} />
          <SummaryTile label="Lançamentos por tags" value={String(totals.transactions)} />
        </div>

        {status === 'ready' && items.length > 0 && (
          <div className="mt-4 space-y-2.5">
            <div className="relative">
              <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-ink-muted">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
              </span>
              <input
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Buscar tag"
                className="h-11 w-full rounded-2xl border border-paper-line bg-paper-card pl-10 pr-10 text-[15px] text-ink outline-none transition-colors placeholder:text-ink-muted/60 focus:border-accent-invest/60 focus:bg-accent-invest/[0.03]"
              />
              {query && (
                <button
                  type="button"
                  onClick={() => setQuery('')}
                  aria-label="Limpar busca"
                  className="absolute right-2.5 top-1/2 -translate-y-1/2 grid h-7 w-7 place-items-center rounded-full text-ink-muted hover:bg-paper-line transition"
                >
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                </button>
              )}
            </div>
            <div className="inline-flex w-full gap-1 rounded-full bg-paper-card border border-paper-line p-0.5">
              <SortButton label="Maior gasto" active={sortMode === 'total'} onClick={() => setSortMode('total')} />
              <SortButton label="Mais usadas" active={sortMode === 'count'} onClick={() => setSortMode('count')} />
              <SortButton label="A-Z" active={sortMode === 'alpha'} onClick={() => setSortMode('alpha')} />
            </div>
          </div>
        )}

        {status === 'loading' && (
          <div className="mt-4 rounded-2xl border border-paper-line bg-paper-card p-5 text-center text-sm text-ink-muted shadow-soft">
            Carregando tags...
          </div>
        )}

        {status === 'error' && (
          <div className="mt-4 rounded-2xl border border-accent-neg/30 bg-accent-neg/5 p-4 text-sm text-accent-neg">
            {error ?? 'Não foi possível carregar as tags.'}
          </div>
        )}

        {status === 'ready' && items.length === 0 && (
          <div className="mt-4 rounded-2xl border border-paper-line bg-paper-card p-5 text-center text-sm text-ink-muted shadow-soft">
            Nenhuma tag cadastrada.
          </div>
        )}

        {status === 'ready' && items.length > 0 && (
          <div className="mt-3 space-y-2">
            {visibleItems.length === 0 ? (
              <div className="rounded-2xl border border-paper-line bg-paper-card p-5 text-center text-sm text-ink-muted shadow-soft">
                Nenhuma tag encontrada para "{query}".
              </div>
            ) : visibleItems.map((item) => {
              const expanded = expandedTag === item.tag;
              const transactions = mapTransactions(item);
              return (
                <section
                  key={item.tag}
                  className="rounded-2xl border border-paper-line bg-paper-card shadow-soft"
                >
                  <button
                    type="button"
                    onClick={() => setExpandedTag(expanded ? null : item.tag)}
                    aria-expanded={expanded}
                    className="flex w-full items-center gap-3 px-4 py-3 text-left"
                  >
                    <span className="inline-flex min-w-0 max-w-[45%] items-center rounded-full bg-paper-line px-2.5 py-1 text-xs font-medium text-ink">
                      <span className="truncate">{item.tag}</span>
                    </span>
                    <span className="ml-auto text-right">
                      <span className="block text-sm font-semibold text-accent-neg tabular-nums">
                        {formatBRL(item.expensesTotal)}
                      </span>
                      <span className="block text-[11px] text-ink-muted">
                        {item.transactionCount} lançamento{item.transactionCount === 1 ? '' : 's'}
                      </span>
                    </span>
                    <span className={`grid h-7 w-7 place-items-center rounded-full text-ink-muted transition ${expanded ? 'rotate-180 bg-paper-line' : ''}`}>
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><polyline points="6 9 12 15 18 9"/></svg>
                    </span>
                  </button>

                  {expanded && (
                    <div className="border-t border-paper-line px-3 py-2">
                      {transactions.length === 0 ? (
                        <div className="px-2 py-3 text-sm text-ink-muted">
                          Sem lançamentos materializados.
                        </div>
                      ) : (
                        <div className="divide-y divide-paper-line">
                          {transactions.map((tx) => (
                            <TransactionItem
                              key={`${tx.userId ?? 'local'}:${tx.yearMonth ?? tx.date.slice(0, 7)}:${tx.id}`}
                              tx={tx}
                              cardLabel={cardLabel(tx)}
                            />
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                </section>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}

function SummaryTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-paper-line bg-paper-card p-4 shadow-soft">
      <div className="text-[10px] uppercase tracking-wider text-ink-muted">{label}</div>
      <div className="mt-1 text-xl font-semibold text-ink tabular-nums">{value}</div>
    </div>
  );
}

function SortButton({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={`flex-1 rounded-full px-3 py-1.5 text-[12px] font-medium transition-all tap-surface ${
        active ? 'bg-accent-invest text-white shadow-soft' : 'text-ink-muted hover:text-ink'
      }`}
    >
      {label}
    </button>
  );
}

function isAbortError(err: unknown) {
  return (
    (err instanceof DOMException && err.name === 'AbortError') ||
    (err instanceof Error && err.name === 'AbortError')
  );
}
