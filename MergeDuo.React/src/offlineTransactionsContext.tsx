/* eslint-disable react-refresh/only-export-components */
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { computeInvoiceDueDate } from './cardInvoice';
import {
  createTransaction,
  toTransaction,
  type CreateTransactionRequest,
} from './api/transactions';
import {
  ApiError,
  NetworkError,
  TimeoutError,
  newIdempotencyKey,
} from './api/http';
import { useFinance } from './store';
import { CATEGORY_META, type Card, type Transaction } from './types';

interface QueuedCreateTransaction {
  id: string;
  userId: string;
  createdAt: string;
  idempotencyKey: string;
  request: CreateTransactionRequest;
  optimisticTransaction: Transaction;
}

interface CreateTransactionWithOfflineInput {
  request: CreateTransactionRequest;
  card?: Card | null;
}

interface CreateTransactionWithOfflineResult {
  queued: boolean;
}

interface OfflineTransactionsContextValue {
  queuedCreates: number;
  syncingQueue: boolean;
  createTransactionWithOfflineQueue: (
    input: CreateTransactionWithOfflineInput,
  ) => Promise<CreateTransactionWithOfflineResult>;
  discardLocalTransaction: (tx: Transaction) => void;
}

const QUEUED_CREATE_STORAGE_KEY = 'mergeduo:offline:create-transactions';

const OfflineTransactionsCtx = createContext<OfflineTransactionsContextValue | null>(null);

