import type { TransactionLoadState } from './store';
import type { MonthData } from './useMonthData';
import type { YearData } from './useYearData';
import type { AggregateSummary, AggregateYearSummary } from './useAggregateSummary';
import { shouldTrustMonthAggregate, shouldTrustYearAggregate } from './aggregateTrust';

export type SummaryDisplayStatus = 'loading' | 'ready' | 'error' | 'updating';

export interface SummaryDisplayState {
  status: SummaryDisplayStatus;
  error: string | null;
  patrimonio: number;
  saldo: number;
  investido: number;
  entradas: number;
  saidas: number;
  aportes: number;
  isProjected: boolean;
}

interface ResolveSummaryInput {
  period: 'monthly' | 'annual';
  isCurrentMonthPeriod: boolean;
  monthData: MonthData;
  yearData: YearData;
  aggregateMonth: AggregateSummary;
  aggregateYear: AggregateYearSummary;
  monthTransactionLoad?: TransactionLoadState;
  yearTransactionLoads: Array<TransactionLoadState | undefined>;
}

export function resolveSummaryDisplay(input: ResolveSummaryInput): SummaryDisplayState {
  return input.period === 'monthly'
    ? resolveMonthlySummary(input)
    : resolveAnnualSummary(input);
}

function resolveMonthlySummary(input: ResolveSummaryInput): SummaryDisplayState {
  const aggregate = input.aggregateMonth;
  const transactionReady = input.monthTransactionLoad?.status === 'ready';
  const transactionError = input.monthTransactionLoad?.status === 'error'
    ? input.monthTransactionLoad.error
    : null;
  const canUseAggregate =
    aggregate.totals != null
    && !isEmptyMonthAggregate(aggregate)
    && shouldTrustMonthAggregate(
      aggregate.isStale,
      input.monthTransactionLoad,
      aggregate.locallyInvalidated,
    );

  if (input.isCurrentMonthPeriod) {
    if (canUseAggregate) {
      return {
        ...fromCurrentMonthAggregate(input, displayStatusForAggregate(aggregate.status)),
        error: aggregate.status === 'error' ? aggregate.error : null,
      };
    }

    if (aggregate.totals && isEmptyMonthAggregate(aggregate) && !transactionReady) {
      return loadingSummary(aggregate.error ?? transactionError);
    }

    if (transactionReady) {
      return fromCurrentMonthData(
        input,
        aggregate.status === 'error' ? 'error' : 'ready',
        aggregate.error,
      );
    }

    if (aggregate.status === 'error') {
      return loadingSummary(aggregate.error ?? transactionError);
    }

    return loadingSummary(transactionError);
  }

  if (canUseAggregate) {
    return {
      ...fromMonthAggregate(input, displayStatusForAggregate(aggregate.status)),
      error: aggregate.status === 'error' ? aggregate.error : null,
    };
  }

  if (aggregate.totals && isEmptyMonthAggregate(aggregate) && !transactionReady) {
    return loadingSummary(aggregate.error ?? transactionError);
  }

  if (transactionReady) {
    return fromMonthData(input, aggregate.status === 'error' ? 'error' : 'ready', aggregate.error);
  }

  if (aggregate.status === 'error') {
    return loadingSummary(aggregate.error ?? transactionError);
  }

  return loadingSummary(transactionError);
}

function resolveAnnualSummary(input: ResolveSummaryInput): SummaryDisplayState {
  const aggregate = input.aggregateYear;
  const allTransactionsReady =
    input.yearTransactionLoads.length > 0 &&
    input.yearTransactionLoads.every((state) => state?.status === 'ready');
  const transactionError = input.yearTransactionLoads.find((state) => state?.status === 'error')?.error ?? null;
  const canUseAggregate =
    aggregate.totals != null
    && aggregate.endOfYear != null
    && !isEmptyYearAggregate(aggregate)
    && shouldTrustYearAggregate(
      aggregate.isStale,
      input.yearTransactionLoads,
      aggregate.locallyInvalidated,
    );

  if (canUseAggregate) {
    return fromYearAggregate(aggregate, displayStatusForAggregate(aggregate.status));
  }

  if (aggregate.totals && aggregate.endOfYear && isEmptyYearAggregate(aggregate) && !allTransactionsReady) {
    return loadingSummary(aggregate.error ?? transactionError);
  }

  if (allTransactionsReady) {
    return fromYearData(input.yearData, aggregate.status === 'error' ? 'error' : 'ready', aggregate.error);
  }

  if (aggregate.status === 'error') {
    return loadingSummary(aggregate.error ?? transactionError);
  }

  return loadingSummary(transactionError);
}

function fromMonthAggregate(
  input: ResolveSummaryInput,
  status: SummaryDisplayStatus,
): SummaryDisplayState {
  const totals = input.aggregateMonth.totals!;
  const saldo = totals.saldo;
  const investido = totals.investido;
  const patrimonio = saldo + investido;

  return {
    status,
    error: null,
    patrimonio,
    saldo,
    investido,
    entradas: totals.entradas,
    saidas: totals.saidas,
    aportes: totals.aportes,
    isProjected: input.aggregateMonth.isProjected,
  };
}

