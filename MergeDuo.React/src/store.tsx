/* eslint-disable react-refresh/only-export-components */
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useReducer,
  useRef,
  type ReactNode,
} from 'react';
import type {
  Card,
  FinanceUser,
  FixedTransactionRule,
  MergePartnerInfo,
  OwnerFilter,
  Transaction,
  TransactionCategory,
} from './types';
import { normalizeTags } from './tags';

export type CardsStatus = 'idle' | 'loading' | 'ready' | 'error';
export type FixedRulesStatus = CardsStatus;
export type TransactionsStatus = CardsStatus;

export interface TransactionLoadState {
  status: TransactionsStatus;
  error: string | null;
  continuationToken: string | null;
  itemKeys: string[];
}

export interface TransactionCacheKeyInput {
  yearMonth: string;
  owner: OwnerFilter;
  category?: TransactionCategory | null;
  cardId?: string | null;
}

interface State {
  currentUser: FinanceUser | null;
  transactionsByKey: Record<string, Transaction>;
  transactionLoads: Record<string, TransactionLoadState>;
  fixedTransactions: FixedTransactionRule[];
  fixedRulesStatus: FixedRulesStatus;
  fixedRulesError: string | null;
  cards: Card[];
  cardsStatus: CardsStatus;
  cardsError: string | null;
  knownTags: string[];
  tagsStatus: CardsStatus;
  tagsError: string | null;
  partner: MergePartnerInfo | null;
  ownerFilter: OwnerFilter;
}

type Action =
  | { type: 'set_current_user'; user: FinanceUser }
  | { type: 'restore_user_state'; user: FinanceUser; cache: PersistedFinanceState | null }
  | { type: 'clear_current_user' }
  | { type: 'set_transactions_loading'; key: string }
  | {
      type: 'set_transactions';
      key: string;
      txs: Transaction[];
      continuationToken: string | null;
    }
  | { type: 'set_transactions_error'; key: string; error: string }
  | { type: 'upsert_transactions'; txs: Transaction[] }
  | { type: 'remove_transaction'; tx: Pick<Transaction, 'id' | 'userId' | 'yearMonth'> }
  | { type: 'clear_transactions' }
  | { type: 'set_fixed_rules_loading' }
  | { type: 'set_fixed_rules'; rules: FixedTransactionRule[] }
  | { type: 'set_fixed_rules_error'; error: string }
  | { type: 'add_fixed_rule'; rule: FixedTransactionRule }
  | { type: 'update_fixed_rule'; rule: FixedTransactionRule }
  | { type: 'remove_fixed_rule'; id: string }
  | { type: 'clear_fixed_rules' }
  | { type: 'set_cards_loading' }
  | { type: 'set_cards'; cards: Card[] }
  | { type: 'set_cards_error'; error: string }
  | { type: 'add_card'; card: Card }
  | { type: 'update_card'; card: Card }
  | { type: 'remove_card'; id: string }
  | { type: 'clear_cards' }
  | { type: 'set_tags_loading' }
  | { type: 'set_known_tags'; tags: string[] }
  | { type: 'merge_known_tags'; tags: string[] }
  | { type: 'set_tags_error'; error: string }
  | { type: 'clear_tags' }
  | { type: 'set_partnership'; partnership: MergePartnerInfo | null }
  | { type: 'clear_partnership' }
  | { type: 'set_owner_filter'; filter: OwnerFilter };

interface PersistedFinanceState {
  version: number;
  transactionsByKey: Record<string, Transaction>;
  transactionLoads: Record<string, TransactionLoadState>;
  fixedTransactions: FixedTransactionRule[];
  fixedRulesStatus: FixedRulesStatus;
  cards: Card[];
  cardsStatus: CardsStatus;
  knownTags: string[];
  tagsStatus: CardsStatus;
  partner: MergePartnerInfo | null;
  ownerFilter: OwnerFilter;
}

const FINANCE_CACHE_VERSION = 1;
const FINANCE_CACHE_PREFIX = 'mergeduo:finance';
const FINANCE_CACHE_WRITE_DELAY_MS = 150;

function emptyStateForUser(user: FinanceUser): State {
  return {
    currentUser: user,
    transactionsByKey: {},
    transactionLoads: {},
    fixedTransactions: [],
    fixedRulesStatus: 'idle',
    fixedRulesError: null,
    cards: [],
    cardsStatus: 'idle',
    cardsError: null,
    knownTags: [],
    tagsStatus: 'idle',
    tagsError: null,
    partner: null,
    ownerFilter: 'me',
  };
}