export function OfflineTransactionsProvider({
  accessToken,
  onRemoteCommit,
  children,
}: {
  accessToken: string;
  onRemoteCommit?: (yearMonths?: string[]) => void;
  children: ReactNode;
}) {
  const {
    currentUser,
    partner,
    upsertTransactions,
    removeTransactionLocal,
  } = useFinance();
  const [queuedCreates, setQueuedCreates] = useState(0);
  const [syncingQueue, setSyncingQueue] = useState(false);
  const flushInFlightRef = useRef(false);

  const syncQueuedCount = useCallback((userId: string | null | undefined) => {
    setQueuedCreates(userId ? listQueuedCreates(userId).length : 0);
  }, []);

  const flushQueuedCreates = useCallback(async () => {
    if (!currentUser || !hasNetworkConnection() || flushInFlightRef.current) {
      syncQueuedCount(currentUser?.id);
      return;
    }

    const queuedItems = listQueuedCreates(currentUser.id);
    if (queuedItems.length === 0) {
      syncQueuedCount(currentUser.id);
      return;
    }

    flushInFlightRef.current = true;
    setSyncingQueue(true);
    let committedAny = false;
    const committedYearMonths = new Set<string>();

    try {
      for (const item of queuedItems) {
        try {
          const response = await createTransaction(accessToken, item.request, item.idempotencyKey);
          removeQueuedCreate(item.id);
          removeTransactionLocal({
            id: item.optimisticTransaction.id,
            userId: item.optimisticTransaction.userId,
            yearMonth: item.optimisticTransaction.yearMonth,
          });
          upsertTransactions(response.items.map((tx) => toTransaction(tx, {
            currentUserId: currentUser.id,
            partnerUserId: partner?.partnerUserId,
            partnerName: partner?.name,
          })));
          committedAny = true;
          for (const yearMonth of yearMonthsFromResponses(response.items)) {
            committedYearMonths.add(yearMonth);
          }
        } catch (err) {
          if (isRetryableCreateError(err)) {
            break;
          }

          removeQueuedCreate(item.id);
          upsertTransactions([{
            ...item.optimisticTransaction,
            pendingSync: false,
            syncError: toQueueErrorMessage(err),
            source: { channel: 'offline_failed' },
          }]);
        }
      }
    } finally {
      flushInFlightRef.current = false;
      setSyncingQueue(false);
      syncQueuedCount(currentUser.id);
      if (committedAny) {
        const yearMonths = Array.from(committedYearMonths);
        onRemoteCommit?.(yearMonths.length > 0 ? yearMonths : undefined);
      }
    }
  }, [
    accessToken,
    currentUser,
    onRemoteCommit,
    partner?.name,
    partner?.partnerUserId,
    removeTransactionLocal,
    syncQueuedCount,
    upsertTransactions,
  ]);

  useEffect(() => {
    if (typeof window === 'undefined') return undefined;

    const timeout = window.setTimeout(() => {
      syncQueuedCount(currentUser?.id);
      if (currentUser) {
        void flushQueuedCreates();
      }
    }, 0);
    if (!currentUser) {
      return () => window.clearTimeout(timeout);
    }

    const handleOnline = () => {
      void flushQueuedCreates();
    };

    window.addEventListener('online', handleOnline);
    return () => {
      window.clearTimeout(timeout);
      window.removeEventListener('online', handleOnline);
    };
  }, [currentUser, flushQueuedCreates, syncQueuedCount]);

  const createTransactionWithOfflineQueue = useCallback(async (
    input: CreateTransactionWithOfflineInput,
  ): Promise<CreateTransactionWithOfflineResult> => {
    if (!currentUser) {
      throw new Error('Sua sessão ainda não foi carregada.');
    }

    const idempotencyKey = newIdempotencyKey();
    try {
      const response = await createTransaction(accessToken, input.request, idempotencyKey);
      upsertTransactions(response.items.map((tx) => toTransaction(tx, {
        currentUserId: currentUser.id,
        partnerUserId: partner?.partnerUserId,
        partnerName: partner?.name,
      })));
      onRemoteCommit?.(yearMonthsFromResponses(response.items));
      return { queued: false };
    } catch (err) {
      if (!isRetryableCreateError(err)) {
        throw err;
      }

      const optimistic = buildOptimisticTransaction({
        request: input.request,
        currentUserId: currentUser.id,
        card: input.card ?? null,
      });

      enqueueQueuedCreate({
        id: optimistic.id,
        userId: currentUser.id,
        createdAt: optimistic.createdAt ?? new Date().toISOString(),
        idempotencyKey,
        request: input.request,
        optimisticTransaction: optimistic,
      });
      upsertTransactions([optimistic]);
      syncQueuedCount(currentUser.id);
      return { queued: true };
    }
  }, [
    accessToken,
    currentUser,
    onRemoteCommit,
    partner?.name,
    partner?.partnerUserId,
    syncQueuedCount,
    upsertTransactions,
  ]);

  const discardLocalTransaction = useCallback((tx: Transaction) => {
    if (!tx.localOnly) return;

    if (tx.pendingSync) {
      removeQueuedCreate(tx.id);
      syncQueuedCount(currentUser?.id);
    }

    removeTransactionLocal({ id: tx.id, userId: tx.userId, yearMonth: tx.yearMonth });
  }, [currentUser?.id, removeTransactionLocal, syncQueuedCount]);

  const value = useMemo<OfflineTransactionsContextValue>(() => ({
    queuedCreates,
    syncingQueue,
    createTransactionWithOfflineQueue,
    discardLocalTransaction,
  }), [
    createTransactionWithOfflineQueue,
    discardLocalTransaction,
    queuedCreates,
    syncingQueue,
  ]);

  return <OfflineTransactionsCtx.Provider value={value}>{children}</OfflineTransactionsCtx.Provider>;
}

export function useOfflineTransactions(): OfflineTransactionsContextValue | null {
  return useContext(OfflineTransactionsCtx);
}

function yearMonthsFromResponses(items: Array<{ yearMonth?: string | null }>): string[] {
  const yearMonths = new Set<string>();
  for (const item of items) {
    if (item.yearMonth) yearMonths.add(item.yearMonth);
  }
  return Array.from(yearMonths);
}

