import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { App } from './App';

describe('interface publique', () => {
  it('affiche la page de bienvenue', () => {
    render(<App />);
    expect(
      screen.getByRole('heading', { name: 'Bienvenue à la maison.' }),
    ).toBeVisible();
  });
});
