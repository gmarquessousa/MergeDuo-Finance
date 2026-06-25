export const MAX_TAGS = 20;
export const MAX_TAG_LENGTH = 40;

export function normalizeTag(value: string): string {
  return value.trim().toLowerCase();
}

export function normalizeTags(values: readonly string[] = []): string[] {
  const seen = new Set<string>();
  const tags: string[] = [];

  for (const value of values) {
    const tag = normalizeTag(value);
    if (!tag || seen.has(tag)) continue;
    seen.add(tag);
    tags.push(tag);
  }

  return tags;
}

export function parseTagInput(value: string): string[] {
  return normalizeTags(value.split(','));
}

export function validateTags(tags: readonly string[]): string | null {
  if (tags.length > MAX_TAGS) return `Máximo de ${MAX_TAGS} tags.`;
  if (tags.some((tag) => tag.length > MAX_TAG_LENGTH)) {
    return `Tags devem ter até ${MAX_TAG_LENGTH} caracteres.`;
  }

  return null;
}

export function appendTagInput(current: readonly string[], input: string): { tags: string[]; error: string | null } {
  const incoming = parseTagInput(input);
  const tags = normalizeTags([...current, ...incoming]);
  return { tags, error: validateTags(tags) };
}
