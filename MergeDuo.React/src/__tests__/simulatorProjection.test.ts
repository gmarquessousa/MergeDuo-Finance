import { describe, expect, it } from 'vitest';
import { buildSimulationProjection, type SimulationBaselineMonth, type SimulationEntryLike } from '../simulatorProjection';

function project(
  overrides: Partial<Parameters<typeof buildSimulationProjection>[0]> = {},
) {
  const months: SimulationBaselineMonth[] = overrides.months ?? [
    { monthIdx: 0, accumulated: 1000, investedAccumulated: 200 },
    { monthIdx: 11, accumulated: 1000, investedAccumulated: 200 },
  ];

  return buildSimulationProjection({
    year: 2026,
    months,
    baseBeforeYear: 1000,
    investedBeforeYear: 200,
    entries: overrides.entries ?? [],
    tableEndAbsMonth: overrides.tableEndAbsMonth,
  });
}

function entry(overrides: Partial<SimulationEntryLike>): SimulationEntryLike {
  return {
    kind: overrides.kind ?? 'out',
    amount: overrides.amount ?? 100,
    startDate: overrides.startDate ?? '2026-01-15',
    frequency: overrides.frequency ?? 'once',
    installmentsUntil: overrides.installmentsUntil,
    recurringUntil: overrides.recurringUntil,
  };
}

describe('buildSimulationProjection', () => {
  it('keeps patrimonio unchanged for a pure investment while moving value from cash to invested', () => {
    const result = project({
      entries: [entry({ kind: 'invest', amount: 150, startDate: '2026-02-10' })],
    });

    expect(result.saldoProjectedByMonth[1]).toBe(850);
    expect(result.investidoProjectedByMonth[1]).toBe(350);
    expect(result.patrimonioProjectedByMonth[1]).toBe(1200);
    expect(result.patrimonioImpactByMonth[1]).toBe(0);
  });

  it('accumulates recurring income month by month inside the selected year', () => {
    const result = project({
      entries: [entry({ kind: 'in', amount: 50, startDate: '2026-03-01', frequency: 'recurring', recurringUntil: '2026-05-31' })],
    });

    expect(result.saldoImpactByMonth.slice(0, 6)).toEqual([0, 0, 50, 50, 50, 0]);
    expect(result.saldoCumulativeImpactByMonth.slice(0, 6)).toEqual([0, 0, 50, 100, 150, 150]);
    expect(result.saldoProjectedByMonth[4]).toBe(1150);
  });

  it('extends installment cash impact beyond december through the absolute-month map', () => {
    const result = project({
      entries: [entry({ kind: 'out', amount: 80, startDate: '2026-11-05', frequency: 'installments', installmentsUntil: '2027-02-05' })],
      tableEndAbsMonth: 2027 * 12 + 1,
    });

    expect(result.saldoCumulativeImpactByAbsMonth.get(2026 * 12 + 10)).toBe(-80);
    expect(result.saldoCumulativeImpactByAbsMonth.get(2026 * 12 + 11)).toBe(-160);
    expect(result.saldoCumulativeImpactByAbsMonth.get(2027 * 12 + 0)).toBe(-240);
    expect(result.saldoCumulativeImpactByAbsMonth.get(2027 * 12 + 1)).toBe(-320);
  });
});