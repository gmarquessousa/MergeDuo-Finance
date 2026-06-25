import { lazy, Suspense, useMemo, useState } from 'react';
import { transactionCacheKey, useFinance } from '../store';
import { useOfflineTransactions } from '../offlineTransactionsContext';
import type { Card, Transaction, TransactionCategory } from '../types';
import { isoDate } from '../utils';
import { synthesizePurchaseDateForInvoice } from '../cardInvoice';
import { DayRow } from './DayRow';
import { DayDetails } from './DayDetails';
import { useMonthData } from '../useMonthData';
import { useAggregateMonthSummary } from '../useAggregateSummary';
import { shouldTrustMonthAggregate } from '../aggregateTrust';
import { resolveDailyTotalMode } from '../dailyTotalMode';
import { resolveDailyAuditTrail } from '../dailyAuditTrail';
import { findLargestExpenseDays } from '../dailyMarkers';
import {
  TransactionsApiError,
  createTransaction,
  deleteTransaction,
  deleteTransactionGroup,
  getTransaction,
  getTransactionGroup,
  patchTransaction,
  toTransaction,
  type TransactionResponse,
} from '../api/transactions';
import {
  CardsApiError,
  createCard,
} from '../api/cards';

const NewTransactionSheet = lazy(() => import('./NewTransactionSheet').then((m) => ({ default: m.NewTransactionSheet })));
const EditTransactionSheet = lazy(() => import('./EditTransactionSheet').then((m) => ({ default: m.EditTransactionSheet })));
const TransactionDetailsSheet = lazy(() => import('./TransactionDetailsSheet').then((m) => ({ default: m.TransactionDetailsSheet })));

interface Props {
  accessToken: string;
  year: number;
  monthIdx: number;
  onNavigateToCards: () => void;
  onTransactionMutated?: (yearMonths?: string[]) => void;
  onCreateFixedFromTransaction?: (tx: Transaction) => void;
}