function reducer(state: State, action: Action): State {
  switch (action.type) {
    case 'set_current_user':
      return { ...state, currentUser: action.user };
    case 'restore_user_state': {
      const next = emptyStateForUser(action.user);
      if (!action.cache) return next;

      return {
        ...next,
        transactionsByKey: action.cache.transactionsByKey,
        transactionLoads: action.cache.transactionLoads,
        fixedTransactions: action.cache.fixedTransactions,
        fixedRulesStatus: action.cache.fixedRulesStatus,
        cards: action.cache.cards,
        cardsStatus: action.cache.cardsStatus,
        knownTags: action.cache.knownTags,
        tagsStatus: action.cache.tagsStatus,
        partner: action.cache.partner,
        ownerFilter: action.cache.ownerFilter,
      };
    }
    case 'clear_current_user':
      return { ...state, currentUser: null };
    case 'set_transactions_loading': {
      const current = state.transactionLoads[action.key];
      return {
        ...state,
        transactionLoads: {
          ...state.transactionLoads,
          [action.key]: {
            status: 'loading',
            error: null,
            continuationToken: current?.continuationToken ?? null,
            itemKeys: current?.itemKeys ?? [],
          },
        },
      };
    }
    case 'set_transactions': {
      const nextByKey = { ...state.transactionsByKey };
      const previousKeys = state.transactionLoads[action.key]?.itemKeys ?? [];
      const keysStillReferenced = new Set(
        Object.entries(state.transactionLoads)
          .filter(([key]) => key !== action.key)
          .flatMap(([, load]) => load.itemKeys),
      );
      for (const key of previousKeys) {
        if (!keysStillReferenced.has(key)) {
          delete nextByKey[key];
        }
      }

      const itemKeys: string[] = [];
      for (const tx of action.txs) {
        const key = transactionIdentityKey(tx);
        nextByKey[key] = tx;
        itemKeys.push(key);
      }

      return {
        ...state,
        transactionsByKey: nextByKey,
        transactionLoads: {
          ...state.transactionLoads,
          [action.key]: {
            status: 'ready',
            error: null,
            continuationToken: action.continuationToken,
            itemKeys,
          },
        },
      };
    }
    case 'set_transactions_error': {
      const current = state.transactionLoads[action.key];
      return {
        ...state,
        transactionLoads: {
          ...state.transactionLoads,
          [action.key]: {
            status: 'error',
            error: action.error,
            continuationToken: current?.continuationToken ?? null,
            itemKeys: current?.itemKeys ?? [],
          },
        },
      };
    }
    case 'upsert_transactions': {
      const nextByKey = { ...state.transactionsByKey };
      for (const tx of action.txs) {
        nextByKey[transactionIdentityKey(tx)] = tx;
      }
      return { ...state, transactionsByKey: nextByKey };
    }
    case 'remove_transaction': {
      const nextByKey = Object.fromEntries(
        Object.entries(state.transactionsByKey).filter(([, tx]) => {
          if (tx.id !== action.tx.id) return true;
          if (action.tx.userId && tx.userId !== action.tx.userId) return true;
          if (action.tx.yearMonth && tx.yearMonth !== action.tx.yearMonth) return true;
          return false;
        }),
      );
      const transactionLoads = Object.fromEntries(
        Object.entries(state.transactionLoads).map(([key, load]) => [
          key,
          {
            ...load,
            itemKeys: load.itemKeys.filter((itemKey) => nextByKey[itemKey]),
          },
        ]),
      );
      return { ...state, transactionsByKey: nextByKey, transactionLoads };
    }
    case 'clear_transactions':
      return { ...state, transactionsByKey: {}, transactionLoads: {} };
    case 'set_fixed_rules_loading':
      return { ...state, fixedRulesStatus: 'loading', fixedRulesError: null };
    case 'set_fixed_rules':
      return {
        ...state,
        fixedTransactions: action.rules,
        fixedRulesStatus: 'ready',
        fixedRulesError: null,
      };
    case 'set_fixed_rules_error':
      return { ...state, fixedRulesStatus: 'error', fixedRulesError: action.error };
    case 'add_fixed_rule':
      return {
        ...state,
        fixedTransactions: [action.rule, ...state.fixedTransactions],
        fixedRulesStatus: 'ready',
        fixedRulesError: null,
      };
    case 'update_fixed_rule':
      return {
        ...state,
        fixedTransactions: state.fixedTransactions.map((r) =>
          r.id === action.rule.id ? action.rule : r,
        ),
        fixedRulesStatus: 'ready',
        fixedRulesError: null,
      };
    case 'remove_fixed_rule':
      return {
        ...state,
        fixedTransactions: state.fixedTransactions.filter((r) => r.id !== action.id),
        fixedRulesStatus: 'ready',
        fixedRulesError: null,
      };
    case 'clear_fixed_rules':
      return { ...state, fixedTransactions: [], fixedRulesStatus: 'idle', fixedRulesError: null };
    case 'set_cards_loading':
      return { ...state, cardsStatus: 'loading', cardsError: null };
    case 'set_cards':
      return { ...state, cards: action.cards, cardsStatus: 'ready', cardsError: null };
    case 'set_cards_error':
      return { ...state, cardsStatus: 'error', cardsError: action.error };
    case 'add_card':
      return {
        ...state,
        cards: [action.card, ...state.cards],
        cardsStatus: 'ready',
        cardsError: null,
      };
    case 'update_card':
      return {
        ...state,
        cards: state.cards.map((card) => (card.id === action.card.id ? action.card : card)),
        cardsStatus: 'ready',
        cardsError: null,
      };
    case 'remove_card':
      return {
        ...state,
        cards: state.cards.filter((c) => c.id !== action.id),
        cardsStatus: 'ready',
        cardsError: null,
      };
    case 'clear_cards':
      return { ...state, cards: [], cardsStatus: 'idle', cardsError: null };
    case 'set_tags_loading':
      return { ...state, tagsStatus: 'loading', tagsError: null };
    case 'set_known_tags':
      return {
        ...state,
        knownTags: normalizeKnownTags(action.tags),
        tagsStatus: 'ready',
        tagsError: null,
      };
    case 'merge_known_tags':
      return {
        ...state,
        knownTags: normalizeKnownTags([...state.knownTags, ...action.tags]),
        tagsStatus: 'ready',
        tagsError: null,
      };
    case 'set_tags_error':
      return { ...state, tagsStatus: 'error', tagsError: action.error };
    case 'clear_tags':
      return { ...state, knownTags: [], tagsStatus: 'idle', tagsError: null };
    case 'set_partnership':
      return {
        ...state,
        partner: action.partnership,
        ownerFilter: action.partnership?.status === 'active' ? 'both' : 'me',
      };
    case 'clear_partnership':
      return { ...state, partner: null, ownerFilter: 'me' };
    case 'set_owner_filter':
      return { ...state, ownerFilter: action.filter };
  }
}

