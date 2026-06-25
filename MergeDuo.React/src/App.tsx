import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { MonthNavigator } from './components/MonthNavigator';
import { YearNavigator } from './components/YearNavigator';
import { DailyList } from './components/DailyList';
import { SummaryHeader } from './components/SummaryHeader';
import { BrandMark } from './components/BrandMark';
import { AuthView } from './components/AuthView';
import { OfflineBanner } from './components/OfflineBanner';

const AnnualView = React.lazy(() => import('./components/AnnualView').then((m) => ({ default: m.AnnualView })));
const ProfileView = React.lazy(() => import('./components/ProfileView').then((m) => ({ default: m.ProfileView })));
const FixedTransactionsView = React.lazy(() => import('./components/FixedTransactionsView').then((m) => ({ default: m.FixedTransactionsView })));
const TagsView = React.lazy(() => import('./components/TagsView').then((m) => ({ default: m.TagsView })));
const CardsView = React.lazy(() => import('./components/CardsView').then((m) => ({ default: m.CardsView })));
const CardInvoiceView = React.lazy(() => import('./components/CardInvoiceView').then((m) => ({ default: m.CardInvoiceView })));
const MergeView = React.lazy(() => import('./components/MergeView').then((m) => ({ default: m.MergeView })));
const SimulatorView = React.lazy(() => import('./components/SimulatorView').then((m) => ({ default: m.SimulatorView })));
import { FinanceProvider, transactionCacheKey, useFinance } from './store';
import {
  AggregatesProvider,
  aggregateMonthKey,
  aggregateOwnersFor,
  aggregateYearKey,
  useAggregates,
} from './aggregatesStore';
import { useMonthData } from './useMonthData';
import { useYearData } from './useYearData';
import { useAggregateMonthSummary, useAggregateYearSummary } from './useAggregateSummary';
import { resolveSummaryDisplay } from './summaryResolver';
import { getUserRegistrationMonth, monthIndex } from './userRegistration';
import type { OwnerFilter, Transaction } from './types';
import { monthLabel } from './utils';
import { setUnauthorizedHandler, recordAuthDiagnostic } from './api/http';
import { RefreshProvider } from './refreshContext';
import { OfflineTransactionsProvider } from './offlineTransactionsContext';
import { ValuesVisibilityProvider, useValuesVisibility } from './valuesVisibilityContext';
import {
  clearStoredAuthSession,
  clearAuthRedirectHandoff,
  loadStoredAuthSession,
  logoutAuthSession,
  persistAuthSession,
  readAuthRedirectHandoff,
  refreshAuthSession,
  type AuthSession,
  type UserMeResponse,
} from './api/identity';
import {
  getCurrentStats,
  type UserStatsResponse,
} from './api/profile';
import {
  getCurrentPartnership,
  toMergePartnerInfo,
} from './api/partnership';
import {
  CardsApiError,
  listCards,
} from './api/cards';
import {
  FixedRulesApiError,
  listFixedRules,
} from './api/fixedRules';
import {
  TransactionsApiError,
  getTransactionTags,
  listTransactions,
  toTransaction,
} from './api/transactions';
import {
  getMyMonthAggregate,
  getMyYearAggregate,
  getPartnerMonthAggregate,
  getPartnerYearAggregate,
  type YearAggregatesResponse,
} from './api/aggregates';
import { ApiError } from './api/http';
import {
  buildAggregateLoadPlan,
  transactionMonthsForScope,
} from './appDataScope';
import {
  buildDailyRunwayMonthRefs,
  resolveDailyRunway,
  resolveDailyRunwayReferenceDate,
} from './dailyRunway';
import {
  classifyRestoreSessionError,
  restoreSessionErrorMessage,
} from './sessionRestore';

const DAILY_RUNWAY_HORIZONS = [3, 6, 9, 12] as const;
const RESTORE_SESSION_TIMEOUT_MS = 30_000;

type PeriodMode = 'monthly' | 'annual';
type Screen =
  | 'home'
  | 'profile'
  | 'fixed_transactions'
  | 'tags'
  | 'cards'
  | 'card_invoice'
  | 'merge'
  | 'simulator';
type StatsMeta = Pick<UserStatsResponse, 'source' | 'isStale'>;
type SessionLifecycle = 'checking' | 'anonymous' | 'authenticated' | 'hydrating' | 'ready' | 'degraded';
type AggregateLoadMode = 'prioritized' | 'complete' | 'criticalOnly';
type FixedRuleDraft = Pick<Transaction, 'category' | 'description' | 'amount' | 'cardId' | 'tags'>;

