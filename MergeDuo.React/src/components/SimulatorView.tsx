import { useMemo, useState } from 'react';
import { useYearData } from '../useYearData';
import { formatBRL, hasNegativeSign } from '../utils';
import { buildSimulationProjection } from '../simulatorProjection';
import { useYearAggregateBalances } from '../useYearAggregateBalances';
import {
  SimulatorEntrySheet,
  type SimEntryFrequency,
  type SimEntryKind,
  type SimulatorEntryDraft,
} from './SimulatorEntrySheet';

interface Props {
  year: number;
  monthIdx: number;
  onBack: () => void;
}

interface SimulatorEntry extends SimulatorEntryDraft {
  id: string;
}

const MONTH_LABELS = [
  'Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho',
  'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro',
];

const MONTH_SHORT = [
  'Jan', 'Fev', 'Mar', 'Abr', 'Mai', 'Jun',
  'Jul', 'Ago', 'Set', 'Out', 'Nov', 'Dez',
];

const KIND_LABEL: Record<SimEntryKind, string> = {
  in: 'Entrada',
  out: 'Saída',
  invest: 'Aporte',
};

const KIND_COLOR: Record<SimEntryKind, string> = {
  in: 'text-accent-pos',
  out: 'text-accent-neg',
  invest: 'text-accent-invest',
};

const FREQ_LABEL: Record<SimEntryFrequency, string> = {
  once: 'Única',
  installments: 'Parcelada',
  recurring: 'Recorrente',
};