interface Ctx {
  currentUser: FinanceUser | null;
  startingBalance: number;
  transactions: Transaction[];
  transactionLoads: Record<string, TransactionLoadState>;
  fixedTransactions: FixedTransactionRule[];
  fixedRulesStatus: FixedRulesStatus;
  fixedRulesError: string | null;
  cards: Card[];
  cardsStatus: CardsStatus;
  cardsError: string | null;
  knownTags: string[];
  tagsStatus: CardsStatus;
  tagsError: string | null;
  partner: MergePartnerInfo | null;
  mergeActive: boolean;
  ownerFilter: OwnerFilter;
  setCurrentUserFinance: (user: FinanceUser) => void;
  clearCurrentUserFinance: () => void;
  setTransactionsLoading: (key: string) => void;
  setTransactions: (key: string, txs: Transaction[], continuationToken?: string | null) => void;
  setTransactionsError: (key: string, error: string) => void;
  upsertTransactions: (txs: Transaction[]) => void;
  removeTransactionLocal: (tx: Pick<Transaction, 'id' | 'userId' | 'yearMonth'>) => void;
  clearTransactions: () => void;
  setFixedRulesLoading: () => void;
  setFixedRules: (rules: FixedTransactionRule[]) => void;
  setFixedRulesError: (error: string) => void;
  addFixedRule: (rule: FixedTransactionRule) => void;
  updateFixedRule: (rule: FixedTransactionRule) => void;
  removeFixedRule: (id: string) => void;
  clearFixedRules: () => void;
  setCardsLoading: () => void;
  setCards: (cards: Card[]) => void;
  setCardsError: (error: string) => void;
  addCard: (card: Card) => void;
  updateCard: (card: Card) => void;
  removeCard: (id: string) => void;
  clearCards: () => void;
  setTagsLoading: () => void;
  setKnownTags: (tags: string[]) => void;
  mergeKnownTags: (tags: string[]) => void;
  setTagsError: (error: string) => void;
  clearTags: () => void;
  setPartnership: (partnership: MergePartnerInfo | null) => void;
  clearPartnership: () => void;
  setOwnerFilter: (filter: OwnerFilter) => void;
}

