import { useEffect, useMemo, useState } from 'react';
import {
  CardsApiError,
  createCard,
  getCardUsage,
  type CardUsageResponse,
} from '../api/cards';
import {
  TransactionsApiError,
  createTransaction,
  deleteTransaction,
  getTransaction,
  listTransactions,
  toTransaction,
} from '../api/transactions';
import { useFinance } from '../store';
import { useRefresh } from '../refreshContext';
import { useOfflineTransactions } from '../offlineTransactionsContext';
import type { Card, Transaction } from '../types';
import { effectiveCashDate, synthesizePurchaseDateForInvoice } from '../cardInvoice';
import { formatBRL, monthLabel } from '../utils';
import { isOwnerAllowed } from '../useMonthData';
import { CategoryIcon } from './CategoryIcon';
import { NewTransactionSheet } from './NewTransactionSheet';

export function CardInvoiceView({
  accessToken,
  cardId,
  onBack,
}: {
  accessToken: string;
  cardId: string;
  onBack: () => void;
}) {
  const {
    cards,
    cardsStatus,
    cardsError,
    transactions,
    partner,
    mergeActive,
    ownerFilter,
    currentUser,
    upsertTransactions,
    removeTransactionLocal,
    addCard,
    knownTags,
    mergeKnownTags,
  } = useFinance();
  const refreshCtx = useRefresh();
  const offlineTransactions = useOfflineTransactions();
  const card = cards.find((c) => c.id === cardId);
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [monthIdx, setMonthIdx] = useState(now.getMonth());
  const [usage, setUsage] = useState<CardUsageResponse | null>(null);
  const [usageStatus, setUsageStatus] = useState<'idle' | 'loading' | 'ready' | 'fallback'>('idle');
  const [usageError, setUsageError] = useState<string | null>(null);
  const [itemsStatus, setItemsStatus] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle');
  const [itemsError, setItemsError] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [newSheetOpen, setNewSheetOpen] = useState(false);
  const [newError, setNewError] = useState<string | null>(null);
  const [tagFilter, setTagFilter] = useState<string | null>(null);
  const yearMonth = `${year}-${String(monthIdx + 1).padStart(2, '0')}`;

  const invoiceTxs = useMemo(() => {
    if (!card) return [] as { tx: Transaction; dueDate: string }[];
    return transactions
      .filter((t) =>
        t.category === 'credit_card' &&
        t.cardId === cardId &&
        isOwnerAllowed(t, ownerFilter, mergeActive, partner, currentUser),
      )
      .map((t) => ({ tx: t, dueDate: effectiveCashDate(t, cards) }))
      .filter(({ dueDate }) => {
        const d = new Date(`${dueDate}T00:00`);
        return d.getFullYear() === year && d.getMonth() === monthIdx;
      })
      .sort((a, b) => (a.tx.purchaseDate ?? a.tx.date).localeCompare(b.tx.purchaseDate ?? b.tx.date));
  }, [card, cards, transactions, cardId, year, monthIdx, ownerFilter, mergeActive, partner, currentUser]);

  const invoiceTotal = invoiceTxs.reduce((a, { tx }) => a + tx.amount, 0);
  const displayedTotal =
    usageStatus === 'ready' && usage?.yearMonth === yearMonth
      ? usage.totalAmount
      : invoiceTotal;

  const availableTags = useMemo(() => {
    const set = new Set<string>();
    for (const { tx } of invoiceTxs) {
      for (const tag of tx.tags ?? []) set.add(tag);
    }
    return Array.from(set).sort((a, b) => a.localeCompare(b, 'pt-BR'));
  }, [invoiceTxs]);

  const activeTagFilter = tagFilter && availableTags.includes(tagFilter) ? tagFilter : null;

  const filteredInvoiceTxs = useMemo(() => {
    if (!activeTagFilter) return invoiceTxs;
    return invoiceTxs.filter(({ tx }) => (tx.tags ?? []).includes(activeTagFilter));
  }, [invoiceTxs, activeTagFilter]);

  const filteredTotal = filteredInvoiceTxs.reduce((a, { tx }) => a + tx.amount, 0);

  useEffect(() => {
    if (!card) return;

    let cancelled = false;
    const timeout = window.setTimeout(() => {
      if (cancelled) return;

      setUsageStatus('loading');
      setUsageError(null);

      void getCardUsage(accessToken, cardId, yearMonth)
        .then((response) => {
          if (!cancelled) {
            setUsage(response);
            setUsageStatus('ready');
          }
        })
        .catch((err) => {
          if (!cancelled) {
            setUsage(null);
            setUsageStatus('fallback');
            setUsageError(usageErrorMessage(err));
          }
        });
    }, 0);

    return () => {
      cancelled = true;
      window.clearTimeout(timeout);
    };
  }, [accessToken, card, cardId, yearMonth]);

  useEffect(() => {
    if (!card || !currentUser) return;

    let cancelled = false;
    const timeout = window.setTimeout(() => {
      if (cancelled) return;
      setItemsStatus('loading');
      setItemsError(null);

      void (async () => {
        const all: Transaction[] = [];
        let continuationToken: string | null = null;
        do {
          const response = await listTransactions(accessToken, {
            ym: yearMonth,
            cardId,
            owner: ownerFilter,
            pageSize: 100,
            continuationToken,
            sort: 'dateAsc',
          });
          all.push(...response.items.map((item) => toTransaction(item, {
            currentUserId: currentUser.id,
            partnerUserId: partner?.partnerUserId,
            partnerName: partner?.name,
          })));
          continuationToken = response.continuationToken;
        } while (continuationToken);

        if (!cancelled) {
          upsertTransactions(all);
          setItemsStatus('ready');
        }
      })().catch((err) => {
        if (!cancelled) {
          setItemsStatus('error');
          setItemsError(transactionsErrorMessage(err));
        }
      });
    }, 0);

    return () => {
      cancelled = true;
      window.clearTimeout(timeout);
    };
  }, [
    accessToken,
    card,
    cardId,
    currentUser,
    ownerFilter,
    partner?.partnerUserId,
    partner?.name,
    upsertTransactions,
    yearMonth,
  ]);

  if (!card) {
    return (
      <div className="p-5">
        <button onClick={onBack} className="text-sm text-ink-muted">
          Voltar
        </button>
        <div className="mt-6 text-sm text-ink-muted">Cartão não encontrado.</div>
      </div>
    );
  }

  function prev() {
    if (monthIdx === 0) { setMonthIdx(11); setYear(year - 1); }
    else setMonthIdx(monthIdx - 1);
  }
  function next() {
    if (monthIdx === 11) { setMonthIdx(0); setYear(year + 1); }
    else setMonthIdx(monthIdx + 1);
  }

  async function remove(tx: Transaction) {
    if (deletingId || tx.fixedRuleId) return;
    if (tx.localOnly) {
      offlineTransactions?.discardLocalTransaction(tx);
      return;
    }
    if (tx.userId && tx.userId !== currentUser?.id) return;

    const ym = tx.yearMonth ?? tx.date.slice(0, 7);
    setDeletingId(tx.id);
    setItemsError(null);
    try {
      const latest = await transactionForMutation(tx);
      await deleteTransaction(accessToken, latest.id, ym, latest.etag);
      removeTransactionLocal({ id: tx.id, userId: tx.userId, yearMonth: ym });
      refreshCtx?.refreshAll();
    } catch (err) {
      if (err instanceof TransactionsApiError && err.code === 'transaction_not_found') {
        removeTransactionLocal({ id: tx.id, userId: tx.userId, yearMonth: ym });
        refreshCtx?.refreshAll();
        return;
      }
      setItemsError(transactionsErrorMessage(err));
    } finally {
      setDeletingId(null);
    }
  }

  async function transactionForMutation(tx: Transaction): Promise<Transaction> {
    if (tx.etag?.trim()) {
      return tx;
    }

    if (!currentUser) {
      throw new Error('Sua sessão ainda não foi carregada.');
    }

    const ym = tx.yearMonth ?? tx.date.slice(0, 7);
    const fresh = await getTransaction(accessToken, tx.id, ym);
    const mapped = toTransaction(fresh, {
      currentUserId: currentUser.id,
      partnerUserId: partner?.partnerUserId,
      partnerName: partner?.name,
    });
    upsertTransactions([mapped]);
    return mapped;
  }

  async function submitNew(data: {
    date: string;
    category: Transaction['category'];
    description: string;
    amount: number;
    cardId?: string;
    installments?: number;
    invoiceYearMonth?: string;
    tags?: string[];
    notes?: string | null;
  }) {
    if (!currentUser) {
      throw new Error('Sua sessão ainda não foi carregada.');
    }
    setNewError(null);

    const isCreditCard = data.category === 'credit_card';
    if (isCreditCard && !data.cardId) {
      throw new Error('Selecione um cartão para continuar.');
    }

    let purchaseDate = data.date;
    if (isCreditCard && data.cardId && data.invoiceYearMonth) {
      const c = cards.find((x) => x.id === data.cardId);
      if (c) purchaseDate = synthesizePurchaseDateForInvoice(data.invoiceYearMonth, c);
    }

    try {
      const request = isCreditCard
        ? {
            category: data.category,
            purchaseDate,
            description: data.description,
            amount: data.amount,
            currency: 'BRL' as const,
            cardId: data.cardId,
            installments: { total: data.installments ?? 1 },
            tags: data.tags ?? [],
            notes: data.notes ?? null,
          }
        : {
            category: data.category,
            date: data.date,
            description: data.description,
            amount: data.amount,
            currency: 'BRL' as const,
            tags: data.tags ?? [],
            notes: data.notes ?? null,
          };

      if (offlineTransactions) {
        await offlineTransactions.createTransactionWithOfflineQueue({ request, card });
        mergeKnownTags(data.tags ?? []);
        return;
      }

      const response = await createTransaction(accessToken, request);

      upsertTransactions(response.items.map((item) => toTransaction(item, {
        currentUserId: currentUser.id,
        partnerUserId: partner?.partnerUserId,
        partnerName: partner?.name,
      })));
      mergeKnownTags(data.tags ?? []);
      refreshCtx?.refreshAll();
    } catch (err) {
      throw new Error(transactionsErrorMessage(err));
    }
  }

  async function createQuickCard(data: {
    title: string;
    closingDay: number;
    dueDay: number;
  }): Promise<Card> {
    try {
      const created = await createCard(accessToken, {
        ...data,
        currency: 'BRL',
      });
      addCard(created);
      refreshCtx?.refreshAll();
      return created;
    } catch (err) {
      throw new Error(cardsErrorMessage(err));
    }
  }

  return (
    <div className="pb-bottom-nav">
      <div className="mx-auto flex w-full max-w-3xl items-center gap-3 px-4 pb-3 pt-2 sm:px-5 md:px-8 lg:px-10">
        <button
          onClick={onBack}
          className="w-9 h-9 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line transition"
          aria-label="Voltar"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
        </button>
        <div className="text-sm font-semibold tracking-tight text-ink">{card.title}</div>
      </div>

      <div className="mx-auto max-w-3xl px-4 sm:px-5 md:px-8 lg:px-10">
        <div className="flex items-center justify-between mb-3">
          <button onClick={prev} className="w-9 h-9 rounded-full grid place-items-center hover:bg-paper-line">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
          </button>
          <div className="text-sm font-semibold text-ink capitalize">
            Fatura - {monthLabel(year, monthIdx)}
          </div>
          <button onClick={next} className="w-9 h-9 rounded-full grid place-items-center hover:bg-paper-line">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="9 18 15 12 9 6"/></svg>
          </button>
        </div>

        <div className="rounded-2xl bg-paper-card border border-paper-line p-5 shadow-soft">
          <div className="flex items-center justify-between">
            <div className="text-[10px] uppercase tracking-wider text-ink-muted">Total da fatura</div>
            <FreshnessBadge status={usageStatus} synced={usageStatus === 'ready' && usage?.yearMonth === yearMonth} />
          </div>
          <div className="mt-1 text-3xl font-semibold text-ink tabular-nums">
            {formatBRL(displayedTotal)}
          </div>
          <div className="mt-1 text-[11px] text-ink-muted">
            {usageStatus === 'ready' && usage ? (
              <>
                Fecha {formatShortDate(usage.billingCycle.closingDate)} - Vence {formatShortDate(usage.billingCycle.dueDate)}
              </>
            ) : (
              <>
                Vence dia {String(card.dueDay).padStart(2, '0')} - Fecha dia {String(card.closingDay).padStart(2, '0')}
              </>
            )}
          </div>
          {usageStatus === 'loading' && (
            <div className="mt-2 text-[11px] text-ink-muted">
              Sincronizando total da fatura...
            </div>
          )}
          {usageStatus === 'fallback' && usageError && (
            <div className="mt-2 rounded-xl border border-paper-line bg-paper px-3 py-2 text-[11px] text-ink-muted">
              {usageError} Exibindo cálculo local.
            </div>
          )}
        </div>

        <div className="mt-4">
          <div className="flex items-center justify-between mb-2 px-1">
            <div className="text-[10px] uppercase tracking-wider text-ink-muted">
              Lançamentos
            </div>
            <div className="flex items-center gap-3">
              <div className="text-[10px] text-ink-muted">{filteredInvoiceTxs.length}</div>
              <button
                onClick={() => setNewSheetOpen(true)}
                className="inline-flex items-center gap-1 h-7 px-2.5 rounded-full bold-surface text-[11px] font-medium active:scale-[0.97] transition"
              >
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                Novo lançamento
              </button>
            </div>
          </div>

          {availableTags.length > 0 && (
            <div className="mb-3 flex flex-wrap items-center gap-1.5">
              <button
                onClick={() => setTagFilter(null)}
                aria-pressed={activeTagFilter === null}
                className={`inline-flex h-7 items-center rounded-full px-3 text-[11px] font-medium transition tap-surface ${
                  activeTagFilter === null
                    ? 'bg-accent-invest text-white shadow-soft'
                    : 'border border-paper-line bg-paper-card text-ink-muted hover:text-ink'
                }`}
              >
                Todas
              </button>
              {availableTags.map((tag) => (
                <button
                  key={tag}
                  onClick={() => setTagFilter(tag)}
                  aria-pressed={activeTagFilter === tag}
                  className={`inline-flex h-7 items-center rounded-full px-3 text-[11px] font-medium transition tap-surface ${
                    activeTagFilter === tag
                      ? 'bg-accent-invest text-white shadow-soft'
                      : 'border border-paper-line bg-paper-card text-ink-muted hover:text-ink'
                  }`}
                >
                  {tag}
                </button>
              ))}
            </div>
          )}

          {activeTagFilter && (
            <div className="mb-3 flex items-center justify-between rounded-xl border border-accent-invest/20 bg-accent-invest/[0.04] px-3 py-2 text-[12px]">
              <span className="text-ink-muted">
                Filtrando por <span className="font-medium text-accent-invest">{activeTagFilter}</span>
              </span>
              <span className="font-semibold text-ink tabular-nums">{formatBRL(filteredTotal)}</span>
            </div>
          )}
          {newError && (
            <div className="mb-3 rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">
              {newError}
            </div>
          )}

          {itemsStatus === 'loading' && invoiceTxs.length === 0 && (
            <div className="mb-3 rounded-xl border border-paper-line bg-paper-card px-3 py-2 text-[12px] text-ink-muted">
              Carregando lançamentos da fatura...
            </div>
          )}
          {itemsError && (
            <div className="mb-3 rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">
              {itemsError}
            </div>
          )}

          {invoiceTxs.length === 0 ? (
            <div className="rounded-2xl bg-paper-card border border-paper-line p-5 text-center text-sm text-ink-muted shadow-soft">
              Nenhum lançamento nesta fatura.
            </div>
          ) : filteredInvoiceTxs.length === 0 ? (
            <div className="rounded-2xl bg-paper-card border border-paper-line p-5 text-center text-sm text-ink-muted shadow-soft">
              Nenhum lançamento com a tag "{activeTagFilter}".
            </div>
          ) : (
            <div className="rounded-2xl bg-paper-card border border-paper-line shadow-soft divide-y divide-paper-line">
              {filteredInvoiceTxs.map(({ tx }) => {
                const purchaseISO = tx.purchaseDate ?? tx.date;
                const pd = new Date(`${purchaseISO}T00:00`);
                const dayMonth = `${String(pd.getDate()).padStart(2, '0')}/${String(pd.getMonth() + 1).padStart(2, '0')}`;
                const isMine = tx.userId ? tx.userId === currentUser?.id : true;
                const canDelete = isMine && !tx.fixedRuleId;
                return (
                  <div key={`${tx.userId ?? 'local'}:${tx.id}:${tx.yearMonth ?? tx.date}`} className="px-4 py-3 flex items-center gap-3">
                    <div className="w-9 h-9 rounded-full bg-paper-line grid place-items-center shrink-0 text-accent-neg">
                      <CategoryIcon category="credit_card" size={16} />
                    </div>
                    <div className="flex-1 min-w-0 text-left">
                      <div className="text-sm text-ink truncate">{tx.description}</div>
                      <div className="flex items-center gap-1.5 mt-0.5 text-[11px] text-ink-muted">
                        <span>Compra {dayMonth}</span>
                        {tx.installments && (
                          <>
                            <span className="text-paper-line">.</span>
                            <span>{tx.installments.index}/{tx.installments.total}</span>
                          </>
                        )}
                        {tx.owner && (
                          <>
                            <span className="text-paper-line">.</span>
                            <span className={isMine ? '' : 'text-accent-invest font-medium'}>
                              {isMine ? 'Você' : tx.owner.split(' ')[0]}
                            </span>
                          </>
                        )}
                        {tx.pendingSync && (
                          <>
                            <span className="text-paper-line">.</span>
                            <span className="inline-flex items-center h-4 rounded-full border border-amber-300 bg-amber-50 px-1.5 text-[10px] font-medium text-amber-800">
                              Na fila
                            </span>
                          </>
                        )}
                        {!tx.pendingSync && tx.syncError && (
                          <>
                            <span className="text-paper-line">.</span>
                            <span className="inline-flex items-center h-4 rounded-full border border-accent-neg/20 bg-accent-neg/10 px-1.5 text-[10px] font-medium text-accent-neg">
                              Falha ao enviar
                            </span>
                          </>
                        )}
                      </div>
                    </div>
                    <div className="text-sm font-medium text-accent-neg tabular-nums">
                      - {formatBRL(tx.amount)}
                    </div>
                    {canDelete && (
                      <button
                        onClick={() => void remove(tx)}
                        disabled={deletingId === tx.id}
                        aria-label="Remover"
                        className="text-ink-muted hover:text-accent-neg p-1 disabled:opacity-40"
                      >
                        {deletingId === tx.id ? (
                          <span className="text-[10px]">...</span>
                        ) : (
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                        )}
                      </button>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      <NewTransactionSheet
        open={newSheetOpen}
        date={new Date().toISOString().slice(0, 10)}
        cards={cards}
        cardsStatus={cardsStatus}
        cardsError={cardsError}
        onClose={() => setNewSheetOpen(false)}
        onNavigateToCards={() => setNewSheetOpen(false)}
        onCreateCard={createQuickCard}
        onSubmit={submitNew}
        initialCategory="credit_card"
        initialCardId={cardId}
        initialInvoiceYearMonth={yearMonth}
        tagSuggestions={knownTags}
        title={`Novo lançamento · ${card.title}`}
      />
    </div>
  );
}

function FreshnessBadge({
  status,
  synced,
}: {
  status: 'idle' | 'loading' | 'ready' | 'fallback';
  synced: boolean;
}) {
  if (status === 'loading') {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-paper-line px-2 py-0.5 text-[10px] font-medium text-ink-muted">
        <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-ink-muted" />
        Sincronizando
      </span>
    );
  }
  if (status === 'fallback') {
    return (
      <span className="inline-flex items-center gap-1 rounded-full border border-amber-300 bg-amber-50 px-2 py-0.5 text-[10px] font-medium text-amber-800">
        <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round"><path d="M12 9v4"/><path d="M12 17h.01"/><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/></svg>
        Cálculo local
      </span>
    );
  }
  if (synced) {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-accent-pos/12 px-2 py-0.5 text-[10px] font-medium text-accent-pos">
        <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
        Sincronizado
      </span>
    );
  }
  return null;
}

function usageErrorMessage(err: unknown) {
  if (err instanceof CardsApiError) {
    if (err.code === 'cards_dependency_unavailable') {
      return 'Não foi possível sincronizar com o serviço de fatura.';
    }
    if (err.code === 'rate_limited') return 'Muitas consultas de fatura em sequência.';
    if (err.code === 'unauthorized') return 'Sua sessão expirou.';
    return err.message || 'Não foi possível sincronizar a fatura.';
  }

  return err instanceof Error ? err.message : 'Não foi possível sincronizar a fatura.';
}

function cardsErrorMessage(err: unknown) {
  if (err instanceof CardsApiError) {
    if (err.code === 'invalid_title') return 'Informe um título válido para o cartão.';
    if (err.code === 'invalid_billing_day') return 'Informe dias entre 1 e 31.';
    if (err.code === 'unsupported_currency') return 'Apenas BRL está disponível no momento.';
    if (err.code === 'card_conflict' || err.code === 'precondition_failed') {
      return 'Este cartão mudou em outro dispositivo. Atualize e tente novamente.';
    }
    if (err.code === 'unauthorized') return 'Sua sessão expirou. Entre novamente.';
    if (err.code === 'rate_limited') return 'Muitas tentativas. Aguarde um pouco e tente de novo.';
    if (err.code === 'cards_dependency_unavailable') return 'Não foi possível acessar o serviço de cartões agora.';
    return err.message || 'Não foi possível concluir a operação.';
  }

  return err instanceof Error ? err.message : 'Não foi possível concluir a operação.';
}

function transactionsErrorMessage(err: unknown) {
  if (err instanceof TransactionsApiError) {
    if (err.code === 'unauthorized') return 'Sua sessão expirou. Entre novamente.';
    if (err.code === 'rate_limited') return 'Muitas consultas em sequência.';
    if (err.code === 'transactions_dependency_unavailable') {
      return 'Não foi possível carregar os lançamentos da fatura.';
    }
    if (err.code === 'transaction_not_found') return 'Lançamento não encontrado.';
    return err.message || 'Não foi possível carregar os lançamentos.';
  }

  return err instanceof Error ? err.message : 'Não foi possível carregar os lançamentos.';
}

function formatShortDate(iso: string) {
  const [year, month, day] = iso.split('-').map(Number);
  if (!year || !month || !day) return iso;
  return `${String(day).padStart(2, '0')}/${String(month).padStart(2, '0')}`;
}
