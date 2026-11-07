import { useEffect, useId, useState } from 'react';
import type { MouseEvent } from 'react';
import { PublicApiError, searchPages } from './publicApi';
import type { PublicInvocation, PublicSearchResult } from './publicApi';

const SearchDelayMs = 200;
const MinimumLength = 2;

const categoryLabels: Record<string, string> = {
  page: 'Page',
  piece: 'Pièce',
  emplacement: 'Emplacement',
  procedure: 'Procédure',
  equipement: 'Équipement',
  depannage: 'Dépannage',
  technique: 'Technique',
};

type SearchState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'done'; results: PublicSearchResult[]; corrected: boolean }
  | { status: 'error' };

interface SearchBoxProps {
  invocation: PublicInvocation;
  hrefFor: (slug: string) => string;
  onOpen: (event: MouseEvent<HTMLAnchorElement>, slug: string) => void;
}

export function SearchBox({ invocation, hrefFor, onOpen }: SearchBoxProps) {
  const [text, setText] = useState('');
  const [state, setState] = useState<SearchState>({ status: 'idle' });
  const inputId = useId();
  const resultsId = useId();
  const { context, profile, device } = invocation;
  const trimmed = text.trim();
  const active = trimmed.length >= MinimumLength;

  useEffect(() => {
    if (!active) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      setState({ status: 'loading' });
      searchPages(trimmed, { context, profile, device }, controller.signal)
        .then((response) =>
          setState({
            status: 'done',
            results: response.results,
            corrected: response.corrected,
          }),
        )
        .catch((error: unknown) => {
          if (controller.signal.aborted) return;
          if (error instanceof PublicApiError || error instanceof Error)
            setState({ status: 'error' });
        });
    }, SearchDelayMs);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [active, trimmed, context, profile, device]);

  const updateText = (value: string) => {
    setText(value);
    if (value.trim().length < MinimumLength) setState({ status: 'idle' });
  };

  const shown = active ? state : ({ status: 'idle' } as SearchState);

  return (
    <search className="public-search">
      <label htmlFor={inputId}>Rechercher dans le manuel</label>
      <input
        id={inputId}
        type="search"
        autoComplete="off"
        spellCheck={false}
        placeholder="Un objet, une procédure, une pièce…"
        value={text}
        aria-controls={resultsId}
        onChange={(event) => updateText(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Escape') updateText('');
        }}
      />
      <div id={resultsId} className="search-results" aria-live="polite">
        {shown.status === 'loading' && <p>Recherche…</p>}
        {shown.status === 'error' && (
          <p role="alert">La recherche est temporairement indisponible.</p>
        )}
        {shown.status === 'done' && shown.results.length === 0 && (
          <p>Aucun résultat pour « {trimmed} ».</p>
        )}
        {shown.status === 'done' && shown.results.length > 0 && (
          <>
            {shown.corrected && (
              <p className="search-corrected">
                Résultats approchants pour « {trimmed} ».
              </p>
            )}
            <ul>
              {shown.results.map((result) => (
                <li key={result.pageId}>
                  <a
                    href={hrefFor(result.slug)}
                    onClick={(event) => {
                      setText('');
                      setState({ status: 'idle' });
                      onOpen(event, result.slug);
                    }}
                  >
                    <span className="search-title">
                      {result.title}
                      {result.requiresAuthentication && (
                        <span className="protected-label">Protégée</span>
                      )}
                    </span>
                    <span className="search-meta">
                      {categoryLabels[result.category] ?? result.category}
                      {result.path.length > 0 &&
                        ` · ${result.path.join(' › ')}`}
                    </span>
                    {(result.excerpt ?? result.summary) && (
                      <span className="search-excerpt">
                        {result.excerpt ?? result.summary}
                      </span>
                    )}
                  </a>
                </li>
              ))}
            </ul>
          </>
        )}
      </div>
    </search>
  );
}
