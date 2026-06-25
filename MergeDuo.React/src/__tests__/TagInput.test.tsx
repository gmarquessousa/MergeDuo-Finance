import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { TagInput } from '../components/TagInput';

describe('TagInput', () => {
  it('filters suggestions and skips already selected tags', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(
      <TagInput
        tags={['casa']}
        onChange={onChange}
        suggestions={['casa', 'mercado', 'uber']}
      />,
    );

    const input = screen.getByPlaceholderText('casa, mercado');
    await user.click(input);

    expect(screen.queryByRole('button', { name: 'casa' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'mercado' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'uber' })).toBeInTheDocument();

    await user.type(input, 'mer');

    expect(screen.getByRole('button', { name: 'mercado' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'uber' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'mercado' }));

    expect(onChange).toHaveBeenLastCalledWith(['casa', 'mercado']);
  });
});
