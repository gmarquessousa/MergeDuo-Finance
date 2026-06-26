import { ApiError, apiFetch, type RequestControlOptions } from './http';
import { appConfig } from './config';

export const aggregatesApiBaseUrl = appConfig.aggregatesApiBaseUrl;

export type AggregateSource = 'cache' | 'recomputed' | 'cold_start' | string;

export interface MonthlyTotalsResponse {
  entradas: number;
  saidas: number;
  aportes: number;
  saldo: number;
  investido: number;
}

export interface OwnerTotalsResponse {
  entradas: number;
  saidas: number;
  aportes: number;
}

export interface SnapshotTodayResponse {
  saldoHoje: number;
  investidoHoje: number;
  patrimonioHoje: number;
  asOfDate: string;
}

export interface DailyBalanceResponse {
  day: number;
  saldo: number;
}

export interface DailyMovementResponse {
  day: number;
  id: string;
  userId: string;
  category: string;
  kind: string;
  description: string;
  amount: number;
  cardId: string | null;
  fixedRuleId: string | null;
  projected: boolean;
  purchaseDate: string | null;
}

export interface ProjectionResponse {
  includesProjected: boolean;
  projectedCount: number;
  asOfDate: string;
}

export interface FreshnessResponse {
  state: 'fresh' | 'stale' | string;
  reason: string | null;
}

export interface SourceWatermarkResponse {
  maxTransactionUpdatedAt: string | null;
  activeTransactionsCount: number;
}

export interface MonthlyAggregateResponse {
  id: string;
  userId: string;
  year: number;
  month: number;
  monthIdx: number;
  yearMonth: string;
  totals: MonthlyTotalsResponse;
  snapshotToday: SnapshotTodayResponse | null;
  dailyBalances: DailyBalanceResponse[];
  dailyMovements: DailyMovementResponse[];
  projection: ProjectionResponse;
  byCategory: Record<string, number>;
  byCard: Record<string, number>;
  byOwner: Record<string, OwnerTotalsResponse>;
  transactionsCount: number;
  computedAt: string | null;
  sourceVersion: number;
  isStale: boolean;
  source: AggregateSource;
  freshness?: FreshnessResponse;
  sourceWatermark?: SourceWatermarkResponse;
}

export interface YearAggregatesResponse {
  userId: string;
  year: number;
  months: MonthlyAggregateResponse[];
}

/** @deprecated Prefer the shared {@link ApiError} from `./http`; kept for compatibility. */
export class AggregatesApiError extends ApiError {
  constructor(status: number, code: string, message: string) {
    super(status, code, message);
    this.name = 'AggregatesApiError';
  }
}

export async function getMyMonthAggregate(
  accessToken: string,
  year: number,
  month: number,
  options?: RequestControlOptions,
): Promise<MonthlyAggregateResponse> {
  return request<MonthlyAggregateResponse>({
    path: `/aggregates/me/${year}/${month}`,
    method: 'GET',
    accessToken,
    ...options,
  });
}

export async function getMyYearAggregate(
  accessToken: string,
  year: number,
  options?: RequestControlOptions,
): Promise<YearAggregatesResponse> {
  return request<YearAggregatesResponse>({
    path: `/aggregates/me/year/${year}`,
    method: 'GET',
    accessToken,
    ...options,
  });
}

export async function getPartnerMonthAggregate(
  accessToken: string,
  userId: string,
  year: number,
  month: number,
  options?: RequestControlOptions,
): Promise<MonthlyAggregateResponse> {
  return request<MonthlyAggregateResponse>({
    path: `/aggregates/${encodeURIComponent(userId)}/${year}/${month}`,
    method: 'GET',
    accessToken,
    ...options,
  });
}

export async function getPartnerYearAggregate(
  accessToken: string,
  userId: string,
  year: number,
  options?: RequestControlOptions,
): Promise<YearAggregatesResponse> {
  return request<YearAggregatesResponse>({
    path: `/aggregates/${encodeURIComponent(userId)}/year/${year}`,
    method: 'GET',
    accessToken,
    ...options,
  });
}

export async function recomputeMyMonthAggregate(
  accessToken: string,
  year: number,
  month: number,
  options?: RequestControlOptions,
): Promise<void> {
  await request<unknown>({
    path: `/aggregates/me/backfill/${year}/${month}`,
    method: 'POST',
    accessToken,
    ...options,
  });
}

export async function recomputeMyYearAggregate(
  accessToken: string,
  year: number,
  options?: RequestControlOptions,
): Promise<void> {
  await request<unknown>({
    path: `/aggregates/me/backfill/${year}`,
    method: 'POST',
    accessToken,
    ...options,
  });
}

interface AggregatesRequestOptions {
  path: string;
  method: string;
  accessToken?: string;
  signal?: AbortSignal;
  timeoutMs?: number;
}