function fromMonthData(
  input: ResolveSummaryInput,
  status: SummaryDisplayStatus,
  error: string | null,
): SummaryDisplayState {
  const { monthData } = input;
  const saldo = monthData.totalAcumulado;
  const investido = resolveMonthEndInvested(input);
  const patrimonio = saldo + investido;
  return {
    status,
    error,
    patrimonio,
    saldo,
    investido,
    entradas: monthData.monthTotals.entradas,
    saidas: monthData.monthTotals.saidas,
    aportes: monthData.monthTotals.aportes,
    isProjected: input.isCurrentMonthPeriod
      && monthData.patrimonioHoje != null
      && Math.round(patrimonio) !== Math.round(monthData.patrimonioHoje),
  };
}

function fromCurrentMonthAggregate(
  input: ResolveSummaryInput,
  status: SummaryDisplayStatus,
): SummaryDisplayState {
  const totals = input.aggregateMonth.totals!;
  const snapshot = input.aggregateMonth.snapshotToday;
  const saldo = snapshot?.saldoHoje
    ?? input.monthData.saldoHoje
    ?? totals.saldo;
  const investido = snapshot?.investidoHoje
    ?? input.monthData.investidoHoje
    ?? totals.investido;
  const patrimonio = snapshot?.patrimonioHoje
    ?? saldo + investido;

  return {
    status,
    error: null,
    patrimonio,
    saldo,
    investido,
    entradas: totals.entradas,
    saidas: totals.saidas,
    aportes: totals.aportes,
    isProjected: input.aggregateMonth.isProjected,
  };
}

function fromCurrentMonthData(
  input: ResolveSummaryInput,
  status: SummaryDisplayStatus,
  error: string | null,
): SummaryDisplayState {
  const { monthData } = input;
  const saldo = monthData.saldoHoje ?? monthData.totalAcumulado;
  const investido = monthData.investidoHoje ?? resolveMonthEndInvested(input);
  const patrimonio = monthData.patrimonioHoje ?? saldo + investido;
  const monthEndPatrimonio = monthData.totalAcumulado + resolveMonthEndInvested(input);

  return {
    status,
    error,
    patrimonio,
    saldo,
    investido,
    entradas: monthData.monthTotals.entradas,
    saidas: monthData.monthTotals.saidas,
    aportes: monthData.monthTotals.aportes,
    isProjected: monthData.patrimonioHoje != null
      && Math.round(monthEndPatrimonio) !== Math.round(patrimonio),
  };
}

function resolveMonthEndInvested(input: ResolveSummaryInput): number {
  const aggregateTotals = input.aggregateMonth.totals;
  if (!aggregateTotals) {
    return input.monthData.totalInvested;
  }

  const aggregateBeforeMonth = aggregateTotals.investido - aggregateTotals.aportes;
  const bestKnownBeforeMonth = Math.max(input.monthData.investedBeforeMonth, aggregateBeforeMonth);
  return bestKnownBeforeMonth + input.monthData.monthTotals.aportes;
}

function fromYearAggregate(
  aggregate: AggregateYearSummary,
  status: SummaryDisplayStatus,
): SummaryDisplayState {
  return {
    status,
    error: aggregate.status === 'error' ? aggregate.error : null,
    patrimonio: aggregate.endOfYear!.saldo + aggregate.endOfYear!.investido,
    saldo: aggregate.endOfYear!.saldo,
    investido: aggregate.endOfYear!.investido,
    entradas: aggregate.totals!.entradas,
    saidas: aggregate.totals!.saidas,
    aportes: aggregate.totals!.aportes,
    isProjected: aggregate.isProjected,
  };
}

function fromYearData(
  yearData: YearData,
  status: SummaryDisplayStatus,
  error: string | null,
): SummaryDisplayState {
  return {
    status,
    error,
    patrimonio: yearData.totalAcumulado + yearData.totalInvested,
    saldo: yearData.totalAcumulado,
    investido: yearData.totalInvested,
    entradas: yearData.yearTotals.entradas,
    saidas: yearData.yearTotals.saidas,
    aportes: yearData.yearTotals.aportes,
    isProjected: false,
  };
}

function loadingSummary(error: string | null): SummaryDisplayState {
  return {
    status: 'loading',
    error,
    patrimonio: 0,
    saldo: 0,
    investido: 0,
    entradas: 0,
    saidas: 0,
    aportes: 0,
    isProjected: false,
  };
}

function displayStatusForAggregate(status: AggregateSummary['status']): SummaryDisplayStatus {
  if (status === 'updating') return 'updating';
  if (status === 'error') return 'error';
  return 'ready';
}

function isEmptyMonthAggregate(aggregate: AggregateSummary): boolean {
  if (!aggregate.totals) return true;
  return aggregate.source?.includes('empty') === true;
}

function isEmptyYearAggregate(aggregate: AggregateYearSummary): boolean {
  if (!aggregate.totals || !aggregate.endOfYear) return true;
  return aggregate.source?.includes('empty') === true;
}