const FinanceCtx = createContext<Ctx | null>(null);

export function FinanceProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(reducer, {
    currentUser: null,
    transactionsByKey: {},
    transactionLoads: {},
    fixedTransactions: [],
    fixedRulesStatus: 'idle',
    fixedRulesError: null,
    cards: [],
    cardsStatus: 'idle',
    cardsError: null,
    knownTags: [],
    tagsStatus: 'idle',
    tagsError: null,
    partner: null,
    ownerFilter: 'me',
  });
  const pendingPersistRef = useRef<{ userId: string; state: State } | null>(null);
  const persistTimeoutRef = useRef<number | null>(null);

  const currentUserId = state.currentUser?.id ?? null;
  const mergeActive = state.partner?.status === 'active';
  const transactions = useMemo(
    () =>
      Object.values(state.transactionsByKey).sort((a, b) => {
        const dateCmp = a.date.localeCompare(b.date);
        if (dateCmp !== 0) return dateCmp;
        return (a.createdAt ?? '').localeCompare(b.createdAt ?? '');
      }),
    [state.transactionsByKey],
  );

  const setCurrentUserFinance = useCallback((user: FinanceUser) => {
    dispatch({ type: 'restore_user_state', user, cache: loadPersistedFinanceState(user.id) });
  }, []);

  const clearCurrentUserFinance = useCallback(() => dispatch({ type: 'clear_current_user' }), []);

  const setTransactionsLoading = useCallback((key: string) =>
    dispatch({ type: 'set_transactions_loading', key }), []);

  const setTransactions = useCallback((
    key: string,
    txs: Transaction[],
    continuationToken: string | null = null,
  ) => dispatch({ type: 'set_transactions', key, txs, continuationToken }), []);

  const setTransactionsError = useCallback((key: string, error: string) =>
    dispatch({ type: 'set_transactions_error', key, error }), []);

  const upsertTransactions = useCallback((txs: Transaction[]) =>
    dispatch({ type: 'upsert_transactions', txs }), []);

  const removeTransactionLocal = useCallback((
    tx: Pick<Transaction, 'id' | 'userId' | 'yearMonth'>,
  ) => dispatch({ type: 'remove_transaction', tx }), []);

  const clearTransactions = useCallback(() => dispatch({ type: 'clear_transactions' }), []);

  const setFixedRulesLoading = useCallback(() => dispatch({ type: 'set_fixed_rules_loading' }), []);

  const setFixedRules = useCallback((rules: FixedTransactionRule[]) =>
    dispatch({ type: 'set_fixed_rules', rules }), []);

  const setFixedRulesError = useCallback((error: string) =>
    dispatch({ type: 'set_fixed_rules_error', error }), []);

  const addFixedRule = useCallback((rule: FixedTransactionRule) =>
    dispatch({ type: 'add_fixed_rule', rule }), []);

  const updateFixedRule = useCallback((rule: FixedTransactionRule) =>
    dispatch({ type: 'update_fixed_rule', rule }), []);

  const removeFixedRule = useCallback((id: string) =>
    dispatch({ type: 'remove_fixed_rule', id }), []);

  const clearFixedRules = useCallback(() => dispatch({ type: 'clear_fixed_rules' }), []);

  const setCardsLoading = useCallback(() => dispatch({ type: 'set_cards_loading' }), []);

  const setCards = useCallback((cards: Card[]) =>
    dispatch({ type: 'set_cards', cards }), []);

  const setCardsError = useCallback((error: string) =>
    dispatch({ type: 'set_cards_error', error }), []);

  const addCard = useCallback((card: Card) =>
    dispatch({ type: 'add_card', card }), []);

  const updateCard = useCallback((card: Card) =>
    dispatch({ type: 'update_card', card }), []);

  const removeCard = useCallback((id: string) => dispatch({ type: 'remove_card', id }), []);

  const clearCards = useCallback(() => dispatch({ type: 'clear_cards' }), []);

  const setTagsLoading = useCallback(() => dispatch({ type: 'set_tags_loading' }), []);

  const setKnownTags = useCallback((tags: string[]) =>
    dispatch({ type: 'set_known_tags', tags }), []);

  const mergeKnownTags = useCallback((tags: string[]) =>
    dispatch({ type: 'merge_known_tags', tags }), []);

  const setTagsError = useCallback((error: string) =>
    dispatch({ type: 'set_tags_error', error }), []);

  const clearTags = useCallback(() => dispatch({ type: 'clear_tags' }), []);

  const setPartnership = useCallback((partnership: MergePartnerInfo | null) =>
    dispatch({ type: 'set_partnership', partnership }), []);

  const clearPartnership = useCallback(() => dispatch({ type: 'clear_partnership' }), []);

  const setOwnerFilter = useCallback((filter: OwnerFilter) =>
    dispatch({ type: 'set_owner_filter', filter }), []);

  const flushPendingPersist = useCallback(() => {
    if (persistTimeoutRef.current != null && typeof window !== 'undefined') {
      window.clearTimeout(persistTimeoutRef.current);
      persistTimeoutRef.current = null;
    }

    const pending = pendingPersistRef.current;
    if (!pending || typeof localStorage === 'undefined') return;
    pendingPersistRef.current = null;

    try {
      localStorage.setItem(
        financeCacheKey(pending.userId),
        JSON.stringify(createPersistedFinanceState(pending.state)),
      );
    } catch {
      // Ignore quota / privacy-mode failures and keep the in-memory state.
    }
  }, []);

  useEffect(() => {
    if (!currentUserId || typeof localStorage === 'undefined' || typeof window === 'undefined') {
      flushPendingPersist();
      return;
    }

    pendingPersistRef.current = { userId: currentUserId, state };
    if (persistTimeoutRef.current != null) {
      window.clearTimeout(persistTimeoutRef.current);
    }
    persistTimeoutRef.current = window.setTimeout(() => {
      persistTimeoutRef.current = null;
      flushPendingPersist();
    }, FINANCE_CACHE_WRITE_DELAY_MS);
  }, [currentUserId, flushPendingPersist, state]);

  useEffect(() => () => {
    flushPendingPersist();
  }, [flushPendingPersist]);

  const value = useMemo<Ctx>(
    () => ({
      currentUser: state.currentUser,
      startingBalance: state.currentUser?.startingBalance ?? 0,
      transactions,
      transactionLoads: state.transactionLoads,
      fixedTransactions: state.fixedTransactions,
      fixedRulesStatus: state.fixedRulesStatus,
      fixedRulesError: state.fixedRulesError,
      cards: state.cards,
      cardsStatus: state.cardsStatus,
      cardsError: state.cardsError,
      knownTags: state.knownTags,
      tagsStatus: state.tagsStatus,
      tagsError: state.tagsError,
      partner: state.partner,
      mergeActive,
      ownerFilter: state.ownerFilter,
      setCurrentUserFinance,
      clearCurrentUserFinance,
      setTransactionsLoading,
      setTransactions,
      setTransactionsError,
      upsertTransactions,
      removeTransactionLocal,
      clearTransactions,
      setFixedRulesLoading,
      setFixedRules,
      setFixedRulesError,
      addFixedRule,
      updateFixedRule,
      removeFixedRule,
      clearFixedRules,
      setCardsLoading,
      setCards,
      setCardsError,
      addCard,
      updateCard,
      removeCard,
      clearCards,
      setTagsLoading,
      setKnownTags,
      mergeKnownTags,
      setTagsError,
      clearTags,
      setPartnership,
      clearPartnership,
      setOwnerFilter,
    }),
    [
      state.currentUser,
      state.transactionLoads,
      state.fixedTransactions,
      state.fixedRulesStatus,
      state.fixedRulesError,
      state.cards,
      state.cardsStatus,
      state.cardsError,
      state.knownTags,
      state.tagsStatus,
      state.tagsError,
      state.partner,
      state.ownerFilter,
      transactions,
      mergeActive,
      setCurrentUserFinance,
      clearCurrentUserFinance,
      setTransactionsLoading,
      setTransactions,
      setTransactionsError,
      upsertTransactions,
      removeTransactionLocal,
      clearTransactions,
      setFixedRulesLoading,
      setFixedRules,
      setFixedRulesError,
      addFixedRule,
      updateFixedRule,
      removeFixedRule,
      clearFixedRules,
      setCardsLoading,
      setCards,
      setCardsError,
      addCard,
      updateCard,
      removeCard,
      clearCards,
      setTagsLoading,
      setKnownTags,
      mergeKnownTags,
      setTagsError,
      clearTags,
      setPartnership,
      clearPartnership,
      setOwnerFilter,
    ],
  );

  return <FinanceCtx.Provider value={value}>{children}</FinanceCtx.Provider>;
}

