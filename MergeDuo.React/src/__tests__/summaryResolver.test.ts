import { describe, expect, it } from 'vitest';
import { resolveSummaryDisplay } from '../summaryResolver';
import type { TransactionLoadState } from '../store';
import type { AggregateSummary, AggregateYearSummary } from '../useAggregateSummary';
import type { MonthData } from '../useMonthData';
import type { YearData } from '../useYearData';

const loadingLoad: TransactionLoadState = {
  status: 'loading',
  error: null,
  continuationToken: null,
  itemKeys: [],
};

const readyLoad: TransactionLoadState = {
  status: 'ready',
  error: null,
  continuationToken: null,
  itemKeys: [],
};

const monthData: MonthData = {
  perDay: [],
  baseBeforeMonth: 900,
  investedBeforeMonth: 100,
  totalAcumulado: 1100,
  totalInvested: 250,
  patrimonioTotal: 1350,
  saldoHoje: 1000,
  investidoHoje: 250,
  patrimonioHoje: 1250,
  monthTotals: {
    entradas: 500,
    saidas: 200,
    aportes: 150,
    byCategory: {},
  },
  monthTransactions: [],
};

const yearData: YearData = {
  months: [],
  yearTotals: {
    entradas: 5000,
    saidas: 2100,
    aportes: 1000,
    byCategory: {},
  },
  totalAcumulado: 3000,
  totalInvested: 1200,
  patrimonioTotal: 4200,
  baseBeforeYear: 1000,
  investedBeforeYear: 0,
  topTransactions: [],
};

