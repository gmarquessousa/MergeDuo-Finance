import type { DayData } from './useMonthData';
import type { AggregateSummary } from './useAggregateSummary';

interface ResolveDailyTotalModeInput {
  year: number;
  monthIdx: number;
  perDay: DayData[];
  totalAcumulado: number;
  aggregateSummary: AggregateSummary;
  canUseAggregateCorrection: boolean;
}

export interface DailyTotalModeState {
  ready: boolean;
  balancesByDay: Map<number, number>;
}

export function resolveDailyTotalMode({
  year,
  monthIdx,
  perDay,
  totalAcumulado,
  aggregateSummary,
  canUseAggregateCorrection,
}: ResolveDailyTotalModeInput): DailyTotalModeState {
  if (canUseAggregateCorrection && aggregateSummary.dailyBalances && aggregateSummary.dailyBalances.length > 0) {
    return {
      ready: true,
      balancesByDay: new Map(
        aggregateSummary.dailyBalances.map((balance) => [balance.day, balance.saldo]),
      ),
    };
  }

  const today = new Date();
  const isCurrentMonth = today.getFullYear() === year && today.getMonth() === monthIdx;
  const isFutureMonth =
    year > today.getFullYear() || (year === today.getFullYear() && monthIdx > today.getMonth());
  const todayDayIdx = isCurrentMonth ? today.getDate() - 1 : null;
  const localAccToday = todayDayIdx != null ? perDay[todayDayIdx]?.accumulated ?? null : null;

  let aggregateCorrection = 0;
  let correctionReady = false;
  if (
    canUseAggregateCorrection &&
    isCurrentMonth &&
    aggregateSummary.snapshotToday != null &&
    localAccToday != null
  ) {
    aggregateCorrection = aggregateSummary.snapshotToday.saldoHoje - localAccToday;
    correctionReady = true;
  } else if (
    canUseAggregateCorrection &&
    !isCurrentMonth &&
    !isFutureMonth &&
    aggregateSummary.totals != null
  ) {
    aggregateCorrection = aggregateSummary.totals.saldo - totalAcumulado;
    correctionReady = true;
  }

  if (!correctionReady) {
    return { ready: false, balancesByDay: new Map() };
  }

  return {
    ready: true,
    balancesByDay: new Map(
      perDay.map((row) => [row.day, row.accumulated + aggregateCorrection]),
    ),
  };
}