export function useFinance() {
  const ctx = useContext(FinanceCtx);
  if (!ctx) throw new Error('useFinance must be used within FinanceProvider');
  return ctx;
}

export function transactionCacheKey(input: TransactionCacheKeyInput) {
  return [
    input.yearMonth,
    input.owner,
    input.category ?? '',
    input.cardId ?? '',
  ].join('|');
}

export function transactionIdentityKey(tx: Pick<Transaction, 'id' | 'userId' | 'yearMonth' | 'date'>) {
  return `${tx.userId ?? 'local'}:${tx.yearMonth ?? tx.date.slice(0, 7)}:${tx.id}`;
}

function financeCacheKey(userId: string) {
  return `${FINANCE_CACHE_PREFIX}:${userId}`;
}

function createPersistedFinanceState(state: State): PersistedFinanceState {
  return {
    version: FINANCE_CACHE_VERSION,
    transactionsByKey: state.transactionsByKey,
    transactionLoads: normalizeTransactionLoads(state.transactionLoads),
    fixedTransactions: state.fixedTransactions,
    fixedRulesStatus: state.fixedTransactions.length > 0 ? 'ready' : normalizeListStatus(state.fixedRulesStatus),
    cards: state.cards,
    cardsStatus: state.cards.length > 0 ? 'ready' : normalizeListStatus(state.cardsStatus),
    knownTags: state.knownTags,
    tagsStatus: state.knownTags.length > 0 ? 'ready' : normalizeListStatus(state.tagsStatus),
    partner: state.partner,
    ownerFilter: state.partner?.status === 'active'
      ? state.ownerFilter
      : 'me',
  };
}