describe('resolveSummaryDisplay', () => {
  it('keeps the header loading when no trusted financial source is ready', () => {
    const summary = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: true,
      monthData,
      yearData,
      aggregateMonth: aggregateMonth('loading', null),
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: loadingLoad,
      yearTransactionLoads: [],
    });

    expect(summary.status).toBe('loading');
    expect(summary.patrimonio).toBe(0);
  });

  it('uses a non-empty aggregate as the first trusted monthly source', () => {
    const summary = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: false,
      monthData,
      yearData,
      aggregateMonth: aggregateMonth('ready', {
        entradas: 700,
        saidas: 300,
        aportes: 100,
        saldo: 2000,
        investido: 500,
      }),
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: loadingLoad,
      yearTransactionLoads: [],
    });

    expect(summary.status).toBe('ready');
    expect(summary.entradas).toBe(700);
    expect(summary.patrimonio).toBe(2500);
  });

  it('does not accept an empty aggregate until transactions confirm the empty period', () => {
    const waiting = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: false,
      monthData,
      yearData,
      aggregateMonth: aggregateMonth('ready', undefined, 'empty'),
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: loadingLoad,
      yearTransactionLoads: [],
    });

    const fallback = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: false,
      monthData,
      yearData,
      aggregateMonth: aggregateMonth('ready', undefined, 'empty'),
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: readyLoad,
      yearTransactionLoads: [],
    });

    expect(waiting.status).toBe('loading');
    expect(fallback.status).toBe('ready');
    expect(fallback.entradas).toBe(monthData.monthTotals.entradas);
  });

  it('uses today snapshot values for the current month when aggregate is ready', () => {
    const summary = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: true,
      monthData,
      yearData,
      aggregateMonth: {
        ...aggregateMonth('ready', {
          entradas: 1200,
          saidas: 500,
          aportes: 200,
          saldo: 3000,
          investido: 700,
        }),
        snapshotToday: {
          saldoHoje: 2400,
          investidoHoje: 600,
          patrimonioHoje: 3000,
        },
        isProjected: true,
      },
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: loadingLoad,
      yearTransactionLoads: [],
    });

    expect(summary.status).toBe('ready');
    expect(summary.saldo).toBe(2400);
    expect(summary.investido).toBe(600);
    expect(summary.patrimonio).toBe(3000);
    expect(summary.entradas).toBe(1200);
    expect(summary.saidas).toBe(500);
    expect(summary.aportes).toBe(200);
    expect(summary.isProjected).toBe(true);
  });

  it('uses local today values for the current month when aggregate fails after transactions loaded', () => {
    const summary = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: true,
      monthData,
      yearData,
      aggregateMonth: { ...aggregateMonth('error', null), error: 'aggregate unavailable' },
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: readyLoad,
      yearTransactionLoads: [],
    });

    expect(summary.status).toBe('error');
    expect(summary.error).toBe('aggregate unavailable');
    expect(summary.saldo).toBe(monthData.saldoHoje);
    expect(summary.investido).toBe(monthData.investidoHoje);
    expect(summary.patrimonio).toBe(monthData.patrimonioHoje);
    expect(summary.entradas).toBe(monthData.monthTotals.entradas);
    expect(summary.saidas).toBe(monthData.monthTotals.saidas);
    expect(summary.aportes).toBe(monthData.monthTotals.aportes);
    expect(summary.isProjected).toBe(true);
  });

  it('falls back to local month data when aggregate fails after transactions loaded', () => {
    const summary = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: false,
      monthData,
      yearData,
      aggregateMonth: { ...aggregateMonth('error', null), error: 'aggregate unavailable' },
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: readyLoad,
      yearTransactionLoads: [],
    });

    expect(summary.status).toBe('error');
    expect(summary.error).toBe('aggregate unavailable');
    expect(summary.patrimonio).toBe(monthData.totalAcumulado + monthData.totalInvested);
  });

  it('falls back to local month data when aggregate is stale and transactions are ready', () => {
    const summary = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: false,
      monthData,
      yearData,
      aggregateMonth: aggregateMonth('ready', {
        entradas: 1200,
        saidas: 200,
        aportes: 100,
        saldo: 900,
        investido: 150,
      }, 'live', true),
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: readyLoad,
      yearTransactionLoads: [],
    });

    expect(summary.status).toBe('ready');
    expect(summary.error).toBe(null);
    expect(summary.saldo).toBe(monthData.totalAcumulado);
    expect(summary.investido).toBe(monthData.totalInvested);
    expect(summary.patrimonio).toBe(monthData.patrimonioTotal);
  });

  it('preserves the aggregate historical invested base when stale monthly data falls back locally', () => {
    const summary = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: false,
      monthData,
      yearData,
      aggregateMonth: aggregateMonth('ready', {
        entradas: 1200,
        saidas: 200,
        aportes: 100,
        saldo: 900,
        investido: 1100,
      }, 'live', true),
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: readyLoad,
      yearTransactionLoads: [],
    });

    expect(summary.status).toBe('ready');
    expect(summary.error).toBe(null);
    expect(summary.saldo).toBe(monthData.totalAcumulado);
    expect(summary.investido).toBe(1150);
    expect(summary.patrimonio).toBe(2250);
    expect(summary.entradas).toBe(monthData.monthTotals.entradas);
    expect(summary.aportes).toBe(monthData.monthTotals.aportes);
  });

  it('keeps previous aggregate values visible while a refresh is updating', () => {
    const summary = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: false,
      monthData,
      yearData,
      aggregateMonth: aggregateMonth('updating', {
        entradas: 1000,
        saidas: 100,
        aportes: 50,
        saldo: 4000,
        investido: 1000,
      }),
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: loadingLoad,
      yearTransactionLoads: [],
    });

    expect(summary.status).toBe('updating');
    expect(summary.patrimonio).toBe(5000);
  });

  it('blocks a locally invalidated aggregate until transactions are ready', () => {
    const waiting = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: false,
      monthData,
      yearData,
      aggregateMonth: {
        ...aggregateMonth('ready', {
          entradas: 1000,
          saidas: 100,
          aportes: 50,
          saldo: 4000,
          investido: 1000,
        }),
        locallyInvalidated: true,
      },
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: loadingLoad,
      yearTransactionLoads: [],
    });

    const fallback = resolveSummaryDisplay({
      period: 'monthly',
      isCurrentMonthPeriod: false,
      monthData,
      yearData,
      aggregateMonth: {
        ...aggregateMonth('ready', {
          entradas: 1000,
          saidas: 100,
          aportes: 50,
          saldo: 4000,
          investido: 1000,
        }),
        locallyInvalidated: true,
      },
      aggregateYear: aggregateYear('idle'),
      monthTransactionLoad: readyLoad,
      yearTransactionLoads: [],
    });

    expect(waiting.status).toBe('loading');
    expect(fallback.status).toBe('ready');
    expect(fallback.saldo).toBe(monthData.totalAcumulado);
  });

  it('falls back to local annual data when aggregate is stale and all transactions are ready', () => {
    const summary = resolveSummaryDisplay({
      period: 'annual',
      isCurrentMonthPeriod: false,
      monthData,
      yearData,
      aggregateMonth: aggregateMonth('idle', null),
      aggregateYear: {
        ...aggregateYear('ready', true),
        totals: {
          entradas: 100,
          saidas: 50,
          aportes: 10,
        },
        endOfYear: {
          saldo: 500,
          investido: 100,
        },
      },
      yearTransactionLoads: [readyLoad, readyLoad],
    });

    expect(summary.status).toBe('ready');
    expect(summary.error).toBe(null);
    expect(summary.saldo).toBe(yearData.totalAcumulado);
    expect(summary.investido).toBe(yearData.totalInvested);
    expect(summary.patrimonio).toBe(yearData.patrimonioTotal);
  });
});

function aggregateMonth(
  status: AggregateSummary['status'],
  totals: AggregateSummary['totals'] = {
    entradas: 0,
    saidas: 0,
    aportes: 0,
    saldo: 0,
    investido: 0,
  },
  source = totals ? 'live' : null,
  isStale = false,
): AggregateSummary {
  return {
    status,
    error: status === 'error' ? 'aggregate error' : null,
    source,
    isStale,
    computedAt: '2026-04-01T00:00:00Z',
    totals,
    snapshotToday: null,
    dailyBalances: [],
    dailyMovements: [],
    isProjected: false,
    locallyInvalidated: false,
  };
}

function aggregateYear(
  status: AggregateYearSummary['status'],
  isStale = false,
): AggregateYearSummary {
  return {
    status,
    error: null,
    isStale,
    source: null,
    totals: null,
    endOfYear: null,
    isProjected: false,
    locallyInvalidated: false,
  };
}
