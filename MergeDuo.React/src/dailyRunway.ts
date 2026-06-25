import type { AggregateSummary } from './useAggregateSummary';
import { daysInMonth } from './utils';

export interface DailyRunwayMonthRef {
  year: number;
  monthIdx: number;
}

export interface DailyRunwayMonthInput extends DailyRunwayMonthRef {
  summary: AggregateSummary;
}

export interface DailyRunwayState {
  ready: boolean;
  /** Per-day safe spend: minProjectedTotal / horizonDays. */
  value: number | null;
  /** Per-day average if you spread the final remaining total over the horizon. */
  averagePerDay: number | null;
  /** Total projected at the last day of the horizon. */
  remainingTotal: number | null;
  /** Lowest projected (cash + invested) total within the horizon. */
  minProjectedTotal: number | null;
  /** ISO date (YYYY-MM-DD) of the lowest projected day. */
  minProjectedDate: string | null;
  /** ISO date (YYYY-MM-DD) of the last covered day in the horizon. */
  horizonEndDate: string | null;
  horizonDays: number;
  horizonMonths: number;
}

/**
 * Returns the months whose aggregates are needed to compute the runway for the
 * given horizon: the previous month (for invested baseline) plus the current
 * month and every subsequent month up to `horizonMonths`.
 */
export function buildDailyRunwayMonthRefs(
  referenceDate: Date,
  horizonMonths: number,
): DailyRunwayMonthRef[] {
  const refs: DailyRunwayMonthRef[] = [monthRef(addMonthsClamped(referenceDate, -1))];
  for (let offset = 0; offset <= horizonMonths; offset += 1) {
    refs.push(monthRef(addMonthsClamped(referenceDate, offset)));
  }
  return refs;
}

export function resolveDailyRunwayReferenceDate(
  year: number,
  monthIdx: number,
  today = new Date(),
): Date {
  const day = Math.min(today.getDate(), daysInMonth(year, monthIdx));
  return new Date(year, monthIdx, day);
}

export function resolveDailyRunway(input: {
  referenceDate: Date;
  horizonMonths: number;
  months: DailyRunwayMonthInput[];
}): DailyRunwayState {
  const horizonMonths = input.horizonMonths;
  const startDate = startOfDay(input.referenceDate);
  const horizonEnd = addMonthsClamped(startDate, horizonMonths);
  const horizonDays = differenceInDays(startDate, horizonEnd);

  if (horizonDays <= 0 || horizonMonths <= 0) {
    return emptyDailyRunway(horizonMonths);
  }

  const orderedRefs = buildDailyRunwayMonthRefs(startDate, horizonMonths);
  const orderedMonths = orderedRefs.map((ref) =>
    input.months.find((month) => month.year === ref.year && month.monthIdx === ref.monthIdx) ?? null,
  );

  if (orderedMonths.some((month) => month == null)) {
    return emptyDailyRunway(horizonMonths, horizonDays);
  }

  const previousMonth = orderedMonths[0]!;
  if (previousMonth.summary.totals == null) {
    return emptyDailyRunway(horizonMonths, horizonDays);
  }

  let investedAtMonthEnd = previousMonth.summary.totals.investido;
  let endTotal: number | null = null;
  let endDate: Date | null = null;
  let minTotal = Number.POSITIVE_INFINITY;
  let minDate: Date | null = null;
  let coveredDays = 0;

  for (const month of orderedMonths.slice(1)) {
    if (
      month == null
      || month.summary.totals == null
      || month.summary.dailyBalances == null
      || month.summary.dailyMovements == null
      || month.summary.dailyBalances.length === 0
    ) {
      return emptyDailyRunway(horizonMonths, horizonDays);
    }

    const balancesByDay = new Map(month.summary.dailyBalances.map((balance) => [balance.day, balance.saldo]));
    const investedMovementsByDay = new Map<number, number>();

    for (const movement of month.summary.dailyMovements) {
      if (movement.kind !== 'invest') continue;
      investedMovementsByDay.set(
        movement.day,
        (investedMovementsByDay.get(movement.day) ?? 0) + movement.amount,
      );
    }

    let investedAtDayEnd = investedAtMonthEnd;
    const totalDays = daysInMonth(month.year, month.monthIdx);

    for (let day = 1; day <= totalDays; day += 1) {
      investedAtDayEnd += investedMovementsByDay.get(day) ?? 0;
      const currentDate = new Date(month.year, month.monthIdx, day);
      if (currentDate < startDate || currentDate >= horizonEnd) {
        continue;
      }

      const cashBalance = balancesByDay.get(day);
      if (cashBalance == null) {
        return emptyDailyRunway(horizonMonths, horizonDays);
      }

      const dayTotal = cashBalance + investedAtDayEnd;
      endTotal = dayTotal;
      endDate = currentDate;
      if (dayTotal < minTotal) {
        minTotal = dayTotal;
        minDate = currentDate;
      }
      coveredDays += 1;
    }

    investedAtMonthEnd = month.summary.totals.investido;
  }

  if (endTotal == null || coveredDays !== horizonDays) {
    return emptyDailyRunway(horizonMonths, horizonDays);
  }

  const minProjectedTotal = Number.isFinite(minTotal) ? minTotal : endTotal;
  const minProjectedDate = minDate ?? endDate;

  return {
    ready: true,
    value: minProjectedTotal / horizonDays,
    averagePerDay: endTotal / horizonDays,
    remainingTotal: endTotal,
    minProjectedTotal,
    minProjectedDate: minProjectedDate ? toIsoDate(minProjectedDate) : null,
    horizonEndDate: endDate ? toIsoDate(endDate) : null,
    horizonDays,
    horizonMonths,
  };
}

function emptyDailyRunway(horizonMonths: number, horizonDays = 0): DailyRunwayState {
  return {
    ready: false,
    value: null,
    averagePerDay: null,
    remainingTotal: null,
    minProjectedTotal: null,
    minProjectedDate: null,
    horizonEndDate: null,
    horizonDays,
    horizonMonths,
  };
}

function toIsoDate(value: Date): string {
  const year = value.getFullYear();
  const month = String(value.getMonth() + 1).padStart(2, '0');
  const day = String(value.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function startOfDay(value: Date): Date {
  return new Date(value.getFullYear(), value.getMonth(), value.getDate());
}

function addMonthsClamped(value: Date, months: number): Date {
  const targetYear = value.getFullYear();
  const targetMonthIdx = value.getMonth() + months;
  const targetMonthStart = new Date(targetYear, targetMonthIdx, 1);
  const targetDay = Math.min(
    value.getDate(),
    daysInMonth(targetMonthStart.getFullYear(), targetMonthStart.getMonth()),
  );
  return new Date(targetMonthStart.getFullYear(), targetMonthStart.getMonth(), targetDay);
}

function differenceInDays(start: Date, end: Date): number {
  const millisecondsPerDay = 24 * 60 * 60 * 1000;
  return Math.round((end.getTime() - start.getTime()) / millisecondsPerDay);
}

function monthRef(value: Date): DailyRunwayMonthRef {
  return {
    year: value.getFullYear(),
    monthIdx: value.getMonth(),
  };
}
