import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { App } from './App';

describe('interface d’administration', () => {
  it('affiche le shell provisoire', () => {
    render(<App />);
    expect(
      screen.getByRole('heading', { name: 'Administration' }),
    ).toBeVisible();
    expect(
      screen.getByText(
        'Le back-office sera disponible dans une prochaine étape.',
      ),
    ).toBeVisible();
  });
});