function Shell() {
  const now = new Date();
  const { hidden, toggle: toggleHidden } = useValuesVisibility();
  const [sessionLifecycle, setSessionLifecycle] = useState<SessionLifecycle>('checking');
  const [session, setSession] = useState<AuthSession | null>(null);
  const [currentUser, setCurrentUser] = useState<UserMeResponse | null>(null);
  const [statsMeta, setStatsMeta] = useState<StatsMeta | null>(null);
  const [statsRefreshing, setStatsRefreshing] = useState(false);
  const [statsError, setStatsError] = useState<string | null>(null);
  const [rememberSession, setRememberSession] = useState(false);
  const [restoreError, setRestoreError] = useState<string | null>(null);
  const [restoreSubmitting, setRestoreSubmitting] = useState(false);
  const [year, setYear] = useState(now.getFullYear());
  const [monthIdx, setMonthIdx] = useState(now.getMonth());
  const [period, setPeriod] = useState<PeriodMode>('monthly');
  const [screen, setScreen] = useState<Screen>('home');
  const [pendingInviteToken, setPendingInviteToken] = useState(() => inviteTokenFromLocation());
  const [menuOpen, setMenuOpen] = useState(false);
  const [invoiceCardId, setInvoiceCardId] = useState<string | null>(null);
  const [fixedRuleDraft, setFixedRuleDraft] = useState<FixedRuleDraft | null>(null);
  const {
    partner,
    mergeActive,
    ownerFilter,
    setOwnerFilter,
    setPartnership,
    clearPartnership,
    setCardsLoading,
    setCards,
    setCardsError,
    clearCards,
    setCurrentUserFinance,
    clearCurrentUserFinance,
    setTransactionsLoading,
    setTransactions,
    setTransactionsError,
    transactionLoads,
    clearTransactions,
    setFixedRulesLoading,
    setFixedRules,
    setFixedRulesError,
    clearFixedRules,
    setTagsLoading,
    setKnownTags,
    setTagsError,
    clearTags,
  } = useFinance();
  const {
    monthByKey: aggregateMonthByKey,
    yearByKey: aggregateYearByKey,
    setMonthLoading: setAggregateMonthLoading,
    setMonth: setAggregateMonth,
    setMonthError: setAggregateMonthError,
    setYearLoading: setAggregateYearLoading,
    setYear: setAggregateYear,
    setYearError: setAggregateYearError,
    setMonthsFromYear: setAggregateMonthsFromYear,
    invalidateMonths: invalidateAggregateMonths,
    invalidateYears: invalidateAggregateYears,
    clearAggregates,
  } = useAggregates();
  const [darkMode, setDarkMode] = useState(() =>
    localStorage.getItem('darkMode') === 'true',
  );
  const menuRef = useRef<HTMLDivElement>(null);
  const bootstrapStartedRef = useRef(false);
  const cardsLoadSeqRef = useRef(0);
  const fixedRulesLoadSeqRef = useRef(0);
  const tagsLoadSeqRef = useRef(0);
  const transactionsLoadSeqRef = useRef(0);
  const aggregatesLoadSeqRef = useRef(0);
  const aggregatesBackgroundTimeoutRef = useRef<number | null>(null);
  const aggregateMonthInFlightRef = useRef<Map<string, number>>(new Map());
  const aggregateYearInFlightRef = useRef<Map<string, number>>(new Map());
  const aggregateMonthByKeyRef = useRef(aggregateMonthByKey);
  const aggregateYearByKeyRef = useRef(aggregateYearByKey);
  useEffect(() => { aggregateMonthByKeyRef.current = aggregateMonthByKey; }, [aggregateMonthByKey]);
  useEffect(() => { aggregateYearByKeyRef.current = aggregateYearByKey; }, [aggregateYearByKey]);
  const hydrateControllerRef = useRef<AbortController | null>(null);
  const registration = getUserRegistrationMonth(currentUser?.registeredAt);
  const registrationMonthIndex = monthIndex(registration.year, registration.monthIdx);
  const selectedMonthIndex = monthIndex(year, monthIdx);
  const canGoPrevMonth = selectedMonthIndex > registrationMonthIndex;
  const canGoPrevYear = year > registration.year;
  const dailyRunwayReferenceDate = useMemo(
    () => resolveDailyRunwayReferenceDate(year, monthIdx),
    [year, monthIdx],
  );
  const dailyRunwayHorizons = DAILY_RUNWAY_HORIZONS;
  const maxDailyRunwayHorizon = dailyRunwayHorizons[dailyRunwayHorizons.length - 1];
  const dailyRunwayMonthRefs = useMemo(
    () => buildDailyRunwayMonthRefs(dailyRunwayReferenceDate, maxDailyRunwayHorizon),
    [dailyRunwayReferenceDate, maxDailyRunwayHorizon],
  );

  const cancelScheduledAggregateBackground = useCallback(() => {
    if (aggregatesBackgroundTimeoutRef.current == null) return;
    window.clearTimeout(aggregatesBackgroundTimeoutRef.current);
    aggregatesBackgroundTimeoutRef.current = null;
  }, []);

  const loadCards = useCallback(async (accessToken: string, signal?: AbortSignal) => {
    const seq = ++cardsLoadSeqRef.current;
    setCardsLoading();

    try {
      const response = await listCards(accessToken, { signal });
      if (seq === cardsLoadSeqRef.current) {
        setCards(response.items);
      }
    } catch (err) {
      if (isAbortError(err)) return;
      if (seq === cardsLoadSeqRef.current) {
        setCardsError(cardsErrorMessage(err));
      }
      throw err;
    }
  }, [setCards, setCardsError, setCardsLoading]);

  const loadFixedRules = useCallback(async (accessToken: string, signal?: AbortSignal) => {
    const seq = ++fixedRulesLoadSeqRef.current;
    setFixedRulesLoading();

    try {
      const response = await listFixedRules(accessToken, 'all', { signal });
      if (seq === fixedRulesLoadSeqRef.current) {
        setFixedRules(response.items);
      }
    } catch (err) {
      if (isAbortError(err)) return;
      if (seq === fixedRulesLoadSeqRef.current) {
        setFixedRulesError(fixedRulesErrorMessage(err));
      }
      throw err;
    }
  }, [setFixedRules, setFixedRulesError, setFixedRulesLoading]);

  const loadKnownTags = useCallback(async (accessToken: string, signal?: AbortSignal) => {
    const seq = ++tagsLoadSeqRef.current;
    setTagsLoading();

    try {
      const response = await getTransactionTags(accessToken, false, { signal });
      if (seq === tagsLoadSeqRef.current) {
        setKnownTags(response.tags);
      }
    } catch (err) {
      if (isAbortError(err)) return;
      if (seq === tagsLoadSeqRef.current) {
        setTagsError(tagsErrorMessage(err));
      }
      throw err;
    }
  }, [setKnownTags, setTagsError, setTagsLoading]);

  const loadTransactionsMonth = useCallback((
    accessToken: string,
    yearMonth: string,
    owner: OwnerFilter,
    seq: number,
    signal?: AbortSignal,
  ) => {
    if (!currentUser) return;

    const key = transactionCacheKey({ yearMonth, owner });
    setTransactionsLoading(key);

    void (async () => {
      const all: Transaction[] = [];
      let continuationToken: string | null = null;

      do {
        const response = await listTransactions(accessToken, {
          ym: yearMonth,
          owner,
          pageSize: 100,
          continuationToken,
          sort: 'dateAsc',
        }, { signal });
        all.push(...response.items.map((item) => toTransaction(item, {
          currentUserId: currentUser.id,
          partnerUserId: partner?.partnerUserId,
          partnerName: partner?.name,
        })));
        continuationToken = response.continuationToken;
      } while (continuationToken);

      if (seq === transactionsLoadSeqRef.current) {
        setTransactions(key, all, null);
      }
    })().catch((err) => {
      if (isAbortError(err)) return;
      if (seq === transactionsLoadSeqRef.current) {
        setTransactionsError(key, transactionsErrorMessage(err));
      }
    });
  }, [
    currentUser,
    partner?.partnerUserId,
    partner?.name,
    setTransactions,
    setTransactionsError,
    setTransactionsLoading,
  ]);

  const clearSessionState = useCallback(() => {
    hydrateControllerRef.current?.abort();
    hydrateControllerRef.current = null;
    cancelScheduledAggregateBackground();
    cardsLoadSeqRef.current += 1;
    fixedRulesLoadSeqRef.current += 1;
    tagsLoadSeqRef.current += 1;
    transactionsLoadSeqRef.current += 1;
    aggregatesLoadSeqRef.current += 1;
    aggregateMonthInFlightRef.current.clear();
    aggregateYearInFlightRef.current.clear();
    clearStoredAuthSession();
    setSession(null);
    setCurrentUser(null);
    clearCurrentUserFinance();
    setStatsMeta(null);
    setStatsError(null);
    setStatsRefreshing(false);
    setRememberSession(false);
    setRestoreError(null);
    setRestoreSubmitting(false);
    setFixedRuleDraft(null);
    clearPartnership();
    clearTransactions();
    clearCards();
    clearFixedRules();
    clearTags();
    clearAggregates();
  }, [
    cancelScheduledAggregateBackground,
    clearAggregates,
    clearCards,
    clearFixedRules,
    clearTags,
    clearCurrentUserFinance,
    clearPartnership,
    clearTransactions,
    setCurrentUser,
    setRememberSession,
    setRestoreError,
    setRestoreSubmitting,
    setSession,
    setFixedRuleDraft,
    setStatsError,
    setStatsMeta,
    setStatsRefreshing,
  ]);

  const mergeStatsIntoUser = useCallback((
    user: UserMeResponse,
    stats: UserStatsResponse,
  ): UserMeResponse => ({
    ...user,
    stats: {
      transactionsTracked: stats.transactionsTracked,
      activeMonths: stats.activeMonths,
      lastRecomputedAt: stats.lastRecomputedAt,
    },
  }), []);

  const startSession = useCallback((nextSession: AuthSession, shouldRemember: boolean) => {
    persistAuthSession(nextSession, shouldRemember);
    setSession(nextSession);
    setRememberSession(shouldRemember);
    setRestoreError(null);
    setRestoreSubmitting(false);
    setStatsError(null);
    const user = nextSession.user;
    setCurrentUserFinance(toFinanceUser(user));
    setCurrentUser(user);
    setStatsMeta({ source: 'session', isStale: false });
  }, [
    setCurrentUser,
    setRememberSession,
    setRestoreError,
    setRestoreSubmitting,
    setSession,
    setStatsError,
    setStatsMeta,
    setCurrentUserFinance,
  ]);

  const refreshSessionTokens = useCallback((nextSession: AuthSession, shouldRemember: boolean) => {
    persistAuthSession(nextSession, shouldRemember);
    setSession(nextSession);
    setRememberSession(shouldRemember);
  }, [
    setRememberSession,
    setSession,
  ]);

  const hydrateAppData = useCallback(async (accessToken: string) => {
    hydrateControllerRef.current?.abort();
    const controller = new AbortController();
    hydrateControllerRef.current = controller;
    setSessionLifecycle('hydrating');

    const results = await Promise.allSettled([
      loadCards(accessToken, controller.signal),
      loadFixedRules(accessToken, controller.signal),
      loadKnownTags(accessToken, controller.signal),
      getCurrentStats(accessToken, false, { signal: controller.signal })
        .then((stats) => {
          setStatsMeta({ source: stats.source, isStale: stats.isStale });
          setCurrentUser((user) => (user ? mergeStatsIntoUser(user, stats) : user));
        })
        .catch((err) => {
          if (isAbortError(err)) return;
          setStatsMeta(null);
          setStatsError(err instanceof Error ? err.message : 'Não foi possível carregar o resumo.');
          throw err;
        }),
      getCurrentPartnership(accessToken, { signal: controller.signal })
        .then((current) => {
          setPartnership(current.partnership ? toMergePartnerInfo(current.partnership) : null);
        })
        .catch((err) => {
          if (isAbortError(err)) return;
          throw err;
        }),
    ]);

    if (controller.signal.aborted || hydrateControllerRef.current !== controller) {
      return;
    }

    const hasFailure = results.some((result) => result.status === 'rejected');
    setSessionLifecycle(hasFailure ? 'degraded' : 'ready');
  }, [
    loadCards,
    loadFixedRules,
    loadKnownTags,
    mergeStatsIntoUser,
    setCurrentUser,
    setStatsError,
    setStatsMeta,
    setPartnership,
  ]);

  const expireSession = useCallback((reason?: string) => {
    recordAuthDiagnostic(`expireSession${reason ? ` (${reason})` : ''}`);
    clearSessionState();
    setSessionLifecycle('anonymous');
    setMenuOpen(false);
    setScreen('home');
  }, [clearSessionState, setMenuOpen, setScreen]);

  useEffect(() => {
    setUnauthorizedHandler((err) => expireSession(`401 ${err.code}`));
    return () => setUnauthorizedHandler(null);
  }, [expireSession]);

  const restoreStoredSession = useCallback(async () => {
    const redirectHandoff = readAuthRedirectHandoff();
    const stored = redirectHandoff ?? loadStoredAuthSession();
    if (!stored) {
      setRestoreError(null);
      setRestoreSubmitting(false);
      setSessionLifecycle('anonymous');
      return;
    }

    setRestoreSubmitting(true);
    setRestoreError(null);

    try {
      const refreshed = await refreshAuthSession(stored.csrfToken, {
        timeoutMs: RESTORE_SESSION_TIMEOUT_MS,
      });
      startSession(refreshed, stored.rememberSession);
      if (redirectHandoff) {
        clearAuthRedirectHandoff();
      }
      if (pendingInviteToken) {
        setScreen('merge');
      }
      void hydrateAppData(refreshed.accessToken);
    } catch (err) {
      if (classifyRestoreSessionError(err) === 'terminal') {
        if (redirectHandoff) {
          clearAuthRedirectHandoff();
        }
        clearSessionState();
        setSessionLifecycle('anonymous');
        return;
      }

      setSession(null);
      setCurrentUser(null);
      setStatsMeta(null);
      setStatsError(null);
      setSessionLifecycle('anonymous');
      setRestoreError(restoreSessionErrorMessage(err));
    } finally {
      setRestoreSubmitting(false);
    }
  }, [
    clearSessionState,
    hydrateAppData,
    pendingInviteToken,
    setCurrentUser,
    setRestoreError,
    setRestoreSubmitting,
    setScreen,
    setStatsError,
    setStatsMeta,
    startSession,
  ]);

  useEffect(() => {
    if (bootstrapStartedRef.current) return;
    bootstrapStartedRef.current = true;
    void restoreStoredSession();
  }, [restoreStoredSession]);

  useEffect(() => {
    if (!session) return;

    let cancelled = false;
    const delayMs = Math.max(30_000, (session.expiresIn - 60) * 1000);
    const timeout = window.setTimeout(async () => {
      try {
        const refreshed = await refreshAuthSession(session.csrfToken);
        if (!cancelled) {
          refreshSessionTokens(refreshed, rememberSession);
        }
      } catch {
        if (!cancelled) {
          expireSession('scheduled refresh failed');
        }
      }
    }, delayMs);

    return () => {
      cancelled = true;
      window.clearTimeout(timeout);
    };
  }, [expireSession, refreshSessionTokens, rememberSession, session]);

  useEffect(() => {
    document.documentElement.classList.toggle('dark', darkMode);
    localStorage.setItem('darkMode', String(darkMode));
  }, [darkMode]);

  useEffect(() => {
    let timeoutId: number | null = null;

    if (year < registration.year) {
      timeoutId = window.setTimeout(() => {
        setYear(registration.year);
        setMonthIdx(registration.monthIdx);
      }, 0);
      return () => {
        if (timeoutId != null) window.clearTimeout(timeoutId);
      };
    }

    if (year === registration.year && monthIdx < registration.monthIdx) {
      timeoutId = window.setTimeout(() => {
        setMonthIdx(registration.monthIdx);
      }, 0);
    }

    return () => {
      if (timeoutId != null) window.clearTimeout(timeoutId);
    };
  }, [year, monthIdx, registration.year, registration.monthIdx]);

  const loadTransactionsForActivePeriod = useCallback((signal?: AbortSignal) => {
    if (!session || !currentUser) return;

    const seq = ++transactionsLoadSeqRef.current;
    const months = transactionMonthsForScope({ year, monthIdx, period, screen });

    for (const ym of months) {
      loadTransactionsMonth(session.accessToken, ym, ownerFilter, seq, signal);
    }
  }, [
    currentUser,
    loadTransactionsMonth,
    monthIdx,
    ownerFilter,
    period,
    screen,
    session,
    year,
  ]);

  useEffect(() => {
    if (!session || !currentUser) return;
    const controller = new AbortController();
    loadTransactionsForActivePeriod(controller.signal);
    return () => controller.abort();
  }, [
    currentUser,
    loadTransactionsForActivePeriod,
    session,
  ]);

  useEffect(() => {
    if (!menuOpen) return;
    function onDoc(e: Event) {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setMenuOpen(false);
      }
    }
    document.addEventListener('mousedown', onDoc);
    document.addEventListener('touchstart', onDoc, { passive: true });
    return () => {
      document.removeEventListener('mousedown', onDoc);
      document.removeEventListener('touchstart', onDoc);
    };
  }, [menuOpen]);

  useEffect(() => {
    function onPopState() {
      const token = inviteTokenFromLocation();
      setPendingInviteToken(token);
      if (token) setScreen('merge');
    }

    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, []);

  const monthData = useMonthData(year, monthIdx);
  const yearData = useYearData(year);
  const aggregateMonthSummary = useAggregateMonthSummary(year, monthIdx + 1);
  const aggregateYearSummary = useAggregateYearSummary(year);

  // Aggregates for the runway window: previous month + months 0..12 from the selected month/day.
  // The hook list is fixed-length so calling it inline is safe.
  const runwayMonthRefM1 = dailyRunwayMonthRefs[0];
  const runwayMonthRef0 = dailyRunwayMonthRefs[1];
  const runwayMonthRef1 = dailyRunwayMonthRefs[2];
  const runwayMonthRef2 = dailyRunwayMonthRefs[3];
  const runwayMonthRef3 = dailyRunwayMonthRefs[4];
  const runwayMonthRef4 = dailyRunwayMonthRefs[5];
  const runwayMonthRef5 = dailyRunwayMonthRefs[6];
  const runwayMonthRef6 = dailyRunwayMonthRefs[7];
  const runwayMonthRef7 = dailyRunwayMonthRefs[8];
  const runwayMonthRef8 = dailyRunwayMonthRefs[9];
  const runwayMonthRef9 = dailyRunwayMonthRefs[10];
  const runwayMonthRef10 = dailyRunwayMonthRefs[11];
  const runwayMonthRef11 = dailyRunwayMonthRefs[12];
  const runwayMonthRef12 = dailyRunwayMonthRefs[13];

  const runwaySummaryM1 = useAggregateMonthSummary(runwayMonthRefM1.year, runwayMonthRefM1.monthIdx + 1);
  const runwaySummary0 = useAggregateMonthSummary(runwayMonthRef0.year, runwayMonthRef0.monthIdx + 1);
  const runwaySummary1 = useAggregateMonthSummary(runwayMonthRef1.year, runwayMonthRef1.monthIdx + 1);
  const runwaySummary2 = useAggregateMonthSummary(runwayMonthRef2.year, runwayMonthRef2.monthIdx + 1);
  const runwaySummary3 = useAggregateMonthSummary(runwayMonthRef3.year, runwayMonthRef3.monthIdx + 1);
  const runwaySummary4 = useAggregateMonthSummary(runwayMonthRef4.year, runwayMonthRef4.monthIdx + 1);
  const runwaySummary5 = useAggregateMonthSummary(runwayMonthRef5.year, runwayMonthRef5.monthIdx + 1);
  const runwaySummary6 = useAggregateMonthSummary(runwayMonthRef6.year, runwayMonthRef6.monthIdx + 1);
  const runwaySummary7 = useAggregateMonthSummary(runwayMonthRef7.year, runwayMonthRef7.monthIdx + 1);
  const runwaySummary8 = useAggregateMonthSummary(runwayMonthRef8.year, runwayMonthRef8.monthIdx + 1);
  const runwaySummary9 = useAggregateMonthSummary(runwayMonthRef9.year, runwayMonthRef9.monthIdx + 1);
  const runwaySummary10 = useAggregateMonthSummary(runwayMonthRef10.year, runwayMonthRef10.monthIdx + 1);
  const runwaySummary11 = useAggregateMonthSummary(runwayMonthRef11.year, runwayMonthRef11.monthIdx + 1);
  const runwaySummary12 = useAggregateMonthSummary(runwayMonthRef12.year, runwayMonthRef12.monthIdx + 1);

  // Loads aggregates for the active period+owner filter into the store.
  // The visible summary is loaded first; runway years are deferred so they do
  // not compete with the top-card request during month navigation.
  const loadAggregatesForActivePeriod = useCallback((
    signal?: AbortSignal,
    force = false,
    mode: AggregateLoadMode = 'prioritized',
  ) => {
    if (!session || !currentUser) return () => {};
    cancelScheduledAggregateBackground();
    const owners = aggregateOwnersFor(
      mergeActive ? ownerFilter : 'me',
      currentUser.id,
      partner?.partnerUserId ?? null,
    );
    const seq = ++aggregatesLoadSeqRef.current;
    const plan = buildAggregateLoadPlan({
      year,
      monthIdx,
      period,
      screen,
      runwayMonthRefs: dailyRunwayMonthRefs,
    });
    const targets = [owners.primary, ...(owners.secondary ? [owners.secondary] : [])];

    const unpackYearMonths = (userId: string, data: YearAggregatesResponse) => {
      const entries = data.months.map((month) => ({
        key: aggregateMonthKey(userId, month.year, month.month),
        data: month,
      }));
      if (entries.length > 0) setAggregateMonthsFromYear(entries);
    };

    const loadMonth = async (userId: string, targetYear: number, targetMonthIdx: number) => {
      const key = aggregateMonthKey(userId, targetYear, targetMonthIdx + 1);
      const cached = aggregateMonthByKeyRef.current[key];
      if (aggregateMonthInFlightRef.current.get(key) === seq) return;
      if (cached?.status === 'ready' && cached.data && !cached.data.isStale && !cached.locallyInvalidated && !force) return;
      aggregateMonthInFlightRef.current.set(key, seq);
      setAggregateMonthLoading(key);
      try {
        const data = userId === currentUser.id
          ? await getMyMonthAggregate(session.accessToken, targetYear, targetMonthIdx + 1, { signal })
          : await getPartnerMonthAggregate(session.accessToken, userId, targetYear, targetMonthIdx + 1, { signal });
        if (seq !== aggregatesLoadSeqRef.current) return;

        setAggregateMonth(key, data);
      } catch (err) {
        if (isAbortError(err)) return;
        if (seq === aggregatesLoadSeqRef.current) {
          setAggregateMonthError(key, aggregateErrorMessage(err));
        }
      } finally {
        if (aggregateMonthInFlightRef.current.get(key) === seq) {
          aggregateMonthInFlightRef.current.delete(key);
        }
      }
    };

    const loadYear = async (userId: string, targetYear: number) => {
      const key = aggregateYearKey(userId, targetYear);
      const cached = aggregateYearByKeyRef.current[key];
      if (aggregateYearInFlightRef.current.get(key) === seq) return;
      if (
        cached?.status === 'ready'
        && cached.data
        && !cached.data.months.some((month) => month.isStale)
        && !cached.locallyInvalidated
        && !force
      ) {
        unpackYearMonths(userId, cached.data);
        return;
      }
      aggregateYearInFlightRef.current.set(key, seq);
      setAggregateYearLoading(key);
      try {
        const data = userId === currentUser.id
          ? await getMyYearAggregate(session.accessToken, targetYear, { signal })
          : await getPartnerYearAggregate(session.accessToken, userId, targetYear, { signal });
        if (seq !== aggregatesLoadSeqRef.current) return;

        setAggregateYear(key, data);
        unpackYearMonths(userId, data);
      } catch (err) {
        if (isAbortError(err)) return;
        if (seq === aggregatesLoadSeqRef.current) {
          setAggregateYearError(key, aggregateErrorMessage(err));
        }
      } finally {
        if (aggregateYearInFlightRef.current.get(key) === seq) {
          aggregateYearInFlightRef.current.delete(key);
        }
      }
    };

    const loadPlan = (years: number[], months: Array<{ year: number; monthIdx: number }>) => {
      const pending: Array<Promise<void>> = [];
      if (signal?.aborted || seq !== aggregatesLoadSeqRef.current) return pending;
      for (const id of targets) {
        for (const targetYear of years) {
          pending.push(loadYear(id, targetYear));
        }
        for (const targetMonth of months) {
          pending.push(loadMonth(id, targetMonth.year, targetMonth.monthIdx));
        }
      }
      return pending;
    };

    const criticalLoads = loadPlan(plan.criticalYears, plan.criticalMonths);

    if (mode === 'criticalOnly' || plan.backgroundYears.length === 0) {
      return () => {};
    }

    let backgroundCancelled = false;
    let backgroundTimeout: number | null = null;
    const loadBackground = () => {
      if (backgroundCancelled || signal?.aborted || seq !== aggregatesLoadSeqRef.current) return;
      loadPlan(plan.backgroundYears, []);
    };

    if (mode === 'complete') {
      loadBackground();
      return () => {
        backgroundCancelled = true;
      };
    }

    const scheduleBackground = () => {
      if (backgroundCancelled || signal?.aborted || seq !== aggregatesLoadSeqRef.current) return;
      backgroundTimeout = window.setTimeout(() => {
        if (aggregatesBackgroundTimeoutRef.current === backgroundTimeout) {
          aggregatesBackgroundTimeoutRef.current = null;
        }
        loadBackground();
      }, 150);
      aggregatesBackgroundTimeoutRef.current = backgroundTimeout;
    };

    void Promise.allSettled(criticalLoads).then(scheduleBackground);

    return () => {
      backgroundCancelled = true;
      if (
        backgroundTimeout != null &&
        aggregatesBackgroundTimeoutRef.current === backgroundTimeout
      ) {
        window.clearTimeout(backgroundTimeout);
        aggregatesBackgroundTimeoutRef.current = null;
      }
    };
  }, [
    cancelScheduledAggregateBackground,
    currentUser,
    dailyRunwayMonthRefs,
    mergeActive,
    monthIdx,
    ownerFilter,
    partner?.partnerUserId,
    period,
    screen,
    session,
    setAggregateMonth,
    setAggregateMonthError,
    setAggregateMonthLoading,
    setAggregateMonthsFromYear,
    setAggregateYear,
    setAggregateYearError,
    setAggregateYearLoading,
    year,
  ]);

  // Fetch aggregates for the active period and owner filter.
  useEffect(() => {
    if (!session || !currentUser) return;
    const controller = new AbortController();
    const cancelBackground = loadAggregatesForActivePeriod(controller.signal);
    return () => {
      cancelBackground();
      controller.abort();
    };
  }, [
    loadAggregatesForActivePeriod,
    currentUser,
    session,
  ]);

  function prevMonth() {
    if (!canGoPrevMonth) return;
    if (monthIdx === 0) { setMonthIdx(11); setYear(year - 1); }
    else setMonthIdx(monthIdx - 1);
  }
  function nextMonth() {
    if (monthIdx === 11) { setMonthIdx(0); setYear(year + 1); }
    else setMonthIdx(monthIdx + 1);
  }

  const isCurrentMonthPeriod =
    period === 'monthly' && now.getFullYear() === year && now.getMonth() === monthIdx;
  const selectedYearMonth = yearMonthString(year, monthIdx);
  const summaryState = resolveSummaryDisplay({
    period,
    isCurrentMonthPeriod,
    monthData,
    yearData,
    aggregateMonth: aggregateMonthSummary,
    aggregateYear: aggregateYearSummary,
    monthTransactionLoad: transactionLoads[transactionCacheKey({
      yearMonth: selectedYearMonth,
      owner: ownerFilter,
    })],
    yearTransactionLoads: Array.from({ length: 12 }, (_, month) =>
      transactionLoads[transactionCacheKey({
        yearMonth: yearMonthString(year, month),
        owner: ownerFilter,
      })],
    ),
  });
  const dailyRunwayMonths = useMemo(
    () => [
      { ...runwayMonthRefM1, summary: runwaySummaryM1 },
      { ...runwayMonthRef0, summary: runwaySummary0 },
      { ...runwayMonthRef1, summary: runwaySummary1 },
      { ...runwayMonthRef2, summary: runwaySummary2 },
      { ...runwayMonthRef3, summary: runwaySummary3 },
      { ...runwayMonthRef4, summary: runwaySummary4 },
      { ...runwayMonthRef5, summary: runwaySummary5 },
      { ...runwayMonthRef6, summary: runwaySummary6 },
      { ...runwayMonthRef7, summary: runwaySummary7 },
      { ...runwayMonthRef8, summary: runwaySummary8 },
      { ...runwayMonthRef9, summary: runwaySummary9 },
      { ...runwayMonthRef10, summary: runwaySummary10 },
      { ...runwayMonthRef11, summary: runwaySummary11 },
      { ...runwayMonthRef12, summary: runwaySummary12 },
    ],
    [
      runwayMonthRefM1, runwayMonthRef0, runwayMonthRef1, runwayMonthRef2, runwayMonthRef3,
      runwayMonthRef4, runwayMonthRef5, runwayMonthRef6, runwayMonthRef7, runwayMonthRef8,
      runwayMonthRef9, runwayMonthRef10, runwayMonthRef11, runwayMonthRef12,
      runwaySummaryM1, runwaySummary0, runwaySummary1, runwaySummary2, runwaySummary3,
      runwaySummary4, runwaySummary5, runwaySummary6, runwaySummary7, runwaySummary8,
      runwaySummary9, runwaySummary10, runwaySummary11, runwaySummary12,
    ],
  );
  const dailyRunwayStates = useMemo(
    () => dailyRunwayHorizons.map((horizonMonths) =>
      resolveDailyRunway({
        referenceDate: dailyRunwayReferenceDate,
        horizonMonths,
        months: dailyRunwayMonths,
      }),
    ),
    [dailyRunwayHorizons, dailyRunwayReferenceDate, dailyRunwayMonths],
  );


  // Current month: the summary header mixes month-end totals (patrimônio,
  // investido and monthly flows) with today's saldo em conta snapshot.

  const isCurrentPeriod = period === 'monthly'
    ? now.getFullYear() === year && now.getMonth() === monthIdx
    : now.getFullYear() === year;
  const periodLabel = period === 'monthly' ? monthLabel(year, monthIdx) : String(year);

  const clearPendingInvite = useCallback(() => {
    setPendingInviteToken(null);
    if (inviteTokenFromLocation()) {
      window.history.replaceState(null, document.title, '/');
    }
  }, [setPendingInviteToken]);

  const handleAuthenticated = useCallback((nextSession: AuthSession, shouldRemember: boolean) => {
    setRestoreError(null);
    setRestoreSubmitting(false);
    setScreen(pendingInviteToken ? 'merge' : 'home');
    startSession(nextSession, shouldRemember);
    void hydrateAppData(nextSession.accessToken).catch((err) => {
      const code = err instanceof ApiError ? `${err.status} ${err.code}` : 'hydrate error';
      expireSession(`hydrate: ${code}`);
    });
  }, [
    expireSession,
    hydrateAppData,
    pendingInviteToken,
    setRestoreError,
    setRestoreSubmitting,
    setScreen,
    startSession,
  ]);

  const handleLogout = useCallback(() => {
    const accessToken = session?.accessToken;
    expireSession();
    if (accessToken) {
      void logoutAuthSession(accessToken).catch(() => {});
    }
  }, [expireSession, session?.accessToken]);

  const invalidateAggregatesForYearMonths = useCallback((
    yearMonths: string[],
    userIds: string[],
  ) => {
    const parsed = yearMonths
      .map(parseYearMonthString)
      .filter((value): value is { year: number; month: number } => value != null);
    if (parsed.length === 0 || userIds.length === 0) return;

    invalidateAggregateMonths(userIds.flatMap((userId) =>
      parsed.map(({ year: targetYear, month }) => aggregateMonthKey(userId, targetYear, month)),
    ));
    invalidateAggregateYears(userIds.flatMap((userId) =>
      [...new Set(parsed.map(({ year: targetYear }) => targetYear))]
        .map((targetYear) => aggregateYearKey(userId, targetYear)),
    ));
  }, [invalidateAggregateMonths, invalidateAggregateYears]);

  const refreshFinancialAfterMutation = useCallback((yearMonths?: string[]) => {
    if (!session || !currentUser) return;
    const affectedYearMonths = yearMonths && yearMonths.length > 0
      ? yearMonths
      : [selectedYearMonth];
    invalidateAggregatesForYearMonths(affectedYearMonths, [currentUser.id]);
    loadAggregatesForActivePeriod(undefined, true, 'criticalOnly');
  }, [
    currentUser,
    invalidateAggregatesForYearMonths,
    loadAggregatesForActivePeriod,
    selectedYearMonth,
    session,
  ]);

  const refreshProfileStats = useCallback(async (fresh = true) => {
    if (!session || !currentUser) return;

    setStatsRefreshing(true);
    setStatsError(null);
    try {
      const stats = await getCurrentStats(session.accessToken, fresh);
      setStatsMeta({ source: stats.source, isStale: stats.isStale });
      setCurrentUser((user) => (user ? mergeStatsIntoUser(user, stats) : user));
    } catch (err) {
      setStatsError(err instanceof Error ? err.message : 'Não foi possível atualizar o resumo.');
    } finally {
      setStatsRefreshing(false);
    }
  }, [
    currentUser,
    mergeStatsIntoUser,
    session,
    setCurrentUser,
    setStatsError,
    setStatsMeta,
    setStatsRefreshing,
  ]);

  const refreshAll = useCallback(() => {
    if (!session || !currentUser) return;
    loadAggregatesForActivePeriod(undefined, true, 'complete');
    loadTransactionsForActivePeriod();
    void loadCards(session.accessToken).catch(() => {});
    void loadFixedRules(session.accessToken).catch(() => {});
    void loadKnownTags(session.accessToken).catch(() => {});
    void getCurrentPartnership(session.accessToken)
      .then((current) => {
        setPartnership(current.partnership ? toMergePartnerInfo(current.partnership) : null);
      })
      .catch(() => {});
    void refreshProfileStats(true).catch(() => {});
  }, [
    currentUser,
    loadAggregatesForActivePeriod,
    loadCards,
    loadFixedRules,
    loadKnownTags,
    loadTransactionsForActivePeriod,
    refreshProfileStats,
    session,
    setPartnership,
  ]);
  const activeAggregateSummary = period === 'monthly'
    ? aggregateMonthSummary
    : aggregateYearSummary;
  const refreshing =
    activeAggregateSummary.status === 'loading' ||
    activeAggregateSummary.status === 'updating';

  if (sessionLifecycle === 'checking') {
    return (
      <div className="min-h-screen w-full flex justify-center" style={{ background: 'rgb(var(--bg-app))' }}>
        <div className="w-full max-w-md min-h-screen bg-paper flex flex-col items-center justify-center gap-3">
          <BrandMark />
          <div className="text-xs text-ink-muted">Carregando sessão...</div>
        </div>
      </div>
    );
  }

  if (!session || sessionLifecycle === 'anonymous') {
    return (
      <div className="min-h-screen w-full flex justify-center" style={{ background: 'rgb(var(--bg-app))' }}>
        <div className="w-full max-w-md min-h-screen bg-paper flex flex-col relative">
          <AuthView
            onAuthenticated={handleAuthenticated}
            restoreError={restoreError}
            restoreSubmitting={restoreSubmitting}
            onRetryRestore={restoreStoredSession}
          />
        </div>
      </div>
    );
  }

  return (
    <RefreshProvider value={{ refreshAll, refreshing }}>
    <OfflineTransactionsProvider accessToken={session.accessToken} onRemoteCommit={refreshFinancialAfterMutation}>
    <div className="min-h-screen w-full flex justify-center" style={{ background: 'rgb(var(--bg-app))' }}>
      <div className="w-full max-w-md sm:max-w-lg md:max-w-3xl lg:max-w-5xl min-h-screen bg-paper flex flex-col relative">
        {/* Top bar */}
        <header className="glass-header sticky top-0 z-30 px-4 sm:px-5 md:px-8 lg:px-10 pt-4 sm:pt-5 pb-3 grid grid-cols-[1fr_auto_1fr] items-center gap-2">
          <div className="justify-self-start inline-flex items-center gap-2 text-sm font-semibold tracking-tight text-ink">
            <BrandMark />
            <span className="hidden min-[340px]:inline">Merge Duo</span>
          </div>
          {/* Period pill (centered) */}
          <div className="justify-self-center inline-flex p-0.5 rounded-full bg-paper-card border border-paper-line shadow-soft-sm">
            <PeriodBtn label="Mensal" active={period === 'monthly'} onClick={() => setPeriod('monthly')} />
            <PeriodBtn label="Anual"  active={period === 'annual'}  onClick={() => setPeriod('annual')}  />
          </div>
          <div ref={menuRef} className="justify-self-end flex items-center gap-0.5 relative">
            <button
              type="button"
              onClick={toggleHidden}
              className="w-9 h-9 rounded-full grid place-items-center tap-surface text-ink-muted hover:bg-paper-line"
              aria-label={hidden ? 'Mostrar valores' : 'Ocultar valores'}
              title={hidden ? 'Mostrar valores' : 'Ocultar valores'}
            >
              {hidden ? <IconEyeOff /> : <IconEye />}
            </button>
            <button
              onClick={() => setMenuOpen((v) => !v)}
              className={`w-9 h-9 rounded-full grid place-items-center tap-surface ${
                menuOpen ? 'bold-surface' : 'text-ink-muted hover:bg-paper-line'
              }`}
              aria-label="Mais opções"
              aria-expanded={menuOpen}
            >
              <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="5" r="1.2" fill="currentColor" stroke="none"/>
                <circle cx="12" cy="12" r="1.2" fill="currentColor" stroke="none"/>
                <circle cx="12" cy="19" r="1.2" fill="currentColor" stroke="none"/>
              </svg>
            </button>
            {menuOpen && (
              <div className="absolute right-0 top-11 w-[min(14rem,calc(100vw-2rem))] rounded-2xl bg-paper-card border border-paper-line shadow-elevated overflow-hidden z-20 animate-scale-in">
                <MenuItem
                  label="Lançamentos fixos"
                  icon={<IconRepeat />}
                  onClick={() => { setScreen('fixed_transactions'); setMenuOpen(false); }}
                />
                <MenuItem
                  label="Tags"
                  icon={<IconTag />}
                  onClick={() => { setScreen('tags'); setMenuOpen(false); }}
                />
                <MenuItem
                  label="Simulador"
                  icon={<IconChart />}
                  onClick={() => { setScreen('simulator'); setMenuOpen(false); }}
                />
                <div className="h-px bg-paper-line mx-3" />
                <MenuItem
                  label="Sair"
                  icon={<IconLogout />}
                  danger
                  onClick={handleLogout}
                />
              </div>
            )}
          </div>
        </header>

        <OfflineBanner />

        <React.Suspense fallback={null}>
        {screen === 'profile' ? (
          <ProfileView
            user={currentUser}
            accessToken={session.accessToken}
            onUserChanged={setCurrentUser}
            statsMeta={statsMeta}
            statsRefreshing={statsRefreshing}
            statsError={statsError}
            onRefreshStats={() => refreshProfileStats(true)}
            onBack={() => setScreen('home')}
            darkMode={darkMode}
            onToggleDark={() => setDarkMode((v) => !v)}
          />
        ) : screen === 'fixed_transactions' ? (
          <FixedTransactionsView
            accessToken={session.accessToken}
            onBack={() => setScreen('home')}
            initialDraft={fixedRuleDraft}
            onInitialDraftConsumed={() => setFixedRuleDraft(null)}
          />
        ) : screen === 'tags' ? (
          <TagsView
            accessToken={session.accessToken}
            onBack={() => setScreen('home')}
          />
        ) : screen === 'cards' ? (
          <CardsView
            accessToken={session.accessToken}
            onBack={() => setScreen('home')}
            onOpenInvoice={(id) => { setInvoiceCardId(id); setScreen('card_invoice'); }}
          />
        ) : screen === 'card_invoice' && invoiceCardId ? (
          <CardInvoiceView
            accessToken={session.accessToken}
            cardId={invoiceCardId}
            onBack={() => setScreen('cards')}
          />
        ) : screen === 'merge' ? (
          <MergeView
            accessToken={session.accessToken}
            pendingInviteToken={pendingInviteToken}
            onInviteHandled={clearPendingInvite}
            onBack={() => {
              if (pendingInviteToken) clearPendingInvite();
              setScreen('home');
            }}
          />
        ) : screen === 'simulator' ? (
          <SimulatorView
            year={year}
            monthIdx={monthIdx}
            onBack={() => setScreen('home')}
          />
        ) : (
          <>
            {period === 'monthly' ? (
              <MonthNavigator year={year} monthIdx={monthIdx} onPrev={prevMonth} onNext={nextMonth} canGoPrev={canGoPrevMonth} />
            ) : (
              <YearNavigator year={year} onPrev={() => canGoPrevYear && setYear(year - 1)} onNext={() => setYear(year + 1)} canGoPrev={canGoPrevYear} />
            )}

            <SummaryHeader
              status={summaryState.status}
              error={summaryState.error}
              patrimonio={summaryState.patrimonio}
              saldo={summaryState.saldo}
              investido={summaryState.investido}
              dailyRunwayStates={dailyRunwayStates}
              isProjected={summaryState.isProjected}
              mesEntradas={summaryState.entradas}
              mesSaidas={summaryState.saidas}
              mesAportes={summaryState.aportes}
              period={period}
              periodLabel={periodLabel}
              isCurrentPeriod={isCurrentPeriod}
              onRefresh={refreshAll}
              refreshing={refreshing}
            />
            {mergeActive && partner && (
              <div className="mx-4 mb-2 sm:mx-5 md:mx-8 lg:mx-10 flex flex-wrap items-center gap-2">
                <button
                  onClick={() => setScreen('merge')}
                  className="flex items-center gap-2 px-3 h-8 rounded-full bg-accent-invest/10 border border-accent-invest/20 text-[11px] text-accent-invest font-medium tap-surface"
                >
                  <span className="w-1.5 h-1.5 rounded-full bg-accent-invest animate-pulse" />
                  Merge ativo com {partner.name}
                </button>
                <OwnerFilterPill
                  value={ownerFilter}
                  onChange={setOwnerFilter}
                  partnerFirstName={partner.name.split(' ')[0]}
                />
              </div>
            )}

            <main className="flex-1 pb-bottom-nav">
              {period === 'annual' ? (
                <AnnualView year={year} />
              ) : (
                <DailyList
                  accessToken={session.accessToken}
                  year={year}
                  monthIdx={monthIdx}
                  onNavigateToCards={() => setScreen('cards')}
                  onTransactionMutated={refreshFinancialAfterMutation}
                  onCreateFixedFromTransaction={(tx) => {
                    setFixedRuleDraft({
                      category: tx.category,
                      description: tx.description,
                      amount: tx.amount,
                      cardId: tx.cardId,
                      tags: tx.tags,
                    });
                    setScreen('fixed_transactions');
                  }}
                />
              )}
            </main>
          </>
        )}
        </React.Suspense>

        {/* Bottom tab bar */}
        <nav
          className="glass-nav fixed bottom-0 left-1/2 -translate-x-1/2 z-40 flex w-full max-w-md sm:max-w-lg md:max-w-3xl lg:max-w-5xl"
          style={{ height: 'calc(var(--bottom-nav-h) + env(safe-area-inset-bottom, 0px))' }}
          aria-label="Navegação principal"
        >
          <div className="flex w-full px-2" style={{ height: 'var(--bottom-nav-h)' }}>
            <BottomTab
              label="Início"
              active={screen === 'home' || screen === 'simulator'}
              onClick={() => setScreen('home')}
              icon={
                <svg aria-hidden="true" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M3 12L12 3l9 9"/>
                  <path d="M9 21V12h6v9"/>
                  <path d="M3 12v9h5M16 21h5V12"/>
                </svg>
              }
            />
            <BottomTab
              label="Cartões"
              active={screen === 'cards' || screen === 'card_invoice'}
              onClick={() => setScreen('cards')}
              icon={
                <svg aria-hidden="true" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                  <rect x="2" y="5" width="20" height="14" rx="3"/>
                  <line x1="2" y1="10" x2="22" y2="10"/>
                </svg>
              }
            />
            <BottomTab
              label="Merge"
              active={screen === 'merge'}
              onClick={() => setScreen('merge')}
              showDot={mergeActive}
              icon={
                <svg aria-hidden="true" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="6" cy="6" r="3"/>
                  <circle cx="6" cy="18" r="3"/>
                  <path d="M6 9v6"/>
                  <path d="M20 12h-5a4 4 0 0 1-4-4V6"/>
                  <path d="M20 12h-5a4 4 0 0 0-4 4v2"/>
                </svg>
              }
            />
            <BottomTab
              label="Perfil"
              active={screen === 'profile'}
              onClick={() => setScreen('profile')}
              icon={
                <svg aria-hidden="true" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="12" cy="8" r="4"/>
                  <path d="M4 20c0-4 4-7 8-7s8 3 8 7"/>
                </svg>
              }
            />
          </div>
        </nav>
      </div>
    </div>
    </OfflineTransactionsProvider>
    </RefreshProvider>
  );
}