function request<T>(options: AggregatesRequestOptions): Promise<T> {
  return apiFetch<T>({
    baseUrl: aggregatesApiBaseUrl,
    defaultErrorCode: 'aggregates_api_error',
    ...options,
  });
}

export function combineMonthAggregates(
  a: MonthlyAggregateResponse,
  b: MonthlyAggregateResponse,
): CombinedMonthSummary {
  return {
    yearMonth: a.yearMonth,
    totals: {
      entradas: a.totals.entradas + b.totals.entradas,
      saidas: a.totals.saidas + b.totals.saidas,
      aportes: a.totals.aportes + b.totals.aportes,
      saldo: a.totals.saldo + b.totals.saldo,
      investido: a.totals.investido + b.totals.investido,
    },
    snapshotToday:
      a.snapshotToday && b.snapshotToday
        ? {
            saldoHoje: a.snapshotToday.saldoHoje + b.snapshotToday.saldoHoje,
            investidoHoje: a.snapshotToday.investidoHoje + b.snapshotToday.investidoHoje,
            patrimonioHoje: a.snapshotToday.patrimonioHoje + b.snapshotToday.patrimonioHoje,
            asOfDate: a.snapshotToday.asOfDate,
        }
        : a.snapshotToday ?? b.snapshotToday ?? null,
    dailyBalances: combineDailyBalances(a.dailyBalances, b.dailyBalances),
    dailyMovements: combineDailyMovements(a.dailyMovements, b.dailyMovements),
    projection: {
      includesProjected: a.projection.includesProjected || b.projection.includesProjected,
      projectedCount: a.projection.projectedCount + b.projection.projectedCount,
      asOfDate: a.projection.asOfDate,
    },
    isStale: a.isStale || b.isStale,
    source: a.source === b.source ? a.source : `${a.source}+${b.source}`,
    computedAt: pickEarlier(a.computedAt, b.computedAt),
  };
}

export interface CombinedMonthSummary {
  yearMonth: string;
  totals: MonthlyTotalsResponse;
  snapshotToday: SnapshotTodayResponse | null;
  dailyBalances: DailyBalanceResponse[];
  dailyMovements: DailyMovementResponse[];
  projection: ProjectionResponse;
  isStale: boolean;
  source: string;
  computedAt: string | null;
}

export interface CombinedYearSummary {
  year: number;
  totals: { entradas: number; saidas: number; aportes: number };
  endOfYearSaldo: number;
  endOfYearInvestido: number;
  isStale: boolean;
  source: string;
  includesProjected: boolean;
}

export function combineYearAggregates(
  a: YearAggregatesResponse,
  b: YearAggregatesResponse | null,
): CombinedYearSummary {
  const months = a.months.map((monthA) => {
    const monthB = b?.months.find((m) => m.month === monthA.month) ?? null;
    return monthB ? combineMonthAggregates(monthA, monthB) : monthA;
  });

  let entradas = 0;
  let saidas = 0;
  let aportes = 0;
  let saldo = 0;
  let investido = 0;
  let isStale = false;
  let source = '';
  let includesProjected = false;

  for (const month of months) {
    if ('totals' in month) {
      entradas += month.totals.entradas;
      saidas += month.totals.saidas;
      aportes += month.totals.aportes;
      saldo = month.totals.saldo;
      investido = month.totals.investido;
      isStale = isStale || (month as { isStale: boolean }).isStale;
      source = (month as { source: string }).source;
      includesProjected = includesProjected || (
        'projection' in month &&
        Boolean((month as { projection?: ProjectionResponse }).projection?.includesProjected)
      );
    }
  }

  return {
    year: a.year,
    totals: { entradas, saidas, aportes },
    endOfYearSaldo: saldo,
    endOfYearInvestido: investido,
    isStale,
    source,
    includesProjected,
  };
}

function pickEarlier(a: string | null, b: string | null): string | null {
  if (!a) return b;
  if (!b) return a;
  return a < b ? a : b;
}

function combineDailyBalances(
  left: DailyBalanceResponse[] | undefined,
  right: DailyBalanceResponse[] | undefined,
): DailyBalanceResponse[] {
  const totalsByDay = new Map<number, number>();

  for (const balance of left ?? []) {
    totalsByDay.set(balance.day, (totalsByDay.get(balance.day) ?? 0) + balance.saldo);
  }

  for (const balance of right ?? []) {
    totalsByDay.set(balance.day, (totalsByDay.get(balance.day) ?? 0) + balance.saldo);
  }

  return [...totalsByDay.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([day, saldo]) => ({ day, saldo }));
}

function combineDailyMovements(
  left: DailyMovementResponse[] | undefined,
  right: DailyMovementResponse[] | undefined,
): DailyMovementResponse[] {
  return [...(left ?? []), ...(right ?? [])].sort((a, b) =>
    a.day - b.day || Number(a.projected) - Number(b.projected) || a.id.localeCompare(b.id),
  );
}
