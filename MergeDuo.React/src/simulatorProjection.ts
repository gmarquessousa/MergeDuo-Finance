import type { SimEntryFrequency, SimEntryKind } from './components/SimulatorEntrySheet';

export interface SimulationEntryLike {
  kind: SimEntryKind;
  amount: number;
  startDate: string;
  frequency: SimEntryFrequency;
  installmentsUntil?: string;
  recurringUntil?: string;
}

export interface SimulationBaselineMonth {
  monthIdx: number;
  accumulated: number;
  investedAccumulated: number;
}

interface SimulationDelta {
  saldo: number;
  investido: number;
}

export interface SimulationProjection {
  saldoBaselineByMonth: number[];
  investidoBaselineByMonth: number[];
  patrimonioBaselineByMonth: number[];
  saldoImpactByMonth: number[];
  investidoImpactByMonth: number[];
  patrimonioImpactByMonth: number[];
  saldoCumulativeImpactByMonth: number[];
  investidoCumulativeImpactByMonth: number[];
  patrimonioCumulativeImpactByMonth: number[];
  saldoProjectedByMonth: number[];
  investidoProjectedByMonth: number[];
  patrimonioProjectedByMonth: number[];
  saldoCumulativeImpactByAbsMonth: Map<number, number>;
  investidoCumulativeImpactByAbsMonth: Map<number, number>;
  patrimonioCumulativeImpactByAbsMonth: Map<number, number>;
}

function isoToAbsMonth(iso: string): number {
  const [year, month] = iso.split('-').map(Number);
  return year * 12 + (month - 1);
}

function endIsoFor(entry: SimulationEntryLike): string {
  if (entry.frequency === 'installments') return entry.installmentsUntil ?? entry.startDate;
  if (entry.frequency === 'recurring') return entry.recurringUntil ?? entry.startDate;
  return entry.startDate;
}

function deltaForKind(kind: SimEntryKind, amount: number): SimulationDelta {
  if (kind === 'in') {
    return { saldo: amount, investido: 0 };
  }
  if (kind === 'invest') {
    return { saldo: -amount, investido: amount };
  }
  return { saldo: -amount, investido: 0 };
}

function listEntryAbsMonths(entry: SimulationEntryLike): number[] {
  const startAbs = isoToAbsMonth(entry.startDate);
  const endAbs = isoToAbsMonth(endIsoFor(entry));
  if (entry.frequency === 'once') return [startAbs];

  const months: number[] = [];
  for (let abs = startAbs; abs <= endAbs; abs += 1) {
    months.push(abs);
  }
  return months;
}

function buildBaselineArrays(
  months: SimulationBaselineMonth[],
  baseBeforeYear: number,
  investedBeforeYear: number,
): Pick<
  SimulationProjection,
  'saldoBaselineByMonth' | 'investidoBaselineByMonth' | 'patrimonioBaselineByMonth'
> {
  const saldoBaselineByMonth = new Array(12).fill(baseBeforeYear);
  const investidoBaselineByMonth = new Array(12).fill(investedBeforeYear);
  const monthMap = new Map(months.map((month) => [month.monthIdx, month]));

  let lastSaldo = baseBeforeYear;
  let lastInvestido = investedBeforeYear;

  for (let monthIdx = 0; monthIdx < 12; monthIdx += 1) {
    const month = monthMap.get(monthIdx);
    if (month) {
      lastSaldo = month.accumulated;
      lastInvestido = month.investedAccumulated;
    }
    saldoBaselineByMonth[monthIdx] = lastSaldo;
    investidoBaselineByMonth[monthIdx] = lastInvestido;
  }

  return {
    saldoBaselineByMonth,
    investidoBaselineByMonth,
    patrimonioBaselineByMonth: saldoBaselineByMonth.map(
      (saldo, monthIdx) => saldo + investidoBaselineByMonth[monthIdx],
    ),
  };
}

