import { describe, expect, it } from 'vitest';
import { appendTagInput, normalizeTags, parseTagInput, validateTags } from '../tags';

describe('tags', () => {
  it('normalizes, trims and removes duplicates', () => {
    expect(normalizeTags([' Casa ', 'casa', 'Mercado'])).toEqual(['casa', 'mercado']);
  });

  it('parses comma separated input', () => {
    expect(parseTagInput('Casa, Mercado, ,casa')).toEqual(['casa', 'mercado']);
  });

  it('appends tags with validation', () => {
    expect(appendTagInput(['casa'], 'Mercado, casa')).toEqual({
      tags: ['casa', 'mercado'],
      error: null,
    });
  });

  it('rejects limits', () => {
    expect(validateTags(Array.from({ length: 21 }, (_, index) => `tag-${index}`))).toBe('Máximo de 20 tags.');
    expect(validateTags(['x'.repeat(41)])).toBe('Tags devem ter até 40 caracteres.');
  });
});