function BottomTab({
  label,
  active,
  onClick,
  icon,
  showDot = false,
}: {
  label: string;
  active: boolean;
  onClick: () => void;
  icon: React.ReactNode;
  showDot?: boolean;
}) {
  return (
    <button
      onClick={onClick}
      className={`relative flex flex-1 flex-col items-center justify-center gap-0.5 tap-surface rounded-xl transition-colors ${
        active ? 'text-accent-invest' : 'text-ink-muted hover:text-ink-soft'
      }`}
      aria-label={label}
      aria-current={active ? 'page' : undefined}
    >
      <div className={`relative transition-transform duration-300 ${active ? 'scale-110 -translate-y-0.5' : 'scale-100'}`} style={{ transitionTimingFunction: 'cubic-bezier(0.34, 1.56, 0.64, 1)' }}>
        {icon}
        {showDot && (
          <span className="absolute -top-0.5 -right-0.5 w-2 h-2 rounded-full bg-accent-neg border-2 border-paper-card" />
        )}
      </div>
      <span className={`text-[10px] font-semibold tracking-wide transition-colors ${active ? 'text-accent-invest' : 'text-ink-muted'}`}>
        {label}
      </span>
    </button>
  );
}

function PeriodBtn({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className={`px-3 h-7 rounded-full text-[11px] font-medium tracking-wide transition ${
        active ? 'bold-surface' : 'text-ink-muted hover:text-ink'
      }`}
    >
      {label}
    </button>
  );
}

