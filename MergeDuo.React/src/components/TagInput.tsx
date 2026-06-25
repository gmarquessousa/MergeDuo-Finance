import { useMemo, useState } from 'react';
import { appendTagInput, normalizeTag, normalizeTags, validateTags } from '../tags';

interface Props {
  tags: string[];
  onChange: (tags: string[]) => void;
  label?: string;
  suggestions?: string[];
}

export function TagInput({ tags, onChange, label = 'Tags', suggestions = [] }: Props) {
  const [draft, setDraft] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [focused, setFocused] = useState(false);

  const filteredSuggestions = useMemo(() => {
    const selected = new Set(tags.map(normalizeTag));
    const query = normalizeTag(draft);
    const available = normalizeTags(suggestions).filter((tag) => !selected.has(tag));

    if (!query) return available.slice(0, 8);

    // Rank: prefix matches first, then substring matches; preserve incoming
    // order (which already reflects usage frequency) within each bucket.
    const prefix: string[] = [];
    const contains: string[] = [];
    for (const tag of available) {
      if (tag.startsWith(query)) prefix.push(tag);
      else if (tag.includes(query)) contains.push(tag);
    }
    return [...prefix, ...contains].slice(0, 8);
  }, [draft, suggestions, tags]);

  const canCreateDraft = useMemo(() => {
    const candidate = normalizeTag(draft);
    if (!candidate) return false;
    if (tags.map(normalizeTag).includes(candidate)) return false;
    return !filteredSuggestions.includes(candidate);
  }, [draft, filteredSuggestions, tags]);

  const showSuggestions = focused && (filteredSuggestions.length > 0 || canCreateDraft);

  function commit(value = draft) {
    if (!value.trim()) return;
    const result = appendTagInput(tags, value);
    if (result.error) {
      setError(result.error);
      return;
    }

    onChange(result.tags);
    setDraft('');
    setError(null);
  }

  function remove(tag: string) {
    const next = tags.filter((item) => item !== tag);
    onChange(next);
    setError(validateTags(next));
  }

  return (
    <div>
      <label className="text-[11px] uppercase tracking-wider text-ink-muted">{label}</label>
      <div className="mt-1.5 rounded-2xl border border-paper-line bg-paper px-2 py-2 transition-colors focus-within:border-accent-invest/60 focus-within:bg-accent-invest/[0.03]">
        {tags.length > 0 && (
          <div className="mb-2 flex flex-wrap gap-1.5">
            {tags.map((tag) => (
              <span
                key={tag}
                className="inline-flex h-7 max-w-full items-center gap-1 rounded-full bg-accent-invest/10 px-2.5 text-[11px] font-medium text-accent-invest"
              >
                <span className="truncate">{tag}</span>
                <button
                  type="button"
                  onClick={() => remove(tag)}
                  className="-mr-1 grid h-5 w-5 shrink-0 place-items-center rounded-full text-accent-invest/70 transition hover:bg-accent-invest/15 hover:text-accent-invest"
                  aria-label={`Remover tag ${tag}`}
                >
                  <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                </button>
              </span>
            ))}
          </div>
        )}
        <input
          value={draft}
          onChange={(event) => {
            const value = event.target.value;
            if (value.includes(',')) {
              commit(value);
              return;
            }

            setDraft(value);
            setError(null);
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter' || event.key === ',') {
              event.preventDefault();
              commit();
            }
          }}
          onBlur={() => {
            commit();
            setFocused(false);
          }}
          onFocus={() => setFocused(true)}
          placeholder="casa, mercado"
          className="h-8 w-full bg-transparent px-1 text-sm text-ink outline-none placeholder:text-ink-muted/60"
        />
      </div>
      {showSuggestions && (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {canCreateDraft && (
            <button
              type="button"
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => {
                commit();
                setFocused(true);
              }}
              className="inline-flex h-7 items-center gap-1 rounded-full border border-accent-invest/30 bg-accent-invest/8 px-2.5 text-[11px] font-medium text-accent-invest transition hover:bg-accent-invest/15 tap-surface"
            >
              <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
              {normalizeTag(draft)}
            </button>
          )}
          {filteredSuggestions.map((suggestion) => (
            <button
              key={suggestion}
              type="button"
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => {
                commit(suggestion);
                setFocused(true);
              }}
              className="inline-flex h-7 items-center rounded-full border border-paper-line bg-paper-card px-2.5 text-[11px] font-medium text-ink-muted transition hover:border-accent-invest/40 hover:text-accent-invest tap-surface"
            >
              {suggestion}
            </button>
          ))}
        </div>
      )}
      {error && (
        <div className="mt-1 text-[11px] text-accent-neg">{error}</div>
      )}
    </div>
  );
}