function buildOptimisticTransaction({
  request,
  currentUserId,
  card,
}: {
  request: CreateTransactionRequest;
  currentUserId: string;
  card: Card | null;
}): Transaction {
  const createdAt = new Date().toISOString();
  const id = `offline-${newIdempotencyKey()}`;
  const purchaseDate = request.purchaseDate ?? request.date ?? createdAt.slice(0, 10);
  const installmentsTotal = request.installments?.total ?? 1;
  const isCreditCard = request.category === 'credit_card';
  const effectiveDate = isCreditCard && card
    ? computeInvoiceDueDate(purchaseDate, card)
    : request.date ?? purchaseDate;
  const description = installmentsTotal > 1
    ? `${request.description} (1/${installmentsTotal})`
    : request.description;

  return {
    id,
    userId: currentUserId,
    yearMonth: effectiveDate.slice(0, 7),
    date: effectiveDate,
    purchaseDate: isCreditCard ? purchaseDate : undefined,
    category: request.category,
    kind: CATEGORY_META[request.category].kind,
    description,
    amount: isCreditCard
      ? splitInstallmentAmount(request.amount, installmentsTotal, 1)
      : request.amount,
    currency: request.currency,
    ownerLabel: request.ownerLabel ?? undefined,
    cardId: request.cardId ?? undefined,
    fixedRuleId: request.fixedRuleId ?? undefined,
    installments: installmentsTotal > 1
      ? { index: 1, total: installmentsTotal, groupId: id }
      : undefined,
    tags: request.tags,
    notes: request.notes ?? undefined,
    source: { channel: 'offline_queue' },
    pendingSync: true,
    syncError: null,
    localOnly: true,
    createdAt,
    updatedAt: createdAt,
  };
}

function splitInstallmentAmount(amount: number, total: number, index: number): number {
  if (total <= 1) return amount;

  const totalCents = Math.round(amount * 100);
  const base = Math.floor(totalCents / total);
  const remainder = totalCents - (base * total);
  const cents = base + (index <= remainder ? 1 : 0);
  return cents / 100;
}

function isRetryableCreateError(err: unknown): boolean {
  if (typeof navigator !== 'undefined' && navigator.onLine === false) {
    return true;
  }

  return err instanceof TimeoutError
    || err instanceof NetworkError
    || (err instanceof ApiError && [502, 503, 504].includes(err.status));
}

function toQueueErrorMessage(err: unknown): string {
  if (err instanceof Error && err.message.trim().length > 0) {
    return err.message;
  }

  return 'Não foi possível enviar este lançamento para o servidor.';
}

function hasNetworkConnection(): boolean {
  return typeof navigator === 'undefined' || navigator.onLine !== false;
}

function listQueuedCreates(userId: string): QueuedCreateTransaction[] {
  return readQueuedCreates().filter((entry) => entry.userId === userId);
}

function enqueueQueuedCreate(entry: QueuedCreateTransaction) {
  const items = readQueuedCreates().filter((item) => item.id !== entry.id);
  items.push(entry);
  writeQueuedCreates(items);
}

function removeQueuedCreate(id: string) {
  writeQueuedCreates(readQueuedCreates().filter((entry) => entry.id !== id));
}

function readQueuedCreates(): QueuedCreateTransaction[] {
  if (typeof localStorage === 'undefined') return [];

  try {
    const raw = localStorage.getItem(QUEUED_CREATE_STORAGE_KEY);
    if (!raw) return [];

    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed.filter(isQueuedCreateTransaction);
  } catch {
    return [];
  }
}

function writeQueuedCreates(entries: QueuedCreateTransaction[]) {
  if (typeof localStorage === 'undefined') return;

  try {
    localStorage.setItem(QUEUED_CREATE_STORAGE_KEY, JSON.stringify(entries));
  } catch {
    // Ignore storage failures and keep the in-memory optimistic state.
  }
}

function isQueuedCreateTransaction(value: unknown): value is QueuedCreateTransaction {
  if (!value || typeof value !== 'object') return false;

  const candidate = value as Partial<QueuedCreateTransaction>;
  return typeof candidate.id === 'string'
    && typeof candidate.userId === 'string'
    && typeof candidate.idempotencyKey === 'string'
    && typeof candidate.createdAt === 'string'
    && Boolean(candidate.request)
    && Boolean(candidate.optimisticTransaction);
}
