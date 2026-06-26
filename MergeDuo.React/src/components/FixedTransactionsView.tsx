import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  CATEGORY_META,
  type FixedTransactionRule,
  type FixedTransactionSchedule,
  type TransactionCategory,
} from '../types';
import { CategoryIcon } from './CategoryIcon';
import { MonthYearPicker } from './MonthYearPicker';
import { useFinance } from '../store';
import { useRefresh } from '../refreshContext';
import {
  describeFixedTransactionSchedule,
  nextFixedTransactionDate,
  resolveFixedTransactionDay,
} from '../fixedTransactions';
import { formatBRL } from '../utils';
import {
  FixedRulesApiError,
  createFixedRule,
  deleteFixedRule,
  getFixedRule,
  getFixedRulePreview,
  listFixedRules,
  patchFixedRule,
  pauseFixedRule,
  resumeFixedRule,
  type FixedRuleOccurrenceResponse,
} from '../api/fixedRules';
import { TagInput } from './TagInput';

type ScheduleMode = FixedTransactionSchedule['type'];
type PeriodOption = Extract<FixedTransactionSchedule, { type: 'period' }>['period'];

const GROUPS: {
  title: string;
  options: TransactionCategory[];
}[] = [
  { title: 'Entrada', options: ['income'] },
  {
    title: 'Saídas',
    options: ['credit_card', 'loan', 'fixed_expense', 'variable_expense'],
  },
  { title: 'Aporte', options: ['investment'] },
];

const PERIOD_OPTIONS: { value: PeriodOption; label: string }[] = [
  { value: 'start', label: 'Início' },
  { value: 'middle', label: 'Meio' },
  { value: 'end', label: 'Fim' },
];

