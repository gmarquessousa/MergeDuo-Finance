export type PeriodMode = 'monthly' | 'annual';
export type ScreenMode =
  | 'home'
  | 'profile'
  | 'fixed_transactions'
  | 'tags'
  | 'cards'
  | 'card_invoice'
  | 'merge'
  | 'simulator';

interface ScopeInput {
  year: number;
  monthIdx: number;
  period: PeriodMode;
  screen: ScreenMode;
}

export interface AggregateMonthRef {
  year: number;
  monthIdx: number;
}

export interface AggregateLoadPlan {
  criticalMonths: AggregateMonthRef[];
  criticalYears: number[];
  backgroundYears: number[];
}

function yearMonthString(year: number, monthIdx: number) {
  return `${year}-${String(monthIdx + 1).padStart(2, '0')}`;
}

export function shouldLoadYearTransactions(input: Pick<ScopeInput, 'period' | 'screen'>): boolean {
  return input.period === 'annual' || input.screen === 'simulator';
}

export function shouldLoadYearAggregates(input: Pick<ScopeInput, 'period' | 'screen'>): boolean {
  return input.period === 'annual' || input.screen === 'simulator';
}

export function transactionMonthsForScope(input: ScopeInput): string[] {
  if (shouldLoadYearTransactions(input)) {
    return Array.from({ length: 12 }, (_, monthIdx) => yearMonthString(input.year, monthIdx));
  }

  const months = new Set<string>([yearMonthString(input.year, input.monthIdx)]);
  return [...months];
}

export function buildAggregateLoadPlan(
  input: ScopeInput & { runwayMonthRefs: AggregateMonthRef[] },
): AggregateLoadPlan {
  const loadYear = shouldLoadYearAggregates(input);
  const criticalYears = loadYear ? [input.year] : [];
  const criticalMonths = loadYear
    ? []
    : [{ year: input.year, monthIdx: input.monthIdx }];
  const runwayYears = input.screen === 'home'
    ? uniqueYears(input.runwayMonthRefs)
    : [];

  return {
    criticalMonths,
    criticalYears,
    backgroundYears: runwayYears.filter((year) => !criticalYears.includes(year)),
  };
}

function uniqueYears(refs: AggregateMonthRef[]): number[] {
  return [...new Set(refs.map((ref) => ref.year))];
}