export function buildSimulationProjection(input: {
  year: number;
  months: SimulationBaselineMonth[];
  baseBeforeYear: number;
  investedBeforeYear: number;
  entries: SimulationEntryLike[];
  tableEndAbsMonth?: number;
}): SimulationProjection {
  const {
    year,
    months,
    baseBeforeYear,
    investedBeforeYear,
    entries,
    tableEndAbsMonth = year * 12 + 11,
  } = input;

  const { saldoBaselineByMonth, investidoBaselineByMonth, patrimonioBaselineByMonth } =
    buildBaselineArrays(months, baseBeforeYear, investedBeforeYear);

  const saldoImpactByMonth = new Array(12).fill(0);
  const investidoImpactByMonth = new Array(12).fill(0);
  const deltasByAbsMonth = new Map<number, SimulationDelta>();

  for (const entry of entries) {
    const delta = deltaForKind(entry.kind, entry.amount);
    for (const absMonth of listEntryAbsMonths(entry)) {
      const current = deltasByAbsMonth.get(absMonth) ?? { saldo: 0, investido: 0 };
      deltasByAbsMonth.set(absMonth, {
        saldo: current.saldo + delta.saldo,
        investido: current.investido + delta.investido,
      });

      const yearOfAbsMonth = Math.floor(absMonth / 12);
      if (yearOfAbsMonth !== year) continue;

      const monthIdx = absMonth % 12;
      saldoImpactByMonth[monthIdx] += delta.saldo;
      investidoImpactByMonth[monthIdx] += delta.investido;
    }
  }

  const patrimonioImpactByMonth = saldoImpactByMonth.map(
    (saldo, monthIdx) => saldo + investidoImpactByMonth[monthIdx],
  );

  const saldoCumulativeImpactByMonth = new Array(12).fill(0);
  const investidoCumulativeImpactByMonth = new Array(12).fill(0);
  const patrimonioCumulativeImpactByMonth = new Array(12).fill(0);
  let runningSaldo = 0;
  let runningInvestido = 0;
  for (let monthIdx = 0; monthIdx < 12; monthIdx += 1) {
    runningSaldo += saldoImpactByMonth[monthIdx];
    runningInvestido += investidoImpactByMonth[monthIdx];
    saldoCumulativeImpactByMonth[monthIdx] = runningSaldo;
    investidoCumulativeImpactByMonth[monthIdx] = runningInvestido;
    patrimonioCumulativeImpactByMonth[monthIdx] = runningSaldo + runningInvestido;
  }

  const saldoProjectedByMonth = saldoBaselineByMonth.map(
    (saldo, monthIdx) => saldo + saldoCumulativeImpactByMonth[monthIdx],
  );
  const investidoProjectedByMonth = investidoBaselineByMonth.map(
    (investido, monthIdx) => investido + investidoCumulativeImpactByMonth[monthIdx],
  );
  const patrimonioProjectedByMonth = patrimonioBaselineByMonth.map(
    (patrimonio, monthIdx) => patrimonio + patrimonioCumulativeImpactByMonth[monthIdx],
  );

  const saldoCumulativeImpactByAbsMonth = new Map<number, number>();
  const investidoCumulativeImpactByAbsMonth = new Map<number, number>();
  const patrimonioCumulativeImpactByAbsMonth = new Map<number, number>();
  runningSaldo = 0;
  runningInvestido = 0;
  for (let absMonth = year * 12; absMonth <= tableEndAbsMonth; absMonth += 1) {
    const delta = deltasByAbsMonth.get(absMonth);
    runningSaldo += delta?.saldo ?? 0;
    runningInvestido += delta?.investido ?? 0;
    saldoCumulativeImpactByAbsMonth.set(absMonth, runningSaldo);
    investidoCumulativeImpactByAbsMonth.set(absMonth, runningInvestido);
    patrimonioCumulativeImpactByAbsMonth.set(absMonth, runningSaldo + runningInvestido);
  }

  return {
    saldoBaselineByMonth,
    investidoBaselineByMonth,
    patrimonioBaselineByMonth,
    saldoImpactByMonth,
    investidoImpactByMonth,
    patrimonioImpactByMonth,
    saldoCumulativeImpactByMonth,
    investidoCumulativeImpactByMonth,
    patrimonioCumulativeImpactByMonth,
    saldoProjectedByMonth,
    investidoProjectedByMonth,
    patrimonioProjectedByMonth,
    saldoCumulativeImpactByAbsMonth,
    investidoCumulativeImpactByAbsMonth,
    patrimonioCumulativeImpactByAbsMonth,
  };
}