export function FixedTransactionsView({
  accessToken,
  onBack,
  initialDraft,
  onInitialDraftConsumed,
}: {
  accessToken: string;
  onBack: () => void;
  initialDraft?: {
    category?: TransactionCategory;
    description?: string;
    amount?: number;
    cardId?: string | null;
    tags?: string[];
  } | null;
  onInitialDraftConsumed?: () => void;
}) {
  const {
    fixedTransactions,
    fixedRulesStatus,
    fixedRulesError,
    setFixedRulesLoading,
    setFixedRules,
    setFixedRulesError,
    addFixedRule,
    updateFixedRule,
    removeFixedRule,
    cards,
    cardsStatus,
    cardsError,
    knownTags,
    mergeKnownTags,
  } = useFinance();
  const refreshCtx = useRefresh();
  const [category, setCategory] = useState<TransactionCategory>('fixed_expense');
  const [description, setDescription] = useState('');
  const [amountStr, setAmountStr] = useState('');
  const [cardId, setCardId] = useState<string | null>(null);
  const [tags, setTags] = useState<string[]>([]);
  const [scheduleMode, setScheduleMode] = useState<ScheduleMode>('calendar_day');
  const [calendarDay, setCalendarDay] = useState(5);
  const [businessOrdinal, setBusinessOrdinal] = useState(5);
  const [period, setPeriod] = useState<PeriodOption>('middle');
  const [startsAtMonth, setStartsAtMonth] = useState(() =>
    defaultStartMonthInputForSchedule({ type: 'calendar_day', day: 5 }),
  );
  const [startsAtTouched, setStartsAtTouched] = useState(false);
  const [endsAt, setEndsAt] = useState('');
  const [saving, setSaving] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [togglingId, setTogglingId] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'paused'>('all');

  const [editingId, setEditingId] = useState<string | null>(null);
  const [editDescription, setEditDescription] = useState('');
  const [editAmountStr, setEditAmountStr] = useState('');
  const [editTags, setEditTags] = useState<string[]>([]);
  const [editScheduleMode, setEditScheduleMode] = useState<ScheduleMode>('calendar_day');
  const [editCalendarDay, setEditCalendarDay] = useState(5);
  const [editBusinessOrdinal, setEditBusinessOrdinal] = useState(5);
  const [editPeriod, setEditPeriod] = useState<PeriodOption>('middle');
  const [editCardId, setEditCardId] = useState<string | null>(null);
  const [editStartsAtMonth, setEditStartsAtMonth] = useState('');
  const [editEndsAt, setEditEndsAt] = useState('');
  const [editSaving, setEditSaving] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);
  const [remotePreviewRuleId, setRemotePreviewRuleId] = useState<string | null>(null);
  const [remotePreviewItems, setRemotePreviewItems] = useState<FixedRuleOccurrenceResponse[]>([]);
  const [remotePreviewError, setRemotePreviewError] = useState<string | null>(null);
  const [remotePreviewLoading, setRemotePreviewLoading] = useState(false);

  const amount = parseFloat(amountStr.replace(',', '.'));
  const isCreditCard = category === 'credit_card';
  const cardsLoading = cardsStatus === 'idle' || cardsStatus === 'loading';
  const cardsUnavailable = cardsStatus === 'error';
  const cardMissing = isCreditCard && cardsStatus === 'ready' && cards.length > 0 && !cardId;
  const needsCardRegistration = isCreditCard && cardsStatus === 'ready' && cards.length === 0;
  const cardsBlocked = isCreditCard && (cardsLoading || cardsUnavailable);
  const canSubmit =
    !!description.trim() &&
    Number.isFinite(amount) &&
    amount > 0 &&
    !saving &&
    !cardMissing &&
    !needsCardRegistration &&
    !cardsBlocked;

  const activeCount = useMemo(
    () => fixedTransactions.filter((rule) => rule.active).length,
    [fixedTransactions],
  );
  const pausedCount = fixedTransactions.length - activeCount;
  const monthlyActiveTotals = useMemo(() => {
    let income = 0;
    let expense = 0;
    for (const rule of fixedTransactions) {
      if (!rule.active) continue;
      const kind = CATEGORY_META[rule.category].kind;
      if (kind === 'in') income += rule.amount;
      else expense += rule.amount;
    }
    return { income, expense };
  }, [fixedTransactions]);
  const filteredRules = useMemo(() => {
    if (statusFilter === 'active') return fixedTransactions.filter((rule) => rule.active);
    if (statusFilter === 'paused') return fixedTransactions.filter((rule) => !rule.active);
    return fixedTransactions;
  }, [fixedTransactions, statusFilter]);

  const reload = useCallback(async () => {
    setFixedRulesLoading();
    setActionError(null);
    try {
      const response = await listFixedRules(accessToken, 'all');
      setFixedRules(response.items);
    } catch (err) {
      setFixedRulesError(fixedRulesErrorMessage(err));
    }
  }, [accessToken, setFixedRules, setFixedRulesError, setFixedRulesLoading]);

  useEffect(() => {
    if (fixedRulesStatus === 'idle') {
      const timeout = window.setTimeout(() => {
        void reload();
      }, 0);
      return () => window.clearTimeout(timeout);
    }
  }, [fixedRulesStatus, reload]);

  useEffect(() => {
    if (!initialDraft) return;

    const timeout = window.setTimeout(() => {
      const draftCategory = initialDraft.category ?? 'fixed_expense';
      setCategory(draftCategory);
      setDescription(initialDraft.description ?? '');
      setAmountStr(
        typeof initialDraft.amount === 'number' && Number.isFinite(initialDraft.amount)
          ? String(initialDraft.amount)
          : '',
      );
      setCardId(draftCategory === 'credit_card' ? initialDraft.cardId ?? null : null);
      setTags(initialDraft.tags ?? []);
      setScheduleMode('calendar_day');
      setCalendarDay(5);
      setBusinessOrdinal(5);
      setPeriod('middle');
      setStartsAtTouched(false);
      setStartsAtMonth(defaultStartMonthInputForSchedule({ type: 'calendar_day', day: 5 }));
      setEndsAt('');
      setFormError(null);
      setActionError(null);
      onInitialDraftConsumed?.();
    }, 0);

    return () => window.clearTimeout(timeout);
  }, [initialDraft, onInitialDraftConsumed]);

  useEffect(() => {
    if (!remotePreviewRuleId) return;

    let cancelled = false;
    const now = new Date();
    const from = isoFromDate(now);
    const to = isoFromDate(new Date(now.getFullYear(), now.getMonth() + 6, 0));

    const timeout = window.setTimeout(() => {
      setRemotePreviewLoading(true);
      setRemotePreviewError(null);
      void getFixedRulePreview(accessToken, remotePreviewRuleId, from, to)
        .then((response) => {
          if (!cancelled) setRemotePreviewItems(response.items.slice(0, 4));
        })
        .catch((err) => {
          if (!cancelled) {
            setRemotePreviewItems([]);
            setRemotePreviewError(fixedRulesErrorMessage(err));
          }
        })
        .finally(() => {
          if (!cancelled) setRemotePreviewLoading(false);
        });
    }, 0);

    return () => {
      cancelled = true;
      window.clearTimeout(timeout);
    };
  }, [accessToken, remotePreviewRuleId]);

  function buildSchedule(): FixedTransactionSchedule {
    if (scheduleMode === 'calendar_day') {
      return { type: 'calendar_day', day: calendarDay };
    }

    if (scheduleMode === 'business_day') {
      return { type: 'business_day', ordinal: businessOrdinal };
    }

    return { type: 'period', period };
  }

  function syncStartMonthForSchedule(schedule: FixedTransactionSchedule) {
    if (!startsAtTouched) {
      setStartsAtMonth(defaultStartMonthInputForSchedule(schedule));
    }
  }

  async function submit() {
    setFormError(null);
    setActionError(null);
    if (!canSubmit) return;

    const schedule = buildSchedule();
    const startDate = monthInputToStartsAt(startsAtMonth);
    if (!startDate.value) {
      setFormError(startDate.error ?? 'Informe um mês de início válido.');
      return;
    }

    const startsAt = startDate.value;
    const endDate = normalizeEndsAt(endsAt, startsAt);
    if (endDate.error) {
      setFormError(endDate.error);
      return;
    }

    setSaving(true);
    try {
      const created = await createFixedRule(accessToken, {
        category,
        description: description.trim(),
        amount,
        schedule,
        startsAt,
        endsAt: endDate.value,
        active: true,
        cardId: isCreditCard && cardId ? cardId : null,
        tags,
      });
      addFixedRule(created);
      mergeKnownTags(tags);
      refreshCtx?.refreshAll();
      setCategory('fixed_expense');
      setDescription('');
      setAmountStr('');
      setCardId(null);
      setTags([]);
      setScheduleMode('calendar_day');
      setCalendarDay(5);
      setBusinessOrdinal(5);
      setPeriod('middle');
      setStartsAtTouched(false);
      setStartsAtMonth(defaultStartMonthInputForSchedule({ type: 'calendar_day', day: 5 }));
      setEndsAt('');
    } catch (err) {
      setFormError(fixedRulesErrorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function remove(id: string) {
    if (deletingId) return;

    setActionError(null);
    setDeletingId(id);
    try {
      const rule = await ruleForMutation(id);
      await deleteFixedRule(accessToken, id, rule.etag);
      removeFixedRule(id);
      refreshCtx?.refreshAll();
    } catch (err) {
      if (err instanceof FixedRulesApiError && err.code === 'fixed_rule_not_found') {
        removeFixedRule(id);
        refreshCtx?.refreshAll();
        return;
      }

      setActionError(fixedRulesErrorMessage(err));
    } finally {
      setDeletingId(null);
    }
  }

  async function toggle(ruleId: string, active: boolean) {
    if (togglingId) return;

    setActionError(null);
    setTogglingId(ruleId);
    try {
      const rule = await ruleForMutation(ruleId);
      const updated = active
        ? await pauseFixedRule(accessToken, ruleId, rule.etag)
        : await resumeFixedRule(accessToken, ruleId, rule.etag);
      updateFixedRule(updated);
      refreshCtx?.refreshAll();
    } catch (err) {
      if (err instanceof FixedRulesApiError && err.code === 'fixed_rule_not_found') {
        removeFixedRule(ruleId);
        refreshCtx?.refreshAll();
        return;
      }

      setActionError(fixedRulesErrorMessage(err));
    } finally {
      setTogglingId(null);
    }
  }

  const isLoading = fixedRulesStatus === 'idle' || fixedRulesStatus === 'loading';
  const hasBlockingError = fixedRulesStatus === 'error' && fixedTransactions.length === 0;

  async function ruleForMutation(id: string): Promise<FixedTransactionRule> {
    const cached = fixedTransactions.find((item) => item.id === id);
    if (cached?.etag?.trim()) {
      return cached;
    }

    const fresh = await getFixedRule(accessToken, id);
    updateFixedRule(fresh);
    return fresh;
  }

  function startEditing(rule: typeof fixedTransactions[number]) {
    setEditingId(rule.id);
    setRemotePreviewRuleId(rule.id);
    setRemotePreviewItems([]);
    setRemotePreviewError(null);
    setRemotePreviewLoading(false);
    setEditDescription(rule.description);
    setEditAmountStr(String(rule.amount));
    setEditTags(rule.tags ?? []);
    setEditCardId(rule.cardId ?? null);
    setEditStartsAtMonth(toMonthInput(rule.startsAt));
    setEditEndsAt(rule.endsAt ? toMonthInput(rule.endsAt) : '');
    setEditError(null);
    if (rule.schedule.type === 'calendar_day') {
      setEditScheduleMode('calendar_day');
      setEditCalendarDay(rule.schedule.day);
    } else if (rule.schedule.type === 'business_day') {
      setEditScheduleMode('business_day');
      setEditBusinessOrdinal(rule.schedule.ordinal);
    } else {
      setEditScheduleMode('period');
      setEditPeriod(rule.schedule.period);
    }
  }

  function buildEditSchedule(): FixedTransactionSchedule {
    if (editScheduleMode === 'calendar_day') return { type: 'calendar_day', day: editCalendarDay };
    if (editScheduleMode === 'business_day') return { type: 'business_day', ordinal: editBusinessOrdinal };
    return { type: 'period', period: editPeriod };
  }

  function previewForNewForm() {
    const startDate = monthInputToStartsAt(startsAtMonth);
    if (!startDate.value || !description.trim() || !Number.isFinite(amount) || amount <= 0) {
      return [];
    }

    const endDate = normalizeEndsAt(endsAt, startDate.value);
    if (endDate.error) return [];

    return buildLocalPreview({
      category,
      description: description.trim(),
      amount,
      cardId: category === 'credit_card' ? cardId : null,
      tags,
      schedule: buildSchedule(),
      startsAt: startDate.value,
      endsAt: endDate.value,
    });
  }

  function previewForEditForm(rule: typeof fixedTransactions[number]) {
    const editAmount = parseFloat(editAmountStr.replace(',', '.'));
    const startDate = monthInputToStartsAt(editStartsAtMonth);
    if (!startDate.value || !editDescription.trim() || !Number.isFinite(editAmount) || editAmount <= 0) {
      return [];
    }

    const endDate = normalizeEndsAt(editEndsAt, startDate.value);
    if (endDate.error) return [];

    return buildLocalPreview({
      category: rule.category,
      description: editDescription.trim(),
      amount: editAmount,
      cardId: rule.category === 'credit_card' ? editCardId : null,
      tags: editTags,
      schedule: buildEditSchedule(),
      startsAt: startDate.value,
      endsAt: endDate.value,
    });
  }

  async function submitEdit(id: string) {
    const editAmount = parseFloat(editAmountStr.replace(',', '.'));
    const cachedRule = fixedTransactions.find((item) => item.id === id);
    if (!editDescription.trim() || !Number.isFinite(editAmount) || editAmount <= 0) {
      setEditError('Informe uma descrição e valor válidos.');
      return;
    }
    if (!cachedRule) {
      setEditError('Lançamento fixo não encontrado.');
      return;
    }
    const startDate = monthInputToStartsAt(editStartsAtMonth);
    if (!startDate.value) {
      setEditError(startDate.error ?? 'Informe um mês de início válido.');
      return;
    }

    const startsAt = startDate.value;
    const endDate = normalizeEndsAt(editEndsAt, startsAt);
    if (endDate.error) {
      setEditError(endDate.error);
      return;
    }

    setEditError(null);
    setEditSaving(true);
    try {
      const rule = await ruleForMutation(id);
      const updated = await patchFixedRule(accessToken, id, {
        description: editDescription.trim(),
        amount: editAmount,
        schedule: buildEditSchedule(),
        cardId: editCardId,
        tags: editTags,
        startsAt,
        endsAt: endDate.value,
      }, rule.etag);
      updateFixedRule(updated);
      mergeKnownTags(editTags);
      refreshCtx?.refreshAll();
      setEditingId(null);
      setRemotePreviewRuleId(null);
      setRemotePreviewItems([]);
      setRemotePreviewError(null);
      setRemotePreviewLoading(false);
    } catch (err) {
      setEditError(fixedRulesErrorMessage(err));
    } finally {
      setEditSaving(false);
    }
  }

  return (
    <div className="pb-bottom-nav">
      <div className="mx-auto flex w-full max-w-5xl items-center gap-3 px-4 pb-3 pt-2 sm:px-5 md:px-8 lg:px-10">
        <button
          onClick={onBack}
          className="w-9 h-9 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line transition"
          aria-label="Voltar"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
        </button>
        <div className="text-sm font-semibold tracking-tight text-ink">Lançamentos fixos</div>
      </div>

      <div className="mx-auto grid w-full max-w-5xl gap-4 px-4 sm:px-5 md:grid-cols-[minmax(0,1.05fr)_minmax(320px,0.95fr)] md:px-8 lg:px-10">
        <div className="min-w-0">
          <div className="rounded-2xl bg-paper-card border border-paper-line p-4 shadow-soft">
          <div className="flex items-center justify-between mb-4">
            <div className="text-[10px] uppercase tracking-wider text-ink-muted">
              Novo fixo
            </div>
            <div className="text-[10px] text-ink-muted">
              Mensal
            </div>
          </div>

          <div className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_160px]">
              <div className="min-w-0">
                <label className="text-[11px] uppercase tracking-wider text-ink-muted">
                  Descrição
                </label>
                <input
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Ex.: Aluguel, salário, academia"
                  className="mt-1 w-full h-11 px-3 rounded-xl bg-paper border border-paper-line text-sm text-ink outline-none focus:border-ink/50"
                />
              </div>

              <div className="min-w-0">
                <label className="text-[11px] uppercase tracking-wider text-ink-muted">
                  Valor
                </label>
                <div className="mt-1 flex items-center h-11 px-3 rounded-xl bg-paper border border-paper-line focus-within:border-ink/50">
                  <span className="text-ink-muted text-sm mr-2">R$</span>
                  <input
                    inputMode="decimal"
                    value={amountStr}
                    onChange={(e) =>
                      setAmountStr(e.target.value.replace(/[^0-9.,]/g, ''))
                    }
                    placeholder="0,00"
                    className="flex-1 min-w-0 bg-transparent text-sm text-ink outline-none"
                  />
                </div>
              </div>
            </div>

            <TagInput tags={tags} onChange={setTags} suggestions={knownTags} />

            <div>
              <div className="text-[11px] uppercase tracking-wider text-ink-muted mb-2">
                Tipo
              </div>
              <div className="space-y-3">
                {GROUPS.map((group) => (
                  <div key={group.title}>
                    <div className="text-[11px] text-ink-muted mb-1.5">{group.title}</div>
                    <div className="flex flex-wrap gap-2">
                      {group.options.map((option) => {
                        const meta = CATEGORY_META[option];
                        const selected = category === option;
                        return (
                          <button
                            key={option}
                            onClick={() => {
                              setCategory(option);
                              if (option !== 'credit_card') setCardId(null);
                            }}
                            className={`px-3 h-9 rounded-full text-xs font-medium border transition inline-flex items-center gap-1.5 ${
                              selected
                                ? 'bold-surface border-transparent'
                                : 'bg-paper border-paper-line text-ink hover:border-ink/40'
                            }`}
                          >
                            <span className={selected ? 'text-white' : meta.color}>
                              <CategoryIcon category={option} size={14} />
                            </span>
                            {meta.label}
                          </button>
                        );
                      })}
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {isCreditCard && (
              <div>
                <div className="flex items-center justify-between mb-2">
                  <div className="text-[11px] uppercase tracking-wider text-ink-muted">
                    Cartão
                  </div>
                  {cardsStatus === 'ready' && cards.length > 0 && (
                    <span className="text-[10px] text-ink-muted">
                      {cards.length} cadastrado{cards.length > 1 ? 's' : ''}
                    </span>
                  )}
                </div>
                {cardsLoading ? (
                  <div className="rounded-xl border border-dashed border-paper-line p-3 text-[11px] text-ink-muted">
                    Carregando cartões...
                  </div>
                ) : cardsUnavailable ? (
                  <div className="rounded-xl border border-dashed border-paper-line p-3 text-[11px] text-ink-muted">
                    {cardsError ?? 'Não foi possível carregar os cartões.'}
                  </div>
                ) : cards.length === 0 ? (
                  <div className="rounded-xl border border-dashed border-paper-line p-3 text-[11px] text-ink-muted">
                    Cadastre um cartão em “Cartões” para criar um lançamento fixo no crédito.
                  </div>
                ) : (
                  <div className="flex flex-wrap gap-2">
                    {cards.map((card) => {
                      const selected = cardId === card.id;
                      return (
                        <button
                          key={card.id}
                          onClick={() => setCardId(card.id)}
                          className={`px-3 h-9 rounded-full text-xs font-medium border transition inline-flex items-center gap-1.5 ${
                            selected
                              ? 'bold-surface border-transparent'
                              : 'bg-paper border-paper-line text-ink hover:border-ink/40'
                          }`}
                        >
                          <span className={selected ? 'text-white' : 'text-accent-neg'}>
                            <CategoryIcon category="credit_card" size={14} />
                          </span>
                          {card.title}
                        </button>
                      );
                    })}
                  </div>
                )}
              </div>
            )}

            <div>
              <div className="text-[11px] uppercase tracking-wider text-ink-muted mb-2">
                Recorrência
              </div>
              <div className="grid grid-cols-3 gap-1 rounded-xl bg-paper p-1 border border-paper-line">
                <ScheduleTab
                  label="Dia"
                  active={scheduleMode === 'calendar_day'}
                  onClick={() => {
                    setScheduleMode('calendar_day');
                    syncStartMonthForSchedule({ type: 'calendar_day', day: calendarDay });
                  }}
                />
                <ScheduleTab
                  label="Dia útil"
                  active={scheduleMode === 'business_day'}
                  onClick={() => {
                    setScheduleMode('business_day');
                    syncStartMonthForSchedule({ type: 'business_day', ordinal: businessOrdinal });
                  }}
                />
                <ScheduleTab
                  label="Período"
                  active={scheduleMode === 'period'}
                  onClick={() => {
                    setScheduleMode('period');
                    syncStartMonthForSchedule({ type: 'period', period });
                  }}
                />
              </div>

              <div className="mt-3">
                {scheduleMode === 'calendar_day' && (
                  <SelectField
                    label="Dia do mês"
                    value={calendarDay}
                    onChange={(value) => {
                      setCalendarDay(value);
                      syncStartMonthForSchedule({ type: 'calendar_day', day: value });
                    }}
                    options={Array.from({ length: 31 }, (_, index) => ({
                      value: index + 1,
                      label: String(index + 1).padStart(2, '0'),
                    }))}
                  />
                )}

                {scheduleMode === 'business_day' && (
                  <SelectField
                    label="Dia útil"
                    value={businessOrdinal}
                    onChange={(value) => {
                      setBusinessOrdinal(value);
                      syncStartMonthForSchedule({ type: 'business_day', ordinal: value });
                    }}
                    options={Array.from({ length: 10 }, (_, index) => ({
                      value: index + 1,
                      label: `${index + 1}º dia útil`,
                    }))}
                  />
                )}

                {scheduleMode === 'period' && (
                  <div className="grid grid-cols-3 gap-2">
                    {PERIOD_OPTIONS.map((option) => (
                      <button
                        key={option.value}
                        onClick={() => {
                          setPeriod(option.value);
                          syncStartMonthForSchedule({ type: 'period', period: option.value });
                        }}
                        className={`h-10 rounded-xl text-xs font-medium border transition ${
                          period === option.value
                            ? 'bold-surface border-transparent'
                            : 'bg-paper border-paper-line text-ink-muted hover:text-ink'
                        }`}
                      >
                        {option.label}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            </div>

            <div className="rounded-2xl border border-paper-line bg-paper/40 p-3">
              <div className="flex items-center justify-between mb-2">
                <span className="text-[11px] uppercase tracking-wider text-ink-muted">Período de vigência</span>
                <span className="text-[10px] text-ink-muted">Apenas mês e ano</span>
              </div>
              <div className="grid gap-3 sm:grid-cols-2">
                <div>
                  <span className="text-[11px] font-medium text-ink-muted">Começa em</span>
                  <div className="mt-1">
                    <MonthYearPicker
                      value={startsAtMonth}
                      onChange={(next) => {
                        setStartsAtTouched(true);
                        setStartsAtMonth(next);
                        if (endsAt && next && endsAt < next) setEndsAt('');
                      }}
                      placeholder="Selecionar mês inicial"
                      ariaLabel="Mês inicial"
                    />
                  </div>
                  <span className="mt-1 block text-[10px] text-ink-muted">Mês inicial</span>
                </div>

                <div>
                  <span className="text-[11px] font-medium text-ink-muted">Termina em</span>
                  <div className="mt-1">
                    <MonthYearPicker
                      value={endsAt}
                      onChange={setEndsAt}
                      min={startsAtMonth || undefined}
                      placeholder="Sem data final"
                      clearable
                      ariaLabel="Mês final"
                    />
                  </div>
                  <span className="mt-1 block text-[10px] text-ink-muted">Opcional</span>
                </div>
              </div>
            </div>

            <FixedRulePreviewPanel
              title="Próximas ocorrências"
              items={previewForNewForm()}
              emptyText="Preencha descrição, valor e período para ver a prévia."
              cardTitleForId={(id) => cards.find((item) => item.id === id)?.title ?? null}
            />
          </div>

          <button
            onClick={() => void submit()}
            disabled={!canSubmit}
            className="mt-5 w-full h-11 rounded-xl bold-surface text-sm font-medium disabled:opacity-40 transition"
          >
            {saving ? 'Salvando...' : 'Salvar lançamento fixo'}
          </button>
          {formError && (
            <div className="mt-3 rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">
              {formError}
            </div>
          )}
          </div>
        </div>

        <div className="min-w-0">
          <div className="flex items-center justify-between mb-2 px-1">
            <div className="text-[10px] uppercase tracking-wider text-ink-muted">
              Fixos ativos
            </div>
            <div className="text-[10px] text-ink-muted">
              {activeCount}/{fixedTransactions.length}
            </div>
          </div>

          {fixedTransactions.length > 0 && (
            <>
              <div className="mb-3 grid grid-cols-2 gap-2">
                <div className="rounded-2xl border border-paper-line bg-paper-card px-3 py-2.5 shadow-soft">
                  <div className="text-[10px] uppercase tracking-wider text-ink-muted">Entradas / mês</div>
                  <div className="mt-0.5 text-base font-semibold text-accent-pos tabular-nums">
                    {formatBRL(monthlyActiveTotals.income)}
                  </div>
                </div>
                <div className="rounded-2xl border border-paper-line bg-paper-card px-3 py-2.5 shadow-soft">
                  <div className="text-[10px] uppercase tracking-wider text-ink-muted">Saídas / mês</div>
                  <div className="mt-0.5 text-base font-semibold text-accent-neg tabular-nums">
                    {formatBRL(monthlyActiveTotals.expense)}
                  </div>
                </div>
              </div>

              <div className="mb-3 inline-flex w-full gap-1 rounded-full bg-paper-card border border-paper-line p-0.5">
                <FilterChip label={`Todos (${fixedTransactions.length})`} active={statusFilter === 'all'} onClick={() => setStatusFilter('all')} />
                <FilterChip label={`Ativos (${activeCount})`} active={statusFilter === 'active'} onClick={() => setStatusFilter('active')} />
                <FilterChip label={`Pausados (${pausedCount})`} active={statusFilter === 'paused'} onClick={() => setStatusFilter('paused')} />
              </div>
            </>
          )}

          {actionError && (
            <div className="mb-3 rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">
              {actionError}
            </div>
          )}

          {fixedRulesStatus === 'error' && fixedTransactions.length > 0 && (
            <div className="mb-3 rounded-xl border border-paper-line bg-paper-card px-3 py-2 text-[12px] text-ink-muted">
              {fixedRulesError ?? 'Não foi possível atualizar os lançamentos fixos.'}
            </div>
          )}

          {isLoading ? (
            <div className="rounded-2xl bg-paper-card border border-paper-line p-5 text-center text-sm text-ink-muted shadow-soft">
              Carregando lançamentos fixos...
            </div>
          ) : hasBlockingError ? (
            <div className="rounded-2xl bg-paper-card border border-paper-line p-5 text-center shadow-soft">
              <div className="text-sm text-ink-muted">
                {fixedRulesError ?? 'Não foi possível carregar os lançamentos fixos.'}
              </div>
              <button
                onClick={() => void reload()}
                className="mt-3 h-9 px-4 rounded-xl border border-paper-line text-xs font-medium text-ink hover:bg-paper-line transition"
              >
                Tentar novamente
              </button>
            </div>
          ) : fixedTransactions.length === 0 ? (
            <div className="rounded-2xl bg-paper-card border border-paper-line p-5 text-center text-sm text-ink-muted shadow-soft">
              Nenhum lançamento fixo cadastrado.
            </div>
          ) : filteredRules.length === 0 ? (
            <div className="rounded-2xl bg-paper-card border border-paper-line p-5 text-center text-sm text-ink-muted shadow-soft">
              {statusFilter === 'active' ? 'Nenhum lançamento fixo ativo.' : 'Nenhum lançamento fixo pausado.'}
            </div>
          ) : (
            <div className="space-y-2">
              {filteredRules.map((rule) => {
                const meta = CATEGORY_META[rule.category];
                const nextDate = nextFixedTransactionDate(rule);
                const ruleCard = rule.cardId ? cards.find((c) => c.id === rule.cardId) : null;
                return (
                  <div key={rule.id}
                    className="rounded-2xl bg-paper-card border border-paper-line p-4 shadow-soft"
                  >
                    {editingId === rule.id ? (
                      <div className="space-y-3">
                        <div className="text-[10px] uppercase tracking-wider text-ink-muted mb-1">Editar lançamento fixo</div>
                        <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_140px]">
                          <div>
                            <label className="text-[11px] uppercase tracking-wider text-ink-muted">Descrição</label>
                            <input
                              value={editDescription}
                              onChange={(e) => setEditDescription(e.target.value)}
                              className="mt-1 w-full h-10 px-3 rounded-xl bg-paper border border-paper-line text-sm text-ink outline-none focus:border-ink/50"
                            />
                          </div>
                          <div>
                            <label className="text-[11px] uppercase tracking-wider text-ink-muted">Valor</label>
                            <div className="mt-1 flex items-center h-10 px-3 rounded-xl bg-paper border border-paper-line focus-within:border-ink/50">
                              <span className="text-ink-muted text-sm mr-2">R$</span>
                              <input
                                inputMode="decimal"
                                value={editAmountStr}
                                onChange={(e) => setEditAmountStr(e.target.value.replace(/[^0-9.,]/g, ''))}
                                className="flex-1 min-w-0 bg-transparent text-sm text-ink outline-none"
                              />
                            </div>
                          </div>
                        </div>

                        <TagInput tags={editTags} onChange={setEditTags} suggestions={knownTags} />

                        <div>
                          <div className="text-[11px] uppercase tracking-wider text-ink-muted mb-1">Recorrência</div>
                          <div className="grid grid-cols-3 gap-1 rounded-xl bg-paper p-1 border border-paper-line">
                            <ScheduleTab label="Dia" active={editScheduleMode === 'calendar_day'} onClick={() => setEditScheduleMode('calendar_day')} />
                            <ScheduleTab label="Dia útil" active={editScheduleMode === 'business_day'} onClick={() => setEditScheduleMode('business_day')} />
                            <ScheduleTab label="Período" active={editScheduleMode === 'period'} onClick={() => setEditScheduleMode('period')} />
                          </div>
                          <div className="mt-2">
                            {editScheduleMode === 'calendar_day' && (
                              <SelectField label="Dia do mês" value={editCalendarDay} onChange={setEditCalendarDay} options={Array.from({ length: 31 }, (_, i) => ({ value: i + 1, label: String(i + 1).padStart(2, '0') }))} />
                            )}
                            {editScheduleMode === 'business_day' && (
                              <SelectField label="Dia útil" value={editBusinessOrdinal} onChange={setEditBusinessOrdinal} options={Array.from({ length: 10 }, (_, i) => ({ value: i + 1, label: `${i + 1}º dia útil` }))} />
                            )}
                            {editScheduleMode === 'period' && (
                              <div className="grid grid-cols-3 gap-2">
                                {PERIOD_OPTIONS.map((option) => (
                                  <button key={option.value} onClick={() => setEditPeriod(option.value)} className={`h-10 rounded-xl text-xs font-medium border transition ${editPeriod === option.value ? 'bold-surface border-transparent' : 'bg-paper border-paper-line text-ink-muted hover:text-ink'}`}>{option.label}</button>
                                ))}
                              </div>
                            )}
                          </div>
                        </div>

                        <div className="rounded-2xl border border-paper-line bg-paper/40 p-3">
                          <div className="flex items-center justify-between mb-2">
                            <span className="text-[11px] uppercase tracking-wider text-ink-muted">Período de vigência</span>
                            <span className="text-[10px] text-ink-muted">Apenas mês e ano</span>
                          </div>
                          <div className="grid gap-3 sm:grid-cols-2">
                            <div>
                              <span className="text-[11px] font-medium text-ink-muted">Começa em</span>
                              <div className="mt-1">
                                <MonthYearPicker
                                  value={editStartsAtMonth}
                                  onChange={(next) => {
                                    setEditStartsAtMonth(next);
                                    if (editEndsAt && next && editEndsAt < next) setEditEndsAt('');
                                  }}
                                  placeholder="Selecionar mês inicial"
                                  size="sm"
                                  ariaLabel="Mês inicial"
                                />
                              </div>
                              <span className="mt-1 block text-[10px] text-ink-muted">Mês inicial</span>
                            </div>

                            <div>
                              <span className="text-[11px] font-medium text-ink-muted">Termina em</span>
                              <div className="mt-1">
                                <MonthYearPicker
                                  value={editEndsAt}
                                  onChange={setEditEndsAt}
                                  min={editStartsAtMonth || undefined}
                                  placeholder="Sem data final"
                                  clearable
                                  size="sm"
                                  ariaLabel="Mês final"
                                />
                              </div>
                              <span className="mt-1 block text-[10px] text-ink-muted">Opcional</span>
                            </div>
                          </div>
                        </div>

                        {rule.category === 'credit_card' && cards.length > 0 && (
                          <div>
                            <div className="text-[11px] uppercase tracking-wider text-ink-muted mb-1">Cartão</div>
                            <div className="flex flex-wrap gap-2">
                              {cards.map((card) => (
                                <button key={card.id} onClick={() => setEditCardId(card.id)} className={`px-3 h-9 rounded-full text-xs font-medium border transition ${editCardId === card.id ? 'bold-surface border-transparent' : 'bg-paper border-paper-line text-ink hover:border-ink/40'}`}>{card.title}</button>
                              ))}
                            </div>
                          </div>
                        )}

                        <FixedRulePreviewPanel
                          title="Preview salvo"
                          items={remotePreviewRuleId === rule.id ? remotePreviewItems : []}
                          loading={remotePreviewRuleId === rule.id && remotePreviewLoading}
                          error={remotePreviewRuleId === rule.id ? remotePreviewError : null}
                          emptyText="Sem ocorrências retornadas pelo servidor."
                          cardTitleForId={(id) => cards.find((item) => item.id === id)?.title ?? null}
                        />

                        <FixedRulePreviewPanel
                          title="Com edição atual"
                          items={previewForEditForm(rule)}
                          emptyText="Ajuste descrição, valor e período para ver a prévia."
                          cardTitleForId={(id) => cards.find((item) => item.id === id)?.title ?? null}
                        />

                        {editError && (
                          <div className="rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">{editError}</div>
                        )}

                        <div className="flex gap-2 justify-end">
                          <button onClick={() => { setEditingId(null); setRemotePreviewRuleId(null); setRemotePreviewItems([]); setRemotePreviewError(null); setRemotePreviewLoading(false); }} disabled={editSaving} className="h-9 px-4 rounded-xl border border-paper-line text-xs font-medium text-ink-muted hover:bg-paper-line disabled:opacity-40 transition">Cancelar</button>
                          <button onClick={() => void submitEdit(rule.id)} disabled={editSaving} className="h-9 px-4 rounded-xl bold-surface text-xs font-medium disabled:opacity-40 transition">{editSaving ? 'Salvando...' : 'Salvar'}</button>
                        </div>
                      </div>
                    ) : (
                      <div className="flex items-start gap-3">
                        <div className={`w-9 h-9 rounded-full bg-paper-line grid place-items-center shrink-0 ${meta.color}`}>
                          <CategoryIcon category={rule.category} size={16} />
                        </div>
                        <div className="flex-1 min-w-0 text-left">
                          <div className="flex flex-col gap-2 min-[380px]:flex-row min-[380px]:items-start min-[380px]:justify-between">
                            <div className="min-w-0">
                              <div className="text-sm font-medium text-ink truncate">
                                {rule.description}
                              </div>
                              <div className="mt-0.5 text-[11px] text-ink-muted">
                                {meta.label}
                                {' · Desde '}
                                {formatMonth(rule.startsAt)}
                                {ruleCard ? ` · ${ruleCard.title}` : ''} · {describeFixedTransactionSchedule(rule.schedule)}
                                {rule.endsAt ? ` · Até ${formatDate(rule.endsAt)}` : ''}
                              </div>
                              {rule.tags && rule.tags.length > 0 && (
                                <div className="mt-2 flex flex-wrap gap-1.5">
                                  {rule.tags.map((tag) => (
                                    <span
                                      key={tag}
                                      className="inline-flex h-5 max-w-full items-center rounded-full bg-paper-line px-2 text-[10px] font-medium text-ink-muted"
                                    >
                                      <span className="truncate">{tag}</span>
                                    </span>
                                  ))}
                                </div>
                              )}
                            </div>
                            <div className={`text-sm font-semibold shrink-0 ${meta.color}`}>
                              {formatBRL(rule.amount)}
                            </div>
                          </div>

                          <div className="mt-3 flex flex-col gap-2 min-[380px]:flex-row min-[380px]:items-center min-[380px]:justify-between">
                            <div className="text-[11px] text-ink-muted">
                              {nextDate
                                ? `Próximo: ${formatDate(nextDate)}`
                                : 'Sem próxima data'}
                            </div>
                            <div className="flex items-center gap-1 self-end min-[380px]:self-auto">
                              <button
                                onClick={() => startEditing(rule)}
                                className="w-8 h-8 rounded-full grid place-items-center text-ink-muted hover:text-ink hover:bg-paper-line transition"
                                aria-label="Editar lançamento fixo"
                              >
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                              </button>
                              <button
                                onClick={() => void toggle(rule.id, rule.active)}
                                disabled={togglingId === rule.id}
                                className={`h-8 px-3 rounded-full text-[11px] font-medium transition ${
                                  rule.active
                                    ? 'bg-accent-pos/10 text-accent-pos'
                                    : 'bg-paper-line text-ink-muted'
                                } disabled:opacity-50`}
                              >
                                {togglingId === rule.id ? '...' : rule.active ? 'Ativo' : 'Pausado'}
                              </button>
                              <button
                                onClick={() => void remove(rule.id)}
                                disabled={deletingId === rule.id}
                                className="w-8 h-8 rounded-full grid place-items-center text-ink-muted hover:text-accent-neg hover:bg-paper-line disabled:opacity-40 transition"
                                aria-label="Remover lançamento fixo"
                              >
                                {deletingId === rule.id ? (
                                  <span className="text-[10px]">...</span>
                                ) : (
                                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                                )}
                              </button>
                            </div>
                          </div>
                        </div>
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function FixedRulePreviewPanel({
  title,
  items,
  loading = false,
  error,
  emptyText,
  cardTitleForId,
}: {
  title: string;
  items: FixedRuleOccurrenceResponse[];
  loading?: boolean;
  error?: string | null;
  emptyText: string;
  cardTitleForId: (id: string) => string | null;
}) {
  return (
    <div className="rounded-2xl border border-paper-line bg-paper/40 p-3">
      <div className="mb-2 flex items-center justify-between gap-2">
        <span className="text-[11px] uppercase tracking-wider text-ink-muted">{title}</span>
        {loading && <span className="text-[10px] text-ink-muted">Carregando...</span>}
      </div>
      {error ? (
        <div className="rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[11px] text-accent-neg">
          {error}
        </div>
      ) : items.length === 0 ? (
        <div className="text-[11px] text-ink-muted">{loading ? 'Consultando preview...' : emptyText}</div>
      ) : (
        <div className="space-y-2">
          {items.map((item) => {
            const cardTitle = item.cardId ? cardTitleForId(item.cardId) : null;
            return (
              <div
                key={`${item.occurrenceDate}:${item.cardId ?? 'cash'}:${item.description}`}
                className="flex items-start justify-between gap-3 rounded-xl bg-paper-card px-3 py-2"
              >
                <div className="min-w-0">
                  <div className="text-[12px] font-medium text-ink truncate">
                    {formatDate(item.occurrenceDate)}
                  </div>
                  <div className="mt-0.5 text-[11px] text-ink-muted truncate">
                    {item.description}
                    {cardTitle ? ` - ${cardTitle}` : ''}
                  </div>
                </div>
                <div className="shrink-0 text-[12px] font-semibold text-ink tabular-nums">
                  {formatBRL(item.amount)}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

function buildLocalPreview(
  rule: {
    category: TransactionCategory;
    description: string;
    amount: number;
    cardId: string | null;
    tags: string[];
    schedule: FixedTransactionSchedule;
    startsAt: string;
    endsAt: string | null;
  },
  limit = 4,
): FixedRuleOccurrenceResponse[] {
  const startsAt = parseDateInput(rule.startsAt);
  if (!startsAt) return [];

  const endsAt = parseDateInput(rule.endsAt ?? '');
  const items: FixedRuleOccurrenceResponse[] = [];
  let year = startsAt.getFullYear();
  let monthIdx = startsAt.getMonth();

  for (let guard = 0; guard < 48 && items.length < limit; guard++) {
    const day = resolveFixedTransactionDay(rule.schedule, year, monthIdx);
    const occurrence = new Date(year, monthIdx, day);
    if (occurrence.getTime() >= startsAt.getTime()) {
      if (endsAt && occurrence.getTime() > endsAt.getTime()) break;

      const occurrenceDate = isoFromDate(occurrence);
      items.push({
        occurrenceDate,
        yearMonth: occurrenceDate.slice(0, 7),
        category: rule.category,
        description: rule.description,
        amount: rule.amount,
        cardId: rule.category === 'credit_card' ? rule.cardId : null,
        tags: rule.tags,
      });
    }

    monthIdx += 1;
    if (monthIdx > 11) {
      monthIdx = 0;
      year += 1;
    }
  }

  return items;
}

function ScheduleTab({
  label,
  active,
  onClick,
}: {
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      className={`h-9 rounded-lg text-[11px] font-medium transition ${
        active ? 'bold-surface' : 'text-ink-muted hover:text-ink'
      }`}
    >
      {label}
    </button>
  );
}

function FilterChip({
  label,
  active,
  onClick,
}: {
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={`flex-1 rounded-full px-3 py-1.5 text-[12px] font-medium transition-all tap-surface ${
        active ? 'bg-accent-invest text-white shadow-soft' : 'text-ink-muted hover:text-ink'
      }`}
    >
      {label}
    </button>
  );
}

function SelectField({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: number;
  onChange: (value: number) => void;
  options: { value: number; label: string }[];
}) {
  return (
    <label className="block">
      <span className="text-[11px] uppercase tracking-wider text-ink-muted">
        {label}
      </span>
      <select
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
        className="mt-1 w-full h-11 px-3 rounded-xl bg-paper border border-paper-line text-sm text-ink outline-none focus:border-ink/50"
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}

function formatDate(value: string) {
  return new Date(`${value}T00:00`).toLocaleDateString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
}

function formatMonth(value: string) {
  const monthInput = toMonthInput(value);
  const [year, month] = monthInput.split('-').map(Number);
  return new Date(year, month - 1, 1).toLocaleDateString('pt-BR', {
    month: '2-digit',
    year: 'numeric',
  });
}

function defaultStartMonthInput(date: Date = new Date()) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
}

function defaultStartMonthInputForSchedule(schedule: FixedTransactionSchedule, now: Date = new Date()) {
  const year = now.getFullYear();
  const monthIdx = now.getMonth();
  const occurrenceDay = resolveFixedTransactionDay(schedule, year, monthIdx);
  const occurrence = new Date(year, monthIdx, occurrenceDay);
  const today = new Date(year, monthIdx, now.getDate());
  const startMonth = occurrence.getTime() < today.getTime()
    ? new Date(year, monthIdx + 1, 1)
    : new Date(year, monthIdx, 1);

  return defaultStartMonthInput(startMonth);
}

function toMonthInput(value: string) {
  const match = /^(\d{4})-(\d{2})-\d{2}$/.exec(value);
  return match ? `${match[1]}-${match[2]}` : defaultStartMonthInput();
}

function monthInputToStartsAt(value: string): { value: string | null; error: string | null } {
  const normalized = value.trim();
  const match = /^(\d{4})-(\d{2})$/.exec(normalized);
  if (!match) {
    return { value: null, error: 'Informe um mês de início válido.' };
  }

  const year = Number(match[1]);
  const month = Number(match[2]);
  if (!Number.isInteger(year) || year < 1 || month < 1 || month > 12) {
    return { value: null, error: 'Informe um mês de início válido.' };
  }

  return { value: `${match[1]}-${match[2]}-01`, error: null };
}

function normalizeEndsAt(value: string, startsAt: string): { value: string | null; error: string | null } {
  const normalized = value.trim();
  if (!normalized) return { value: null, error: null };

  const monthMatch = /^(\d{4})-(\d{2})$/.exec(normalized);
  let endDate: Date | null = null;
  let endIso: string | null = null;
  if (monthMatch) {
    const year = Number(monthMatch[1]);
    const month = Number(monthMatch[2]);
    if (!Number.isInteger(year) || year < 1 || month < 1 || month > 12) {
      return { value: null, error: 'Informe um mês final válido.' };
    }
    const lastDay = new Date(year, month, 0).getDate();
    endDate = new Date(year, month - 1, lastDay);
    endIso = `${monthMatch[1]}-${monthMatch[2]}-${String(lastDay).padStart(2, '0')}`;
  } else {
    endDate = parseDateInput(normalized);
    endIso = normalized;
  }

  const startDate = parseDateInput(startsAt);
  if (!endDate || !startDate || !endIso) {
    return { value: null, error: 'Informe um mês final válido.' };
  }

  if (endDate.getTime() < startDate.getTime()) {
    return { value: null, error: 'O mês final não pode ser anterior ao início.' };
  }

  return { value: endIso, error: null };
}

function parseDateInput(value: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;

  const year = Number(match[1]);
  const monthIdx = Number(match[2]) - 1;
  const day = Number(match[3]);
  const date = new Date(year, monthIdx, day);

  if (
    date.getFullYear() !== year ||
    date.getMonth() !== monthIdx ||
    date.getDate() !== day
  ) {
    return null;
  }

  return date;
}

function isoFromDate(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

function fixedRulesErrorMessage(err: unknown) {
  if (err instanceof FixedRulesApiError) {
    if (err.code === 'invalid_description') return 'Informe uma descrição válida.';
    if (err.code === 'invalid_amount') return 'Informe um valor maior que zero.';
    if (err.code === 'invalid_tags') return 'Informe tags válidas.';
    if (err.code === 'invalid_category') return 'Selecione um tipo válido.';
    if (err.code === 'invalid_card_id') return 'Selecione um cartão válido.';
    if (err.code === 'card_not_found') return 'O cartão selecionado não está disponível.';
    if (err.code === 'invalid_schedule') return 'Configure uma recorrência válida.';
    if (err.code === 'invalid_date_range') return 'Informe uma data final válida.';
    if (err.code === 'fixed_rule_expired') return 'Esta regra está expirada.';
    if (err.code === 'fixed_rule_conflict' || err.code === 'precondition_failed') {
      return 'Esta regra mudou em outro dispositivo. Atualize e tente novamente.';
    }
    if (err.code === 'unauthorized') return 'Sua sessão expirou. Entre novamente.';
    if (err.code === 'rate_limited') return 'Muitas tentativas. Aguarde um pouco e tente de novo.';
    if (err.code === 'fixed_rules_dependency_unavailable') return 'Não foi possível acessar o serviço de lançamentos fixos agora.';
    return err.message || 'Não foi possível concluir a operação.';
  }

  return err instanceof Error ? err.message : 'Não foi possível concluir a operação.';
}