export function DailyList({
  accessToken,
  year,
  monthIdx,
  onNavigateToCards,
  onTransactionMutated,
  onCreateFixedFromTransaction,
}: Props) {
  const {
    currentUser,
    partner,
    ownerFilter,
    transactions,
    transactionLoads,
    upsertTransactions,
    removeTransactionLocal,
    cards,
    cardsStatus,
    cardsError,
    addCard,
    knownTags,
    mergeKnownTags,
  } = useFinance();
  const offlineTransactions = useOfflineTransactions();
  const { perDay, totalAcumulado } = useMonthData(year, monthIdx);
  // Aggregate is already cached from App-level fetches — no extra API call.
  // When available, it provides a per-day absolute balance series.
  const aggregateSummary = useAggregateMonthSummary(year, monthIdx + 1);
  const yearMonth = `${year}-${String(monthIdx + 1).padStart(2, '0')}`;
  const monthLoad = transactionLoads[transactionCacheKey({ yearMonth, owner: ownerFilter })];
  const canUseAggregateCorrection = shouldTrustMonthAggregate(
    aggregateSummary.isStale,
    monthLoad,
    aggregateSummary.locallyInvalidated,
  );
  const totalModeState = useMemo(
    () => resolveDailyTotalMode({
      year,
      monthIdx,
      perDay,
      totalAcumulado,
      aggregateSummary,
      canUseAggregateCorrection,
    }),
    [aggregateSummary, canUseAggregateCorrection, monthIdx, perDay, totalAcumulado, year],
  );
  const totalModeReady = totalModeState.ready;
  const showAggregateAuditTrail = totalModeReady;
  const auditTrail = useMemo(
    () => resolveDailyAuditTrail({
      year,
      monthIdx,
      perDay,
      dailyMovements: aggregateSummary.dailyMovements,
      currentUser,
      partner,
    }),
    [aggregateSummary.dailyMovements, currentUser, monthIdx, partner, perDay, year],
  );

  const [expandedDay, setExpandedDay] = useState<number | null>(
    new Date().getMonth() === monthIdx && new Date().getFullYear() === year
      ? new Date().getDate()
      : null,
  );
  const [sheetDate, setSheetDate] = useState<string | null>(null);
  const [editingTx, setEditingTx] = useState<Transaction | null>(null);
  const [selectedTx, setSelectedTx] = useState<Transaction | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [removingId, setRemovingId] = useState<string | null>(null);
  const [removingGroupId, setRemovingGroupId] = useState<string | null>(null);

  const totalLancamentos = perDay.reduce(
    (sum, row) =>
      sum + (showAggregateAuditTrail
        ? (auditTrail.transactionsByDay.get(row.day)?.length ?? row.txs.length)
        : row.txs.length),
    0,
  );
  const largestExpenseDays = useMemo(
    () =>
      findLargestExpenseDays(
        perDay.map((row) => ({
          day: row.day,
          transactions: showAggregateAuditTrail
            ? auditTrail.transactionsByDay.get(row.day) ?? row.txs
            : row.txs,
        })),
      ),
    [auditTrail.transactionsByDay, perDay, showAggregateAuditTrail],
  );

  async function submitTransaction(data: {
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

    setActionError(null);
    const isCreditCard = data.category === 'credit_card';
    if (isCreditCard && !data.cardId) {
      throw new Error('Selecione um cartão para continuar.');
    }

    // Quando o usuário escolhe uma fatura específica, sintetizamos um
    // purchaseDate que cai dentro da janela daquela fatura.
    let purchaseDate = data.date;
    if (isCreditCard && data.cardId && data.invoiceYearMonth) {
      const card = cards.find((c) => c.id === data.cardId);
      if (card) {
        purchaseDate = synthesizePurchaseDateForInvoice(data.invoiceYearMonth, card);
      }
    }

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
      await offlineTransactions.createTransactionWithOfflineQueue({
        request,
        card: isCreditCard && data.cardId ? cards.find((c) => c.id === data.cardId) ?? null : null,
      }).catch((err: unknown) => {
        throw new Error(transactionsErrorMessage(err));
      });
      mergeKnownTags(data.tags ?? []);
      return;
    }

    const response = await createTransaction(accessToken, request).catch((err: unknown) => {
      throw new Error(transactionsErrorMessage(err));
    });

    upsertTransactions(response.items.map((item) => toTransaction(item, {
      currentUserId: currentUser.id,
      partnerUserId: partner?.partnerUserId,
      partnerName: partner?.name,
    })));
    mergeKnownTags(data.tags ?? []);
    onTransactionMutated?.(yearMonthsFromResponses(response.items));
  }

  async function createQuickCard(data: { title: string; closingDay: number; dueDay: number }): Promise<Card> {
    const created = await createCard(accessToken, {
      title: data.title,
      closingDay: data.closingDay,
      dueDay: data.dueDay,
      currency: 'BRL',
    }).catch((err: unknown) => {
      throw new Error(cardsErrorMessage(err));
    });
    addCard(created);
    return created;
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

  async function removeTransaction(tx: Transaction) {
    if (removingId || tx.fixedRuleId) return;
    if (tx.localOnly) {
      if (selectedTx?.id === tx.id) setSelectedTx(null);
      offlineTransactions?.discardLocalTransaction(tx);
      return;
    }
    if (tx.userId && tx.userId !== currentUser?.id) return;

    const ym = tx.yearMonth ?? tx.date.slice(0, 7);
    setActionError(null);
    setRemovingId(tx.id);
    try {
      const latest = await transactionForMutation(tx);
      await deleteTransaction(accessToken, latest.id, ym, latest.etag);
      if (selectedTx?.id === tx.id) setSelectedTx(null);
      removeTransactionLocal({ id: tx.id, userId: tx.userId, yearMonth: ym });
      onTransactionMutated?.([ym]);
    } catch (err) {
      if (err instanceof TransactionsApiError && err.code === 'transaction_not_found') {
        if (selectedTx?.id === tx.id) setSelectedTx(null);
        removeTransactionLocal({ id: tx.id, userId: tx.userId, yearMonth: ym });
        onTransactionMutated?.([ym]);
        return;
      }

      setActionError(transactionsErrorMessage(err));
    } finally {
      setRemovingId(null);
    }
  }

  async function removeTransactionGroup(tx: Transaction) {
    const groupId = tx.installments?.groupId;
    if (!groupId || removingGroupId) return;
    if (tx.userId && tx.userId !== currentUser?.id) return;

    setActionError(null);
    setRemovingGroupId(groupId);
    try {
      let groupItems: Transaction[] = [];
      try {
        const group = await getTransactionGroup(accessToken, groupId);
        groupItems = group.items.map((item) => toTransaction(item, {
          currentUserId: currentUser?.id,
          partnerUserId: partner?.partnerUserId,
          partnerName: partner?.name,
        }));
      } catch {
        groupItems = transactions.filter((item) => item.installments?.groupId === groupId);
      }

      await deleteTransactionGroup(accessToken, groupId);
      const affectedYearMonths = new Set<string>();
      for (const item of groupItems.length > 0 ? groupItems : [tx]) {
        const ym = item.yearMonth ?? item.date.slice(0, 7);
        affectedYearMonths.add(ym);
        removeTransactionLocal({ id: item.id, userId: item.userId, yearMonth: ym });
      }
      if (selectedTx?.installments?.groupId === groupId) setSelectedTx(null);
      onTransactionMutated?.(Array.from(affectedYearMonths));
    } catch (err) {
      setActionError(transactionsErrorMessage(err));
    } finally {
      setRemovingGroupId(null);
    }
  }

  async function submitEdit(data: {
    description: string;
    amount: number;
    date: string;
    category: TransactionCategory;
    cardId?: string | null;
    tags: string[];
    notes: string | null;
  }) {
    if (!editingTx || !currentUser) return;
    const tx = editingTx;
    const ym = tx.yearMonth ?? tx.date.slice(0, 7);
    const isCreditCard = data.category === 'credit_card';
    try {
      const latest = await transactionForMutation(tx);
      const updated = await patchTransaction(
        accessToken,
        latest.id,
        ym,
        isCreditCard
          ? {
              description: data.description,
              amount: data.amount,
              category: data.category,
              purchaseDate: data.date,
              cardId: data.cardId,
              tags: data.tags,
              notes: data.notes,
            }
          : {
              description: data.description,
              amount: data.amount,
              category: data.category,
              date: data.date,
              cardId: null,
              tags: data.tags,
              notes: data.notes,
            },
        latest.etag,
      );
      const updatedYm = updated.yearMonth;
      if (updatedYm !== ym) {
        removeTransactionLocal({ id: tx.id, userId: tx.userId, yearMonth: ym });
      }
      upsertTransactions([
        toTransaction(updated, {
          currentUserId: currentUser.id,
          partnerUserId: partner?.partnerUserId,
          partnerName: partner?.name,
        }),
      ]);
      mergeKnownTags(data.tags);
      onTransactionMutated?.(updatedYm === ym ? [ym] : [ym, updatedYm]);
    } catch (err) {
      throw new Error(transactionsErrorMessage(err));
    }
  }

  const defaultSheetDate = useMemo(
    () => defaultNewTransactionDate(year, monthIdx),
    [monthIdx, year],
  );

  return (
    <>
      <div className="bg-paper rounded-t-2xl mx-0 pb-bottom-nav">
        <div className="px-4 pt-3 pb-2 flex items-center justify-between gap-3 sm:px-5 md:px-8 lg:px-10">
          <div className="flex items-center gap-2">
            <div className="text-[11px] uppercase tracking-[0.18em] text-ink-muted font-medium">
              Movimentações
            </div>
            {totalLancamentos > 0 && (
              <span className="inline-flex items-center h-5 rounded-full bg-paper-card border border-paper-line px-2 text-[10px] font-semibold text-ink-muted tabular-nums">
                {totalLancamentos}
              </span>
            )}
          </div>
          <button
            type="button"
            onClick={() => setSheetDate(defaultSheetDate)}
            className="inline-flex h-9 items-center gap-1.5 rounded-xl bold-surface px-3 text-xs font-semibold shadow-soft-sm active:scale-[0.98] transition tap-surface"
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.3" strokeLinecap="round" strokeLinejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Lançar
          </button>
        </div>

        {monthLoad?.status === 'loading' && totalLancamentos === 0 && (
          <div className="mx-4 mb-2 rounded-xl border border-paper-line bg-paper-card px-3 py-2 text-[12px] text-ink-muted sm:mx-5 md:mx-8 lg:mx-10">
            Carregando lançamentos...
          </div>
        )}
        {monthLoad?.status === 'error' && (
          <div className="mx-4 mb-2 rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg sm:mx-5 md:mx-8 lg:mx-10">
            {monthLoad.error ?? 'Não foi possível carregar os lançamentos.'}
          </div>
        )}
        {actionError && (
          <div className="mx-4 mb-2 rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg sm:mx-5 md:mx-8 lg:mx-10">
            {actionError}
          </div>
        )}

        <div>
          {perDay.map((row) => {
            const isExpanded = expandedDay === row.day;
            const displayTransactions = showAggregateAuditTrail
              ? auditTrail.transactionsByDay.get(row.day) ?? row.txs
              : row.txs;
            const displayDayNet = showAggregateAuditTrail
              ? auditTrail.netByDay.get(row.day) ?? row.net
              : row.net;
            const displayAccumulated =
              totalModeReady
                ? totalModeState.balancesByDay.get(row.day) ?? row.accumulated
                : row.accumulated;
            return (
              <div key={row.day}>
                <DayRow
                  year={year}
                  monthIdx={monthIdx}
                  day={row.day}
                  dayNet={displayDayNet}
                  totalAcumulado={displayAccumulated}
                  expanded={isExpanded}
                  onToggle={() => setExpandedDay(isExpanded ? null : row.day)}
                  transactions={displayTransactions}
                  markerLabel={largestExpenseDays.has(row.day) ? 'Maior saída' : undefined}
                />
                {isExpanded && (
                  <DayDetails
                    transactions={displayTransactions}
                    onRemove={(tx) => void removeTransaction(tx)}
                    onEdit={(tx) => {
                      setSelectedTx(null);
                      setEditingTx(tx);
                    }}
                    onOpen={(tx) => setSelectedTx(tx)}
                    removingId={removingId}
                    onNew={() => setSheetDate(isoDate(year, monthIdx, row.day))}
                  />
                )}
              </div>
            );
          })}
        </div>
      </div>

      <button
        type="button"
        onClick={() => setSheetDate(defaultSheetDate)}
        className="fixed right-4 z-30 inline-flex h-[52px] items-center gap-2 rounded-full bg-accent-invest px-5 text-sm font-semibold text-white shadow-fab active:scale-[0.94] transition tap-surface animate-scale-in sm:right-6 md:right-8"
        style={{
          bottom: 'calc(var(--bottom-nav-h) + env(safe-area-inset-bottom, 0px) + 12px)',
        }}
        aria-label="Lançar"
      >
        <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
        Lançar
      </button>

      <Suspense fallback={null}>
        {sheetDate !== null && (
          <NewTransactionSheet
            open
            date={sheetDate}
            cards={cards}
            cardsStatus={cardsStatus}
            cardsError={cardsError}
            onClose={() => setSheetDate(null)}
            onNavigateToCards={onNavigateToCards}
            onCreateCard={createQuickCard}
            onSubmit={submitTransaction}
            tagSuggestions={knownTags}
            transactionSuggestions={transactions}
          />
        )}

        {editingTx !== null && (
          <EditTransactionSheet
            open
            tx={editingTx}
            onClose={() => setEditingTx(null)}
            onSubmit={submitEdit}
            tagSuggestions={knownTags}
            cards={cards}
            cardsStatus={cardsStatus}
            cardsError={cardsError}
            onCreateCard={createQuickCard}
          />
        )}

        {selectedTx !== null && (
          <TransactionDetailsSheet
            open
            tx={selectedTx}
            onClose={() => setSelectedTx(null)}
            onEdit={(tx) => {
              setSelectedTx(null);
              setEditingTx(tx);
            }}
            onRemove={(tx) => void removeTransaction(tx)}
            onDeleteGroup={(tx) => void removeTransactionGroup(tx)}
            deleting={removingId === selectedTx.id}
            deletingGroup={!!selectedTx.installments && removingGroupId === selectedTx.installments.groupId}
            onCreateFixedFromTransaction={onCreateFixedFromTransaction}
          />
        )}
      </Suspense>
    </>
  );
}

function defaultNewTransactionDate(year: number, monthIdx: number): string {
  const now = new Date();
  if (now.getFullYear() === year && now.getMonth() === monthIdx) {
    return isoDate(year, monthIdx, now.getDate());
  }
  return isoDate(year, monthIdx, 1);
}

function yearMonthsFromResponses(items: TransactionResponse[]): string[] {
  return [...new Set(items.map((item) => item.yearMonth))];
}

function cardsErrorMessage(err: unknown) {
  if (err instanceof CardsApiError) {
    if (err.code === 'invalid_title') return 'Informe um título válido para o cartão.';
    if (err.code === 'invalid_billing_day') return 'Informe dias entre 1 e 31.';
    if (err.code === 'unsupported_currency') return 'Apenas BRL está disponível no momento.';
    if (err.code === 'unauthorized') return 'Sua sessão expirou. Entre novamente.';
    if (err.code === 'rate_limited') return 'Muitas tentativas. Aguarde um pouco e tente de novo.';
    if (err.code === 'cards_dependency_unavailable') return 'Não foi possível acessar o serviço de cartões agora.';
    return err.message || 'Não foi possível concluir a operação.';
  }

  return err instanceof Error ? err.message : 'Não foi possível concluir a operação.';
}

function transactionsErrorMessage(err: unknown) {
  if (err instanceof TransactionsApiError) {
    if (err.code === 'invalid_date') return 'Informe uma data válida.';
    if (err.code === 'invalid_card_id') return 'Selecione um cartão válido.';
    if (err.code === 'invalid_installments') return 'Informe um parcelamento válido.';
    if (err.code === 'idempotency_key_reused') return 'Este lançamento já foi enviado com outros dados.';
    if (err.code === 'transaction_conflict') return 'Este lançamento foi alterado em outro lugar.';
    if (err.code === 'precondition_failed') return 'Este lançamento mudou em outro dispositivo. Atualize e tente novamente.';
    if (err.code === 'unauthorized') return 'Sua sessão expirou. Entre novamente.';
    if (err.code === 'rate_limited') return 'Muitas tentativas. Aguarde um pouco e tente de novo.';
    if (err.code === 'transactions_dependency_unavailable') {
      return 'Não foi possível acessar o serviço de lançamentos agora.';
    }
    return err.message || 'Não foi possível concluir a operação.';
  }

  return err instanceof Error ? err.message : 'Não foi possível concluir a operação.';
}
