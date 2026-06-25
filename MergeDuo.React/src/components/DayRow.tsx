import { CATEGORY_META, type Transaction } from '../types';
import { formatBRL, hasNegativeSign, isNeutralZero, isToday, weekdayLabel } from '../utils';
import { useValuesHidden } from '../valuesVisibilityContext';

interface Props {
  year: number;
  monthIdx: number;
  day: number;
  dayNet: number; // saldo do dia (entradas - saidas)
  totalAcumulado: number;
  expanded: boolean;
  onToggle: () => void;
  transactions: Transaction[];
  markerLabel?: string;
}

export function DayRow({
  year,
  monthIdx,
  day,
  dayNet,
  totalAcumulado,
  expanded,
  onToggle,
  transactions,
  markerLabel,
}: Props) {
  const hidden = useValuesHidden();
  const today = isToday(year, monthIdx, day);
  const isFuture = new Date(year, monthIdx, day) > new Date(new Date().getFullYear(), new Date().getMonth(), new Date().getDate());
  const hasActivity = transactions.length > 0;

  const saldoColor =
    hasNegativeSign(dayNet)
      ? 'text-accent-neg'
      : dayNet > 0
      ? 'text-accent-pos'
      : 'text-ink-muted';
  const accumulatedColor = hasNegativeSign(totalAcumulado) ? 'text-accent-neg' : 'text-ink';

  return (
    <button
      onClick={onToggle}
      className={`w-full text-left px-4 py-3 grid grid-cols-[52px_minmax(0,1fr)] min-[380px]:grid-cols-[52px_minmax(0,1fr)_auto] items-center gap-3 border-b border-paper-line tap-surface sm:px-5 md:px-8 lg:px-10 ${
        expanded ? 'bg-paper-card' : 'bg-transparent hover:bg-paper-card/70'
      }`}
    >
      {/* Dia */}
      <div className="flex flex-col items-center gap-0.5">
        <div
          className={`w-10 h-10 rounded-2xl grid place-items-center text-sm font-semibold ${
            markerLabel ? 'relative' : ''
          } ${
            today
              ? 'bold-surface shadow-soft-sm'
              : hasActivity
              ? 'bg-paper-card text-ink border border-paper-line shadow-soft-sm'
              : 'text-ink-muted'
          }`}
        >
          {day}
          {markerLabel && (
            <span aria-hidden="true" className="absolute -right-1 -top-1 h-2.5 w-2.5 rounded-full bg-accent-neg border-2 border-paper" />
          )}
        </div>
        <div className="text-[9px] uppercase tracking-wider text-ink-muted font-medium">
          {weekdayLabel(year, monthIdx, day)}
        </div>
      </div>

      {/* Saldo do dia */}
      <div className="min-w-0">
        <div className="text-[10px] uppercase tracking-wider text-ink-muted font-medium">
          Saldo do dia
        </div>
        <div className={`text-sm font-semibold tabular-nums ${saldoColor}`}>
          {isNeutralZero(dayNet)
            ? <span className="text-ink-muted font-normal">—</span>
            : hidden
            ? 'R$ ••••'
            : `${isFuture ? '~ ' : ''}${hasNegativeSign(dayNet) ? '−' : '+'} ${formatBRL(Math.abs(dayNet))}`}
        </div>
        {(hasActivity || markerLabel) && (
          <div className="mt-0.5 flex flex-wrap items-center gap-1">
            {hasActivity && !expanded && (
              <div className="flex items-center gap-0.5">
                {transactions.slice(0, 8).map((t, i) => {
                  const kind = CATEGORY_META[t.category].kind;
                  const dot =
                    kind === 'in'
                      ? 'bg-accent-pos'
                      : kind === 'invest'
                      ? 'bg-accent-invest'
                      : 'bg-accent-neg';
                  return <span key={i} className={`w-1.5 h-1.5 rounded-full ${dot} opacity-75`} />;
                })}
                {transactions.length > 8 && (
                  <span className="text-[9px] text-ink-muted ml-0.5 leading-none">
                    +{transactions.length - 8}
                  </span>
                )}
              </div>
            )}
            {expanded && hasActivity && (
              <span className="text-[10px] text-ink-muted">
                {transactions.length} {transactions.length === 1 ? 'lançamento' : 'lançamentos'}
              </span>
            )}
            {markerLabel && (
              <span className="inline-flex items-center h-4 rounded-full border border-accent-neg/25 bg-accent-neg/8 px-1.5 text-[10px] font-medium text-accent-neg">
                {markerLabel}
              </span>
            )}
          </div>
        )}
      </div>

      {/* Total acumulado */}
      <div className="col-span-2 text-left min-[380px]:col-span-1 min-[380px]:text-right">
        <div className="text-[10px] uppercase tracking-wider text-ink-muted font-medium">
          Total
        </div>
        <div className={`text-sm font-semibold tabular-nums ${accumulatedColor}`}>
          {hidden ? 'R$ ••••' : `${isFuture ? '~ ' : ''}${formatBRL(totalAcumulado)}`}
        </div>
      </div>
    </button>
  );
}