export function SimulatorView({ year, monthIdx, onBack }: Props) {
  const { months, baseBeforeYear, investedBeforeYear } = useYearData(year);
  const aggregateBalancesByMonth = useYearAggregateBalances(year);
  const [entries, setEntries] = useState<SimulatorEntry[]>([]);
  const [sheetOpen, setSheetOpen] = useState(false);

  const today = useMemo(() => new Date(), []);
  const isCurrentYear = today.getFullYear() === year;
  const startMonthIdx = isCurrentYear ? today.getMonth() : monthIdx;

  const defaultDate = useMemo(() => {
    const day = String(today.getDate()).padStart(2, '0');
    const m = String(startMonthIdx + 1).padStart(2, '0');
    return `${year}-${m}-${day}`;
  }, [year, startMonthIdx, today]);

  const yearEnd = `${year}-12-31`;

  const visibleMonths = Array.from({ length: 12 - startMonthIdx }, (_, i) => startMonthIdx + i);

  // Extended table: covers full installment periods even if they go beyond Dec of current year
  const tableEndAbsMonth = useMemo(() => {
    let end = year * 12 + 11;
    for (const e of entries) {
      if (e.frequency === 'installments' && e.installmentsUntil) {
        const [ey, em] = e.installmentsUntil.split('-').map(Number);
        end = Math.max(end, ey * 12 + (em - 1));
      }
    }
    return end;
  }, [entries, year]);

  const tableSlots = useMemo(() => {
    const slots: { y: number; m: number; abs: number }[] = [];
    const startAbs = year * 12 + startMonthIdx;
    for (let abs = startAbs; abs <= tableEndAbsMonth; abs++) {
      slots.push({ y: Math.floor(abs / 12), m: abs % 12, abs });
    }
    return slots;
  }, [year, startMonthIdx, tableEndAbsMonth]);

  const correctedMonths = useMemo(
    () => months.map((month) => {
      const corrected = aggregateBalancesByMonth?.get(month.monthIdx);
      if (!corrected) return month;
      return {
        ...month,
        accumulated: corrected.saldo,
        investedAccumulated: corrected.investido,
        patrimonio: corrected.patrimonio,
      };
    }),
    [aggregateBalancesByMonth, months],
  );

  const projection = useMemo(
    () => buildSimulationProjection({
      year,
      months: correctedMonths.map((month) => ({
        monthIdx: month.monthIdx,
        accumulated: month.accumulated,
        investedAccumulated: month.investedAccumulated,
      })),
      baseBeforeYear,
      investedBeforeYear,
      entries,
      tableEndAbsMonth,
    }),
    [baseBeforeYear, correctedMonths, entries, investedBeforeYear, tableEndAbsMonth, year],
  );

  const baselineEndYear = projection.patrimonioBaselineByMonth[11];
  const projectedEndYear = projection.patrimonioProjectedByMonth[11];
  const totalDelta = projectedEndYear - baselineEndYear;

  // Build sparkline data: from startMonthIdx → 11, both baseline and projected.
  const chart = useMemo(() => {
    const w = 320;
    const h = 80;
    const pad = 4;
    const baselineSlice = visibleMonths.map((i) => projection.patrimonioBaselineByMonth[i]);
    const projectedSlice = visibleMonths.map((i) => projection.patrimonioProjectedByMonth[i]);
    const all = [...baselineSlice, ...projectedSlice];
    const min = Math.min(...all);
    const max = Math.max(...all);
    const range = max - min || 1;
    const n = visibleMonths.length;
    const step = n > 1 ? (w - pad * 2) / (n - 1) : 0;
    const toPoints = (arr: number[]) =>
      arr
        .map((v, i) => {
          const x = (pad + i * step).toFixed(1);
          const y = (h - pad - ((v - min) / range) * (h - pad * 2)).toFixed(1);
          return `${x},${y}`;
        })
        .join(' ');
    return {
      w,
      h,
      baseline: toPoints(baselineSlice),
      projected: toPoints(projectedSlice),
    };
  }, [projection.patrimonioBaselineByMonth, projection.patrimonioProjectedByMonth, visibleMonths]);

  function addEntry(draft: SimulatorEntryDraft) {
    setEntries((prev) => [
      ...prev,
      { ...draft, id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}` },
    ]);
  }

  function removeEntry(id: string) {
    setEntries((prev) => prev.filter((e) => e.id !== id));
  }

  function clearAll() {
    setEntries([]);
  }

  return (
    <div className="pb-bottom-nav">
      <div className="flex items-center gap-3 px-4 sm:px-5 md:px-8 lg:px-10 py-3">
        <button
          onClick={onBack}
          className="w-9 h-9 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line transition"
          aria-label="Voltar"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <polyline points="15 18 9 12 15 6" />
          </svg>
        </button>
        <div>
          <h1 className="text-base font-semibold text-ink leading-tight">Simulador</h1>
          <p className="text-[11px] text-ink-muted">
            Patrimônio no topo, saldo acumulado na tabela. A projeção começa em {MONTH_SHORT[startMonthIdx]}/{year}.
          </p>
        </div>
      </div>

      {/* Resumo */}
      <div className="mx-4 sm:mx-5 md:mx-8 lg:mx-10 rounded-2xl bg-paper-card border border-paper-line p-4 shadow-soft-sm">
        <div className="text-[11px] uppercase tracking-wider text-ink-muted">
          Patrimônio projetado em dez/{year}
        </div>
        <div className="mt-1 flex items-end justify-between gap-3 flex-wrap">
          <div>
            <div className={`text-2xl font-semibold tabular-nums ${hasNegativeSign(projectedEndYear) ? 'text-accent-neg' : 'text-ink'}`}>
              {formatBRL(projectedEndYear)}
            </div>
            <div className="text-[11px] text-ink-muted mt-0.5">
              Sem simulação: {formatBRL(baselineEndYear)}
            </div>
          </div>
          {entries.length > 0 && (
            <div className={`text-sm font-medium ${hasNegativeSign(totalDelta) ? 'text-accent-neg' : 'text-accent-pos'}`}>
              {hasNegativeSign(totalDelta) ? '−' : '+'} {formatBRL(Math.abs(totalDelta))}
            </div>
          )}
        </div>

        {/* Sparkline */}
        {visibleMonths.length > 1 && (
          <div className="mt-3">
            <svg viewBox={`0 0 ${chart.w} ${chart.h}`} className="w-full h-20">
              <polyline
                fill="none"
                stroke="rgb(var(--ink-muted))"
                strokeOpacity="0.4"
                strokeWidth="1.5"
                strokeDasharray="3 3"
                points={chart.baseline}
              />
              <polyline
                fill="none"
                stroke="rgb(var(--accent-invest))"
                strokeWidth="2"
                points={chart.projected}
              />
            </svg>
            <div className="flex items-center gap-3 text-[10px] text-ink-muted mt-1">
              <span className="inline-flex items-center gap-1">
                <span className="w-3 h-px bg-ink-muted/50 border-t border-dashed" /> Sem simulação
              </span>
              <span className="inline-flex items-center gap-1">
                <span className="w-3 h-0.5 bg-accent-invest" /> Com simulação
              </span>
            </div>
          </div>
        )}
      </div>

      {/* Lista de simulações */}
      <div className="mt-4 mx-4 sm:mx-5 md:mx-8 lg:mx-10">
        <div className="flex items-center justify-between mb-2">
          <div className="text-[11px] uppercase tracking-wider text-ink-muted font-medium">
            Simulações ({entries.length})
          </div>
          {entries.length > 0 && (
            <button
              onClick={clearAll}
              className="text-[11px] text-ink-muted hover:text-accent-neg transition"
            >
              Limpar tudo
            </button>
          )}
        </div>

        {entries.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-paper-line p-6 text-center">
            <div className="text-[12px] text-ink-muted">
              Nenhuma simulação adicionada ainda.
            </div>
            <div className="text-[11px] text-ink-muted/80 mt-1">
              Toque em "Adicionar simulação" para começar.
            </div>
          </div>
        ) : (
          <div className="space-y-2">
            {entries.map((e) => (
              <EntryRow key={e.id} entry={e} onRemove={() => removeEntry(e.id)} />
            ))}
          </div>
        )}

        <button
          onClick={() => setSheetOpen(true)}
          className="mt-3 w-full h-11 rounded-xl bold-surface text-sm font-medium inline-flex items-center justify-center gap-1.5 active:scale-[0.99] shadow-soft-sm"
        >
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <line x1="12" y1="5" x2="12" y2="19" />
            <line x1="5" y1="12" x2="19" y2="12" />
          </svg>
          Adicionar simulação
        </button>
      </div>

      {/* Tabela mês a mês */}
      <div className="mt-5 mx-4 sm:mx-5 md:mx-8 lg:mx-10">
        <div className="text-[11px] uppercase tracking-wider text-ink-muted font-medium">
          Mês a mês
        </div>
        <p className="mt-1 mb-2 text-[10px] text-ink-muted leading-snug">
          Saldo acumulado em caixa no fim de cada mês, comparando base e projeção.
        </p>
        <div className="overflow-x-auto rounded-2xl">
        <div className="min-w-[300px] rounded-2xl bg-paper-card border border-paper-line overflow-hidden">
          <div className="grid grid-cols-[1fr_auto_auto] items-center text-[10px] uppercase tracking-wider text-ink-muted px-3 py-2 border-b border-paper-line">
            <span>Mês</span>
            <span className="px-2">Sem sim.</span>
            <span className="text-right min-w-[118px]">Projeção</span>
          </div>
          {tableSlots.map(({ y, m, abs }, idx) => {
            const isFutureYear = y > year;
            const baseline = isFutureYear
              ? projection.saldoBaselineByMonth[11]
              : projection.saldoBaselineByMonth[m];
            const projected = baseline + (projection.saldoCumulativeImpactByAbsMonth.get(abs) ?? 0);
            const isCurrent = isCurrentYear && !isFutureYear && m === today.getMonth();
            const showYearBadge = idx > 0 && y !== tableSlots[idx - 1].y;
            return (
              <div key={abs}>
                {showYearBadge && (
                  <div className="px-3 py-1 text-[10px] uppercase tracking-wider text-ink-muted bg-paper-line/60 border-b border-paper-line/60">
                    {y}
                  </div>
                )}
                <div
                  className={`grid grid-cols-[1fr_auto_auto] items-center px-3 py-2.5 text-[12px] border-b border-paper-line/60 last:border-b-0 ${
                    isCurrent ? 'bg-paper' : ''
                  }`}
                >
                  <div className="flex items-center gap-2">
                    <span className="text-ink font-medium">{MONTH_LABELS[m]}</span>
                    {isCurrent && (
                      <span className="text-[9px] uppercase tracking-wider text-ink-muted bg-paper-line px-1.5 rounded-full">
                        atual
                      </span>
                    )}
                  </div>
                  <span className="min-w-[118px] px-2 tabular-nums text-ink-muted text-[11px] text-right">
                    {isFutureYear ? '—' : formatBRL(baseline)}
                  </span>
                  <div className="text-right min-w-[118px]">
                    <div className={`tabular-nums font-medium ${hasNegativeSign(projected) ? 'text-accent-neg' : 'text-ink'}`}>
                      {formatBRL(projected)}
                    </div>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
        </div>
        <p className="mt-2 text-[10px] text-ink-muted leading-snug">
          O topo mostra patrimônio total projetado; a tabela mostra saldo de caixa acumulado. Aportes
          reduzem caixa, mas aumentam o investido no mesmo valor.
        </p>
      </div>

      <SimulatorEntrySheet
        open={sheetOpen}
        defaultDate={defaultDate}
        yearEnd={yearEnd}
        onClose={() => setSheetOpen(false)}
        onSubmit={addEntry}
      />
    </div>
  );
}

function EntryRow({ entry, onRemove }: { entry: SimulatorEntry; onRemove: () => void }) {
  const sign = entry.kind === 'in' ? '+' : '−';
  let detail: string;
  if (entry.frequency === 'installments') {
    const [sy, sm] = entry.startDate.split('-').map(Number);
    const endIso = entry.installmentsUntil ?? entry.startDate;
    const [ey, em] = endIso.split('-').map(Number);
    const n = Math.max(1, (ey - sy) * 12 + (em - sm) + 1);
    detail = `${n}x de ${entry.amount.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}`;
  } else if (entry.frequency === 'recurring') {
    detail = `Mensal até ${formatMonthShort(entry.recurringUntil ?? entry.startDate)}`;
  } else {
    detail = `Em ${formatDayShort(entry.startDate)}`;
  }
  return (
    <div className="flex items-center gap-3 rounded-xl bg-paper-card border border-paper-line px-3 py-2.5">
      <div className={`text-xs font-semibold ${KIND_COLOR[entry.kind]}`}>{KIND_LABEL[entry.kind]}</div>
      <div className="flex-1 min-w-0">
        <div className="text-[13px] text-ink font-medium truncate">{entry.description}</div>
        <div className="text-[10px] text-ink-muted">
          {FREQ_LABEL[entry.frequency]} · {detail}
        </div>
      </div>
      <div className={`tabular-nums text-sm font-semibold ${KIND_COLOR[entry.kind]}`}>
        {sign} {entry.amount.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}
      </div>
      <button
        onClick={onRemove}
        className="w-7 h-7 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line transition"
        aria-label="Remover simulação"
      >
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <line x1="18" y1="6" x2="6" y2="18"/>
          <line x1="6" y1="6" x2="18" y2="18"/>
        </svg>
      </button>
    </div>
  );
}

function formatDayShort(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  return `${String(d).padStart(2, '0')}/${MONTH_SHORT[m - 1]}/${y}`;
}

function formatMonthShort(iso: string): string {
  const [y, m] = iso.split('-').map(Number);
  return `${MONTH_SHORT[m - 1]}/${y}`;
}