function loadPersistedFinanceState(userId: string): PersistedFinanceState | null {
  if (typeof localStorage === 'undefined') return null;

  try {
    const raw = localStorage.getItem(financeCacheKey(userId));
    if (!raw) return null;

    const parsed = JSON.parse(raw) as unknown;
    return parsePersistedFinanceState(parsed);
  } catch {
    return null;
  }
}

function parsePersistedFinanceState(value: unknown): PersistedFinanceState | null {
  if (!value || typeof value !== 'object') return null;

  const candidate = value as Partial<PersistedFinanceState>;
  if (candidate.version !== FINANCE_CACHE_VERSION) return null;

  return {
    version: FINANCE_CACHE_VERSION,
    transactionsByKey: asRecord<Transaction>(candidate.transactionsByKey),
    transactionLoads: normalizeTransactionLoads(asRecord<TransactionLoadState>(candidate.transactionLoads)),
    fixedTransactions: Array.isArray(candidate.fixedTransactions) ? candidate.fixedTransactions : [],
    fixedRulesStatus: normalizeListStatus(candidate.fixedRulesStatus),
    cards: Array.isArray(candidate.cards) ? candidate.cards : [],
    cardsStatus: normalizeListStatus(candidate.cardsStatus),
    knownTags: normalizeKnownTags(candidate.knownTags),
    tagsStatus: normalizeListStatus(candidate.tagsStatus),
    partner: candidate.partner && typeof candidate.partner === 'object' ? candidate.partner : null,
    ownerFilter: normalizeOwnerFilter(candidate.ownerFilter),
  };
}

function normalizeTransactionLoads(
  loads: Record<string, TransactionLoadState>,
): Record<string, TransactionLoadState> {
  return Object.fromEntries(
    Object.entries(loads).map(([key, load]) => {
      const itemKeys = Array.isArray(load?.itemKeys) ? load.itemKeys : [];
      const status = load?.status === 'error'
        ? 'error'
        : itemKeys.length > 0
          ? 'ready'
          : 'idle';

      return [key, {
        status,
        error: status === 'error' && typeof load?.error === 'string' ? load.error : null,
        continuationToken: typeof load?.continuationToken === 'string' ? load.continuationToken : null,
        itemKeys,
      } satisfies TransactionLoadState];
    }),
  );
}

function normalizeListStatus(status: unknown): CardsStatus {
  return status === 'error' ? 'error' : 'idle';
}

function normalizeOwnerFilter(value: unknown): OwnerFilter {
  return value === 'partner' || value === 'both' ? value : 'me';
}

function asRecord<T>(value: unknown): Record<string, T> {
  return value && typeof value === 'object' ? value as Record<string, T> : {};
}

function normalizeKnownTags(value: unknown): string[] {
  return Array.isArray(value)
    ? normalizeTags(value.filter((item): item is string => typeof item === 'string'))
    : [];
}