function MenuItem({
  label, icon, onClick, danger = false,
}: {
  label: string;
  icon: React.ReactNode;
  onClick: () => void;
  danger?: boolean;
}) {
  return (
    <button
      onClick={onClick}
      className={`w-full flex items-center gap-3 px-4 h-11 text-left text-sm font-medium transition tap-surface ${
        danger ? 'text-accent-neg hover:bg-accent-neg/8' : 'text-ink hover:bg-paper-line'
      }`}
    >
      <span className={`shrink-0 ${danger ? 'text-accent-neg' : 'text-ink-muted'}`}>{icon}</span>
      {label}
    </button>
  );
}

/* Menu icons */
const MS = { width: 16, height: 16, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const, 'aria-hidden': true as const };
function IconRepeat() { return <svg {...MS}><path d="M17 1l4 4-4 4"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><path d="M7 23l-4-4 4-4"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/></svg>; }
function IconTag()    { return <svg {...MS}><path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z"/><circle cx="7" cy="7" r="1"/></svg>; }
function IconChart() { return <svg {...MS}><path d="M3 3v18h18" /><path d="M7 14l4-4 4 4 5-6" /></svg>; }
function IconLogout() { return <svg {...MS}><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/></svg>; }
function IconEye() { return <svg {...MS}><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>; }
function IconEyeOff() { return <svg {...MS}><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>; }

function OwnerFilterPill({
  value,
  onChange,
  partnerFirstName,
}: {
  value: OwnerFilter;
  onChange: (v: OwnerFilter) => void;
  partnerFirstName: string;
}) {
  const opts: { v: OwnerFilter; label: string }[] = [
    { v: 'me', label: 'Você' },
    { v: 'partner', label: partnerFirstName },
    { v: 'both', label: 'Ambos' },
  ];
  return (
    <div className="inline-flex p-0.5 rounded-full bg-paper-card border border-paper-line shadow-soft-sm">
      {opts.map((o) => (
        <button
          key={o.v}
          onClick={() => onChange(o.v)}
          className={`px-3 h-7 rounded-full text-[11px] font-medium transition ${
            value === o.v ? 'bold-surface' : 'text-ink-muted hover:text-ink'
          }`}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

function inviteTokenFromLocation(): string | null {
  if (typeof window === 'undefined') return null;

  const match = window.location.pathname.match(/^\/invites\/([^/?#]+)\/?$/);
  return match?.[1] ? decodeURIComponent(match[1]) : null;
}

function toFinanceUser(user: UserMeResponse) {
  return {
    id: user.id,
    name: user.name,
    registeredAt: user.registeredAt,
    startingBalance: user.financial.startingBalance,
  };
}

function yearMonthString(year: number, monthIdx: number) {
  return `${year}-${String(monthIdx + 1).padStart(2, '0')}`;
}

function parseYearMonthString(value: string): { year: number; month: number } | null {
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  if (!match) return null;

  const year = Number(match[1]);
  const month = Number(match[2]);
  if (!Number.isInteger(year) || !Number.isInteger(month) || month < 1 || month > 12) {
    return null;
  }

  return { year, month };
}

function transactionsErrorMessage(err: unknown) {
  if (err instanceof TransactionsApiError) {
    if (err.code === 'unauthorized') return 'Sua sessão expirou. Entre novamente.';
    if (err.code === 'rate_limited') return 'Muitas tentativas. Aguarde um pouco e tente de novo.';
    if (err.code === 'transactions_dependency_unavailable') {
      return 'Não foi possível carregar os lançamentos agora.';
    }
    return err.message || 'Não foi possível carregar os lançamentos.';
  }

  return err instanceof Error ? err.message : 'Não foi possível carregar os lançamentos.';
}

function cardsErrorMessage(err: unknown) {
  if (err instanceof CardsApiError) {
    if (err.code === 'unauthorized') return 'Sua sessão expirou. Entre novamente.';
    if (err.code === 'rate_limited') return 'Muitas tentativas. Aguarde um pouco e tente de novo.';
    if (err.code === 'cards_dependency_unavailable') return 'Não foi possível carregar os cartões agora.';
    return err.message || 'Não foi possível carregar os cartões.';
  }

  return err instanceof Error ? err.message : 'Não foi possível carregar os cartões.';
}

function fixedRulesErrorMessage(err: unknown) {
  if (err instanceof FixedRulesApiError) {
    if (err.code === 'unauthorized') return 'Sua sessão expirou. Entre novamente.';
    if (err.code === 'rate_limited') return 'Muitas tentativas. Aguarde um pouco e tente de novo.';
    if (err.code === 'fixed_rules_dependency_unavailable') return 'Não foi possível carregar os lançamentos fixos agora.';
    return err.message || 'Não foi possível carregar os lançamentos fixos.';
  }

  return err instanceof Error ? err.message : 'Não foi possível carregar os lançamentos fixos.';
}

function tagsErrorMessage(err: unknown) {
  if (err instanceof TransactionsApiError) {
    if (err.code === 'unauthorized') return 'Sua sessão expirou. Entre novamente.';
    if (err.code === 'rate_limited') return 'Muitas tentativas. Aguarde um pouco e tente de novo.';
    if (err.code === 'transactions_dependency_unavailable') return 'Não foi possível carregar as tags agora.';
    return err.message || 'Não foi possível carregar as tags.';
  }

  return err instanceof Error ? err.message : 'Não foi possível carregar as tags.';
}

function aggregateErrorMessage(err: unknown) {
  if (err instanceof ApiError) {
    if (err.status === 401) return 'Sua sessão expirou. Entre novamente.';
    if (err.status === 429) return 'Muitas tentativas. Aguarde um pouco e tente de novo.';
    return err.message || 'Não foi possível carregar o resumo financeiro.';
  }
  return err instanceof Error ? err.message : 'Não foi possível carregar o resumo financeiro.';
}

function isAbortError(err: unknown) {
  return (
    (err instanceof DOMException && err.name === 'AbortError') ||
    (err instanceof Error && err.name === 'AbortError')
  );
}

class ErrorBoundary extends React.Component<{ children: React.ReactNode }, { error: Error | null }> {
  constructor(props: { children: React.ReactNode }) {
    super(props);
    this.state = { error: null };
  }

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  override render() {
    if (this.state.error) {
      return (
        <div className="min-h-screen w-full flex justify-center" style={{ background: 'rgb(var(--bg-app))' }}>
          <div className="w-full max-w-md min-h-screen bg-paper flex flex-col items-center justify-center gap-4 px-6 text-center">
            <BrandMark />
            <p className="text-sm text-ink font-medium">Algo deu errado</p>
            <p className="text-xs text-ink-muted">
              Ocorreu um erro inesperado. Tente recarregar o aplicativo.
            </p>
            <button
              onClick={() => window.location.reload()}
              className="mt-2 px-5 h-11 rounded-2xl bg-ink text-paper text-sm font-medium"
            >
              Recarregar
            </button>
          </div>
        </div>
      );
    }
    return this.props.children;
  }
}

export default function App() {
  return (
    <ErrorBoundary>
      <ValuesVisibilityProvider>
        <FinanceProvider>
          <AggregatesProvider>
            <Shell />
          </AggregatesProvider>
        </FinanceProvider>
      </ValuesVisibilityProvider>
    </ErrorBoundary>
  );
}
